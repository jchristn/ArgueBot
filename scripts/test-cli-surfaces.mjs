import fs from 'node:fs/promises';
import net from 'node:net';
import path from 'node:path';
import { fixtureCertificate } from './fixture-certificate.mjs';
import { spawnSync } from 'node:child_process';
import { setTimeout as delay } from 'node:timers/promises';

const root = path.resolve(import.meta.dirname, '..');
const cli = path.join(root, 'src/ArgueBot.Cli/bin/Release/net10.0/ArgueBot.Cli.dll');
await fs.access(cli);
await fs.mkdir(path.join(root, '.run'), {recursive:true});
const home = await fs.mkdtemp(path.join(root, '.run/cli surfaces 議論-'));
await fs.chmod(home, 0o700);
const listener = net.createServer();
await new Promise((resolve, reject) => listener.once('error', reject).listen(0, '127.0.0.1', resolve));
const port = listener.address().port;
await new Promise(resolve => listener.close(resolve));
await fixtureCertificate(home);
await fs.writeFile(path.join(home, 'arguebot.json'), JSON.stringify({port,tlsEnabled:false}), {mode:0o600});
let started = false;
let checks = 0;
function invoke(words, body) {
  const result = spawnSync('dotnet', [cli, '--home', home + path.sep, ...words], {cwd:root, input: body === undefined ? undefined : typeof body === "string" ? body : JSON.stringify(body), encoding:'utf8', timeout:30000, env: {...process.env, ARGUEBOT_TOKEN:''}});
  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error(`CLI ${words.slice(0,2).join(' ')} failed: ${result.stderr}`);
  return result.stdout;
}
function command(words, body) { return JSON.parse(invoke(words, body)); }
function check(condition, message) {if (!condition) throw new Error(message); checks++;}
async function completed(id, expected = "completed") {
  for(let attempt=0;attempt<100;attempt++) {
    const job = command(['backups','status',id]);
    if(['failed','completed'].includes(job.state)) {
      if(job.state!==expected) throw new Error(`Recovery job expected ${expected}, received ${job.state}: ${job.errorCode}`);
      return job;
    }
    await delay(50);
  }
  throw new Error('Recovery job did not complete before its test deadline.');
}
try {
  started = true;
  check(command(['service','start']).status==='ready', 'CLI did not attach to its isolated service.');
  const blocked = spawnSync('dotnet',[cli,'--home',home,'auth','login','-'],{cwd:root,input:JSON.stringify({tenantId:'ten_local',email:'missing@example.invalid',passwordSha256:'D'.repeat(64)}),encoding:'utf8',timeout:10000,env:{...process.env,ARGUEBOT_TOKEN:''}});
  check(blocked.status!==0 && blocked.stderr.includes('tls_required'), 'The HTTP native helper accepted password-equivalent exchange.');
  const plainExchange = await fetch(`http://127.0.0.1:${port}/v1.0/api/auth/login`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({tenantId:'ten_local',email:'missing@example.invalid',passwordSha256:'D'.repeat(64)})});
  check(plainExchange.status===403 && (await plainExchange.json()).code==='tls_required', 'The HTTP listener accepted password exchange independently of the native helper.');
  command(['service','stop']);
  for(let attempt=0;attempt<100;attempt++) { if(await fs.stat(path.join(home,'runtime/service.json')).then(()=>false,()=>true)) break; await delay(50); }
  await fs.writeFile(path.join(home,'arguebot.json'),JSON.stringify({port,tlsEnabled:true}),{mode:0o600});
  check(command(['service','start']).status==='ready', 'Native CLI did not attach to the owner-pinned HTTPS listener.');
  const builtins = command(['roles','list','Viewer']);
  check(builtins.items.length===1 && builtins.items[0].builtin && builtins.items[0].protected, 'CLI role inspection lost protected built-ins.');
  const role = command(['roles','create','-'], {name:'CLI scoped role %+議論',cloneRoleId:builtins.items[0].id});
  check(role.tenantId==='ten_local' && !role.builtin && !role.protected, 'CLI role clone lost tenant/custom identity.');
  check(command(['roles','list','%+議論']).items.some(item=>item.id===role.id), 'CLI role search lost literal punctuation/Unicode.');
  check(command(['roles','show',role.id]).revision===1, 'CLI role read lost its initial revision.');
  const inactiveRole = command(['roles','update',role.id,'-'], {name:role.name,active:false,expectedRevision:role.revision});
  check(!inactiveRole.active && inactiveRole.revision===2, 'CLI role deactivation lost optimistic concurrency.');
  const permission = command(['permissions','create','-'], {name:'CLI read permission %+議論',resourceTypes:['Conversation'],operations:['Read'],effect:'Permit'});
  check(permission.tenantId==='ten_local' && permission.effect==='Permit' && !permission.protected, 'CLI permission creation lost tenant/effect metadata.');
  check(command(['permissions','list','%+議論']).items.some(item=>item.id===permission.id), 'CLI permission search lost literal metadata.');
  check(command(['permissions','show',permission.id]).operations.includes('Read'), 'CLI permission read lost operation metadata.');
  const mappedRole = command(['roles','set-permissions',role.id,'-'], {permissionIds:[permission.id],expectedRevision:inactiveRole.revision});
  check(mappedRole.revision===3 && command(['roles','permissions',role.id]).items.some(item=>item.permissionId===permission.id), 'CLI mapping replacement lost its role revision or referenced permission.');
  const inactivePermission = command(['permissions','update',permission.id,'-'], {name:permission.name,resourceTypes:permission.resourceTypes,operations:permission.operations,effect:permission.effect,active:false,expectedRevision:permission.revision});
  check(!inactivePermission.active && inactivePermission.revision===2, 'CLI permission deactivation lost its revision.');
  check(command(['roles','permissions',role.id]).items.some(item=>item.permissionId===permission.id && !item.permission.active), 'CLI mapping inspection hid permission activity.');
  command(['roles','set-permissions',role.id,'-'], {permissionIds:[],expectedRevision:mappedRole.revision});
  check(command(['roles','permissions',role.id,'1','false']).items.some(item=>item.permissionId===permission.id && !item.active), 'CLI replacement discarded inactive mapping history.');
  const audit = command(['audit','list','session.issue']);
  check(audit.totalCount >= 1 && audit.items.every(item=>item.operation==='session.issue' && item.sessionId), 'CLI audit inspection lost session accounting.');
  const profile = command(['participants','add','-'], {name:'CLI surface fixture',kind:'cli',provider:'plain',executable:'fixture-not-executed',arguments:['','--literal','議論 with spaces']});
  check(typeof profile.id==='string' && profile.id.length>0 && profile.id.length<=64, 'Participant creation did not return an identifier.');
  profile.name = 'Edited CLI fixture';
  const updated = command(['participants','update',profile.id,'-'], profile);
  check(updated.name===profile.name && JSON.stringify(updated.arguments)===JSON.stringify(profile.arguments), 'Participant update lost its name or empty argument.');
  check(command(['participants','list']).some(item=>item.id===profile.id), 'Participant listing lost the edited profile.');
  command(['participants','delete',profile.id]);
  check(!command(['participants','list']).some(item=>item.id===profile.id), 'Participant deletion retained its reusable record.');
  const identity = command(['users','create','-'], {name:'CLI viewer fixture',email:'cli-viewer@example.invalid',passwordSha256:'A'.repeat(64)});
  check(identity.role==='viewer' && identity.hasPassword && !JSON.stringify(identity).includes('A'.repeat(64)), 'CLI identity creation leaked its password or assigned an elevated role.');
  check(command(['users','list','cli-viewer']).items.some(item=>item.id===identity.id), 'CLI identity paging lost its created user.');
  const assignment = command(['assignments','create',identity.id,'-'], {roleName:'viewer',resourceScope:'Tenant',inheritsToChildren:false});
  check(assignment.userId===identity.id && assignment.tenantId==='ten_local' && assignment.roleName==='Viewer', 'CLI scoped assignment lost principal or canonical role name.');
  check(command(['assignments','list',identity.id]).items.some(item=>item.id===assignment.id), 'CLI scoped assignment paging lost its created record.');
  check(command(['assignments','show',identity.id,assignment.id]).resourceScope==='Tenant', 'CLI assignment read lost scope metadata.');
  const inactiveAssignment = command(['assignments','update',identity.id,assignment.id,'-'], {roleName:assignment.roleName,resourceScope:assignment.resourceScope,active:false,expectedRevision:assignment.revision});
  check(!inactiveAssignment.active && inactiveAssignment.revision===2, 'CLI assignment deactivation lost optimistic concurrency.');

  const enrolled = command(['auth','login','-'], {tenantId:'ten_local',email:identity.email,passwordSha256:'A'.repeat(64)});
  check(typeof enrolled.token==='string' && enrolled.token.startsWith('v1.'), 'CLI password login did not return an opaque session.');
  const disabled = command(['users','update',identity.id,'-'], {name:identity.name,email:identity.email,active:false,expectedRevision:identity.revision});
  check(!disabled.active && disabled.revision===identity.revision+1, 'CLI identity disablement did not advance its revision.');
  const fixture = path.join(root, 'src/Test.Fixtures/bin/Release/net10.0/Test.Fixtures.dll');
  const debaters = ['A', 'B'].map(name => command(['participants','add','-'], {name:`CLI attempt fixture ${name}`,kind:'cli',provider:'plain',executable:'dotnet',arguments:[fixture,'plain']}));
  const conversation = command(['conversations','create','CLI attempt provenance fixture']);
  const run = command(['debate','start',conversation.id,'-'], {participantIds:debaters.map(item=>item.id),maxRounds:1,interventionSeconds:0,idempotencyKey:'cli-fixture-run'});
  let runs;
  for(let attempt=0;attempt<100;attempt++) {
    runs = command(['debate','status',conversation.id]);
    if(runs[0].state==='completed') break;
    if(['paused','failed'].includes(runs[0].state)) throw new Error(`CLI fixture debate stopped: ${runs[0].outcome}`);
    await delay(50);
  }
  check(runs[0].state==='completed', 'CLI fixture debate did not complete.');
  const attempts = command(['debate','attempts',conversation.id,run.id]);
  check(attempts.totalCount===2 && attempts.items.every(item=>item.state==='completed' && item.promptDigest.length===64), 'CLI attempt inspection lost external execution provenance.');
  const longNote = '議論🙂 '.repeat(3000) + '\nFULL CLI TRANSCRIPT TAIL';
  command(['message','send',conversation.id,'-'], longNote);
  const lines = invoke(['conversations','show',conversation.id,'--jsonl']).trim().split(/\r?\n/).map(line=>JSON.parse(line));
  check(lines.length===3 && lines.at(-1).content===longNote && lines.every(item=>!item.contentTruncated), 'CLI JSONL exported previews or lost record boundaries.');
  check(invoke(['conversations','export',conversation.id]).includes(longNote), 'CLI Markdown export silently truncated content.');
  const accepted = command(['backups','create','-'], {name:'CLI recovery fixture'});
  await completed(accepted.id);
  const artifacts = command(['backups','list']);
  check(artifacts.items.some(item=>item.id===accepted.backupId && item.status==='ready'), 'CLI archive creation did not publish a ready record.');
  const jobs = command(['backups','operations',accepted.backupId]);
  check(jobs.totalCount===1 && jobs.items[0].id===accepted.id, 'CLI cannot recover its durable operation metadata.');
  const destination = path.join(home,'downloaded.arguebak');
  command(['backups','download',accepted.backupId,destination]);
  check((await fs.stat(destination)).size>0, 'CLI published an empty archive download.');
  const verified = command(['backups','verify',accepted.backupId,'-'], {});
  await completed(verified.id);
  check(command(['backups','operations',accepted.backupId]).totalCount===2, 'CLI verification did not persist a separate operation.');
  const targetReference = process.env.ARGUEBOT_TEST_CLI_POSTGRESQL_REFERENCE;
  if(targetReference) {
    const connection = process.env[targetReference] || '';
    if(!/^[A-Za-z_][A-Za-z0-9_]{0,127}$/.test(targetReference) || !/(?:^|;)Host=127\.0\.0\.1(?:;|$)/i.test(connection) || !/(?:^|;)Database=arguebot_[A-Za-z0-9_]+(?:;|$)/i.test(connection)) throw new Error('PostgreSQL CLI smoke requires an explicit loopback arguebot_ fixture database reference.');
    const restored = command(['backups','restore',accepted.backupId,'-'], {destinationName:'cli-postgresql-recovered',restoreDatabaseType:'Postgresql',restoreConnectionEnvironmentVariable:targetReference});
    const restoredJob = await completed(restored.id);
    const settings = JSON.parse(await fs.readFile(path.join(home,'restores/cli-postgresql-recovered/arguebot.json'),'utf8'));
    check(restoredJob.targetProvider==='Postgresql' && settings.Database.Type==='Postgresql' && settings.Database.ConnectionEnvironmentVariable===targetReference && settings.Database.ConnectionString==='', 'HTTP/CLI PostgreSQL recovery did not preserve its explicit provider and secret reference.');
    const refused = command(['backups','restore',accepted.backupId,'-'], {destinationName:'must-not-overwrite',restoreDatabaseType:'Postgresql',restoreConnectionEnvironmentVariable:targetReference});
    check((await completed(refused.id,'failed')).errorCode==='restore_target_not_empty', 'HTTP/CLI PostgreSQL recovery did not refuse a populated destination.');
  }
  command(['backups','delete',accepted.backupId]);
  check(command(['backups','list']).totalCount===0, 'CLI archive deletion retained metadata.');
  console.log(`CLI surface smoke passed ${checks} assertions against an isolated service.`);
} finally {
  if(started) {
    command(['service','stop']);
    const descriptor = path.join(home,'runtime/service.json');
    for(let attempt=0;attempt<100;attempt++) {
      if(await fs.stat(descriptor).then(()=>false,()=>true)) break;
      await delay(50);
    }
    if(await fs.stat(descriptor).then(()=>true,()=>false)) throw new Error(`Test service did not stop; isolated home retained at ${home}`);
    await delay(100);
  }
  await fs.rm(home,{recursive:true,force:true,maxRetries:20,retryDelay:100});
}

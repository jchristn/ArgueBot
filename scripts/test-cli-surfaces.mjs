import fs from 'node:fs/promises';
import net from 'node:net';
import path from 'node:path';
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
await fs.writeFile(path.join(home, 'arguebot.json'), JSON.stringify({port}), {mode:0o600});
let started = false;
let checks = 0;
function command(words, body) {
  const result = spawnSync('dotnet', [cli, '--home', home + path.sep, ...words], {cwd:root, input: body === undefined ? undefined : JSON.stringify(body), encoding:'utf8', timeout:30000, env: {...process.env, ARGUEBOT_TOKEN:''}});
  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error(`CLI ${words.slice(0,2).join(' ')} failed: ${result.stderr}`);
  return JSON.parse(result.stdout);
}
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

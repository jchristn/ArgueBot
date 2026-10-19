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
async function completed(id) {
  for(let attempt=0;attempt<100;attempt++) {
    const job = command(['backups','status',id]);
    if(job.state==='failed') throw new Error(`Recovery job failed: ${job.errorCode}`);
    if(job.state==='completed') return job;
    await delay(50);
  }
  throw new Error('Recovery job did not complete before its test deadline.');
}
try {
  started = true;
  check(command(['service','start']).status==='ready', 'CLI did not attach to its isolated service.');
  const profile = command(['participants','add','-'], {name:'CLI surface fixture',kind:'cli',provider:'plain',executable:'fixture-not-executed',arguments:['','--literal','議論 with spaces']});
  check(typeof profile.id==='string' && profile.id.length>0 && profile.id.length<=64, 'Participant creation did not return an identifier.');
  profile.name = 'Edited CLI fixture';
  const updated = command(['participants','update',profile.id,'-'], profile);
  check(updated.name===profile.name && JSON.stringify(updated.arguments)===JSON.stringify(profile.arguments), 'Participant update lost its name or empty argument.');
  check(command(['participants','list']).some(item=>item.id===profile.id), 'Participant listing lost the edited profile.');
  command(['participants','delete',profile.id]);
  check(!command(['participants','list']).some(item=>item.id===profile.id), 'Participant deletion retained its reusable record.');
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

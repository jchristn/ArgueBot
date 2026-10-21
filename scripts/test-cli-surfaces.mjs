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
await fs.writeFile(path.join(home, 'arguebot.json'), JSON.stringify({port,tlsEnabled:false,requireAuthentication:true}), {mode:0o600});
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

try {
  started = true;
  const web = invoke(['service','web','--port',String(port)]);
  started = true;
  check(web.includes(`http://127.0.0.1:${port}/dashboard`), 'Cold web startup did not print its actual browser URL.');
  check(!web.includes('#') && !web.includes('connect='), 'Web launcher printed a connection credential.');
  const descriptor = JSON.parse(await fs.readFile(path.join(home,'runtime/service.json'),'utf8'));
  check(command(['service','start']).status==='ready', 'CLI did not attach to its isolated service.');
  const attach = invoke(['service','web','--port',String(port === 65535 ? port-1 : port+1)]);
  const attached = JSON.parse(await fs.readFile(path.join(home,'runtime/service.json'),'utf8'));
  check(attach.includes(`http://127.0.0.1:${port}/dashboard`) && descriptor.ProcessId === attached.ProcessId, 'Web launch did not reuse the existing service and actual port.');
  check(!await fs.access(path.join(home,'runtime/pairing-code')).then(()=>true,()=>false), 'Open service generated a pairing code.');
  for (const removed of ['auth/me','auth/pair','auth/login','users','roles','permissions','audit','backups','backup-operations']) {
    const response = await fetch(`http://127.0.0.1:${port}/v1.0/api/${removed}`, {method: removed.startsWith('auth/') && removed!=='auth/me' ? 'POST' : 'GET'});
    check(response.status===404, `Removed product route ${removed} is still exposed.`);
  }
  for (const [method, suffix] of [['POST','backups'],['POST','backups/bak_fixture/restore'],['POST','backups/bak_fixture/verify'],['DELETE','backups/bak_fixture'],['GET','backups/bak_fixture/download']]) {
    const response = await fetch(`http://127.0.0.1:${port}/v1.0/api/${suffix}`, {method});
    check(response.status===404, `Removed archive operation ${method} ${suffix} is still exposed.`);
  }
  check(!/backups|restore/i.test(invoke(['--help'])), 'CLI help still advertises archive workflows.');
  const server = path.join(root,'src/ArgueBot.Server/bin/Release/net10.0/ArgueBot.Server.dll');
  for (const flag of ['--export-snapshot','--restore-snapshot','--verify-snapshot']) {
    const result = spawnSync('dotnet',[server,flag,path.join(home,'unused.snapshot')],{encoding:'utf8'});
    check(result.status===2 && result.stderr.includes('Unknown argument'), `Retired maintenance flag ${flag} is still accepted.`);
  }
  command(['service','stop']);
  for(let attempt=0;attempt<100;attempt++) { if(await fs.stat(path.join(home,'runtime/service.json')).then(()=>false,()=>true)) break; await delay(50); }
  await fs.writeFile(path.join(home,'arguebot.json'),JSON.stringify({port,tlsEnabled:true,requireAuthentication:true}),{mode:0o600});
  check(command(['service','start']).status==='ready', 'Native CLI did not attach to the owner-pinned HTTPS listener.');
  check(invoke(['service','web']).includes(`https://127.0.0.1:${port}/dashboard`), 'Web attachment lost the configured HTTPS scheme.');
  const mux = command(['participants','add','-'], {name:'Mux CLI fixture',kind:'cli',provider:'mux',executable:'mux',arguments:['print','--output-format','text','--no-stats','--no-session-persistence','--endpoint','fixture endpoint']});
  check(mux.provider==='mux' && mux.arguments.at(-1)==='fixture endpoint', 'Mux profile creation lost its protocol or literal endpoint argument.');
  check(command(['participants','list']).some(item=>item.id===mux.id && item.provider==='mux'), 'Mux profile did not survive open service persistence.');
  command(['participants','delete',mux.id]);
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
  const longNote = '議論🙂 '.repeat(3000) + '\nFULL CLI TRANSCRIPT TAIL';
  command(['message','send',conversation.id,'-'], longNote);
  const lines = invoke(['conversations','show',conversation.id,'--jsonl']).trim().split(/\r?\n/).map(line=>JSON.parse(line));
  check(lines.length===3 && lines.at(-1).content===longNote && lines.every(item=>!item.contentTruncated), 'CLI JSONL exported previews or lost record boundaries.');
  check(invoke(['conversations','export',conversation.id]).includes(longNote), 'CLI Markdown export silently truncated content.');
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

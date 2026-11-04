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
  const titled = command(['conversations','create','CLI titled fixture','--question','Which fixture question should the debate use?']);
  check(titled.title === 'CLI titled fixture' && titled.openingQuestion === 'Which fixture question should the debate use?', 'CLI create must store the title and --question opening question separately.');

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
  const capabilities = command(['capabilities']);
  check(capabilities.debateProtocol === 2 && capabilities.maxParticipants >= 4, 'CLI capabilities do not describe n-agent admission.');
  const credential = command(['configuration','credentials','add','-'], {name:'CLI fictional token',kind:'token',secretValue:'fictional-cli-token'});
  check(!JSON.stringify(credential).includes('fictional-cli-token'), 'CLI echoed an imported credential.');
  const keys = command(['credential-keys','status']);
  const rotated = command(['credential-keys','rotate',keys.activeKeyId]);
  check(rotated.activeKeyId !== keys.activeKeyId && rotated.reencryptedCredentials === 1, 'CLI master-key rotation lost credential records.');
  const retired = command(['credential-keys','retire',rotated.activeKeyId]);
  check(retired.retainedKeyCount === 1 && retired.retiredKeyCount === rotated.retainedKeyCount - 1 && command(['credential-keys','status']).retainedKeyCount === 1, 'CLI key retirement did not remove earlier keys.');
  check(command(['configuration','credentials','list']).find(item => item.id === credential.id).revision === credential.revision, 'Master rotation changed authentication revision.');
  const endpoint = command(['configuration','endpoints','add','-'], {name:'CLI reusable endpoint',protocol:'ollama',baseUrl:process.env.ARGUEBOT_SIMULATION_URL || 'http://127.0.0.1:11434',authBindings:[]});
  check(command(['configuration','endpoints','preview',endpoint.id]).generationUrl.endsWith('/api/chat'), 'CLI request preview lost the native Ollama path.');
  if (process.env.ARGUEBOT_SIMULATION_URL) check(command(['configuration','endpoints','discover',endpoint.id]).models.includes('multi-agent'), 'CLI endpoint discovery did not return advertised models.');
  const model = command(['configuration','models','add','-'], {name:'CLI reusable model',endpointId:endpoint.id,model:'multi-agent',maxOutputBytes:1048576,generation:{maxOutputTokens:2048,temperature:0.25,topP:0.9}});
  check(model.maxOutputBytes === 1048576 && model.generation.maxOutputTokens === 2048 && model.generation.temperature === 0.25 && model.generation.topP === 0.9, 'CLI generation overrides were not preserved.');
  if (process.env.ARGUEBOT_SIMULATION_URL) { const observed = command(['configuration','models','check',model.id,'-'], {idempotencyKey:'cli-model-check',checkTools:false}); check(observed.completionVerified && observed.state === 'completed', 'CLI explicit model check did not observe a completion.'); check(command(['configuration','models','checks',model.id]).length === 1, 'CLI model-check history was not durable.'); }
  const harness = command(['configuration','harnesses','add','-'], {name:'CLI reusable harness',provider:'plain',executable:'dotnet',arguments:[fixture,'plain'],environmentVariables:['ARGUEBOT_FIXTURE_ALLOWED']});
  check(harness.environmentVariables[0] === 'ARGUEBOT_FIXTURE_ALLOWED', 'CLI harness lost its inherited environment names.');
  for (const [kind,item] of [['endpoints',endpoint],['models',model],['harnesses',harness],['credentials',credential]]) check(command(['configuration',kind,'list']).some(value=>value.id===item.id), `CLI ${kind} list lost its created item.`);
  const replacement = command(['configuration','credentials','update',credential.id,'-'], {name:credential.name,kind:credential.kind,revision:credential.revision,secretValue:'fictional-cli-rotated'});
  check(replacement.revision > credential.revision, 'CLI credential rotation did not advance its revision.');
  const revealed = command(['configuration','credentials','reveal',credential.id]);
  check(revealed.secretValue === 'fictional-cli-rotated' && revealed.revision === replacement.revision, 'CLI reveal did not return the current credential value.');
  check(!JSON.stringify(command(['configuration','credentials','list'])).includes('fictional-cli-rotated'), 'Credential lists must not include values after a reveal.');
  command(['configuration','credentials','disable',credential.id,String(replacement.revision)]);
  if (process.env.ARGUEBOT_SIMULATION_URL) {
    const panel = ['Claude','Codex','Gemini','DeepSeek'].map(name=>command(['participants','add','-'], {name:`CLI ${name}`,modelProfileId:model.id}));
    const moderator = command(['participants','add','-'], {name:'CLI orchestrator',modelProfileId:model.id});
    const debate = command(['conversations','create','CLI four-agent consensus']);
    const rejected = spawnSync('dotnet', [cli, '--home', home + path.sep, 'debate','start',debate.id,'-'], {cwd:root, input: JSON.stringify({protocolVersion:2,participantIds:[panel[0].id],orchestratorId:moderator.id,idempotencyKey:'cli-v2-one-seat'}), encoding:'utf8', timeout:30000, env:{...process.env, ARGUEBOT_TOKEN:''}});
    check(rejected.status===2 && rejected.stderr.includes('too_few_participants (participantIds)'), `CLI did not report a field-specific admission error: ${rejected.stderr}`);
    const live = command(['debate','start',debate.id,'-'], {protocolVersion:2,participantIds:panel.map(value=>value.id),orchestratorId:moderator.id,maxRounds:2,interventionSeconds:0,idempotencyKey:'cli-v2-start'});
    const receipt = command(['interject',debate.id,'-'], {text:'Require a reversible rollout.',deliveryMode:'next_turn',pinned:true,idempotencyKey:'cli-v2-context'});
    check(receipt.runId === live.id && receipt.contextEpoch > 0, 'CLI interjection did not target the active debate.');
    check(command(['interject',debate.id,'-'], {text:'Require a reversible rollout.',deliveryMode:'next_turn',pinned:true,idempotencyKey:'cli-v2-context'}).id === receipt.id, 'CLI context retry created a duplicate.');
    let status;
    for (let attempt=0;attempt<200;attempt++) { status=command(['debate','status',debate.id])[0]; if(['completed','paused','failed'].includes(status.state)) break; await delay(25); }
    check(status.state==='completed' && status.outcome==='consensus', 'CLI four-agent debate failed to reach unanimous consensus.');
    check(command(['debate','roster',debate.id,live.id]).filter(value=>value.active).length===4, 'CLI roster omitted a seat.');
    check(command(['debate','proposals',debate.id,live.id])[0].agreements.filter(value=>value.position==='agree').length===4, 'CLI outcome lacked four explicit votes.');
    check(Array.isArray(command(['debate','summaries',debate.id,live.id])) && Array.isArray(command(['debate','clarifications',debate.id,live.id])), 'CLI memory views failed.');
    check(command(['debate','interjections',debate.id]).some(value=>value.id===receipt.id), 'CLI context history lost its receipt.');
  } else console.log('SKIP: Set ARGUEBOT_SIMULATION_URL for the four-agent CLI provider journey.');
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

import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { setTimeout as delay } from 'node:timers/promises';

// Requires a built image. Owns a unique Compose project and removes only its test volumes.
// DOCKER_HOST may select an isolated daemon without changing the user's Docker context.
const root = path.resolve(import.meta.dirname, '..');
const project = `arguebot-test-${process.pid}-${Date.now()}`;
await fs.mkdir(path.join(root, '.run'), { recursive: true });
const directory = await fs.mkdtemp(path.join(root, '.run/container-'));
const override = path.join(directory, 'compose.yaml');
await fs.writeFile(override, 'services:\n  arguebot:\n    ports: !override\n      - "127.0.0.1::4317"\n    restart: "no"\n    environment:\n      ARGUEBOT_FIXTURE_ALLOWED: fictional-allowed\n      ARGUEBOT_FIXTURE_SECRET: fictional-unrelated-secret\n');
const composeArgs = ['compose', '-p', project, '-f', path.join(root, 'docker/compose.yaml'), '-f', override];
let assertions = 0;
let base;
function check(condition, message) { assert.ok(condition, message); assertions++; }
function docker(args, input) {
  const result = spawnSync('docker', args, { cwd: root, input, encoding: 'utf8', timeout: 180000, maxBuffer: 8 * 1024 * 1024 });
  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error(`Docker ${args[0]} failed: ${result.stderr}`);
  return result.stdout.trim();
}
function compose(args, input) { return docker([...composeArgs, ...args], input); }
function exec(args, input) { return compose(['exec', '-T', 'arguebot', ...args], input); }
function cli(args, input) { return JSON.parse(exec(['dotnet', '/app/cli/ArgueBot.Cli.dll', '--home', '/data', ...args], input === undefined ? undefined : JSON.stringify(input))); }
async function api(route, body, method = body === undefined ? 'GET' : 'POST') {
  const response = await fetch(base + '/v2.0/api' + route, { method, headers: { 'Content-Type': 'application/json' }, body: body === undefined ? undefined : JSON.stringify(body), signal: AbortSignal.timeout(45000) });
  assert.ok(response.ok, `API ${method} ${route} returned ${response.status}`);
  return response.json();
}
async function start() {
  compose(['up', '-d', '--no-build', '--wait', '--wait-timeout', '90']);
  base = 'http://' + compose(['port', 'arguebot', '4317']);
  for (let attempt = 0; attempt < 100; attempt++) {
    if (await fetch(base + '/health', { signal: AbortSignal.timeout(1000) }).then(response => response.ok, () => false)) return;
    await delay(100);
  }
  throw new Error('Published loopback health endpoint was unavailable.');
}
function provider() {
  const id = compose(['ps', '-q', 'arguebot']);
  docker(['cp', path.join(root, 'postman/mocks/ollama/default.js'), id + ':/work/provider.cjs']);
  compose(['exec', '-T', '-d', '-e', 'PORT=4491', 'arguebot', 'node', '/work/provider.cjs']);
}
try {
  const started = Date.now();
  await start();
  const container = compose(['ps', '-q', 'arguebot']);
  check(docker(['inspect', '-f', '{{.State.Health.Status}}', container]) === 'healthy', 'The Compose health check did not report healthy after startup.');
  check(Date.now() - started < 60000, 'The service took longer than its health start period to become healthy.');
  check((await fetch(base + '/health', { headers: { Origin: base } })).ok, 'Published-port browser origin was rejected.');
  check((await fetch(base + '/health', { headers: { Origin: 'http://127.0.0.1:1' } })).status === 403, 'Unrelated browser origin was accepted.');
  check(exec(['curl', '-s', '-o', '/dev/null', '-w', '%{http_code}', '-H', 'Host: attacker.invalid', 'http://127.0.0.1:4317/health']) === '403', 'Non-loopback request authority was accepted.');
  check(exec(['id', '-u']) === '10001', 'Container must run without root.');
  const page = await fetch(base + '/dashboard').then(response => response.text());
  check(page.includes('<div id="root">') && /assets\/.+\.js/.test(page), 'Packaged dashboard assets are missing.');
  check(cli(['capabilities']).debateProtocol === 2, 'Packaged CLI cannot attach to the service.');
  check(exec(['dotnet', '/app/tui/ArgueBot.Tui.dll', '--help']).includes('--home'), 'Packaged TUI help cannot launch.');
  for (const executable of ['claude', 'codex', 'gemini']) check(/\d+\.\d+/.test(exec([executable, '--version'])), `Packaged ${executable} is unavailable.`);
  const id = compose(['ps', '-q', 'arguebot']);
  docker(['cp', path.join(root, 'scripts/test-codex-login.mjs'), id + ':/work/test-codex-login.mjs']);
  check(exec(['node', '/work/test-codex-login.mjs']).includes('documents parse'), 'Pinned Codex rejects sanitized credentials.');
  provider();
  const credential = await api('/configuration/credentials', { name: 'Container fictional token', kind: 'token', secretValue: 'fictional-gateway-token' });
  const claude = await api('/configuration/credentials', { name: 'Container fictional Claude login', kind: 'claude_oauth', secretValue: 'fictional-claude-oauth' });
  const codex = await api('/configuration/credentials', { name: 'Container fictional Codex login', kind: 'codex_auth_json', secretValue: JSON.stringify({ OPENAI_API_KEY: 'fictional-codex-key' }) });
  check(!JSON.stringify([credential, claude, codex]).includes('secretValue'), 'Credential import echoes secret fields.');
  const harnessFixture = path.join(directory, 'credentials.cjs');
  await fs.writeFile(harnessFixture, `const fs=require('fs');
process.stdin.resume();
process.stdin.on('end',()=>{
  if(process.env.ARGUEBOT_FIXTURE_ALLOWED!=='fictional-allowed'||process.env.ARGUEBOT_FIXTURE_SECRET)process.exit(6);
  if(process.argv[2]==='claude'){
    if(process.env.CLAUDE_CODE_OAUTH_TOKEN!=='fictional-claude-oauth')process.exit(3);
    console.log(JSON.stringify({type:'result',subtype:'success',result:'Credential injection verified.'}));
  }else{
    const directory=process.env.CODEX_HOME;
    if(!directory||!directory.startsWith('/data/runtime/credential-homes/')||(fs.statSync(directory).mode&0o077))process.exit(4);
    const file=directory+'/auth.json';
    if((fs.statSync(file).mode&0o077)||JSON.parse(fs.readFileSync(file)).OPENAI_API_KEY!=='fictional-codex-key')process.exit(5);
    console.log(JSON.stringify({type:'item.completed',item:{id:'fixture',type:'agent_message',text:'Private credential file verified.'}}));
  }
});\n`);
  docker(['cp', harnessFixture, id + ':/work/credentials.cjs']);
  const credentialModels = [];
  for (const [name, imported] of [['claude', claude], ['codex', codex]]) {
    const harness = await api('/configuration/harnesses', { name: `Container ${name} injection fixture`, provider: name, executable: 'node', arguments: ['/work/credentials.cjs', name], workingDirectory: '/work', credentialId: imported.id, environmentVariables: ['ARGUEBOT_FIXTURE_ALLOWED'] });
    credentialModels.push(await api('/configuration/models', { name: `Container ${name} injection`, harnessId: harness.id, model: '' }));
  }
  const endpoint = await api('/configuration/endpoints', { name: 'Container loopback fixture', protocol: 'ollama', baseUrl: 'http://127.0.0.1:4491', authBindings: [
    { kind: 'bearer', credentialId: credential.id }, { kind: 'api_key', name: 'X-Api-Key', credentialId: credential.id },
    { kind: 'header', name: 'X-Route', value: 'fixture' }, { kind: 'query', name: 'gateway_key', credentialId: credential.id },
  ] });
  const model = await api('/configuration/models', { name: 'Container authenticated model', endpointId: endpoint.id, model: 'multi-agent-auth', generation: { maxOutputTokens: 2048, temperature: 0.25, topP: 0.9 } });
  const seats = [];
  for (const name of ['Claude', 'Codex', 'Gemini', 'DeepSeek', 'Orchestrator']) seats.push(await api('/participants', { name, modelProfileId: model.id }));
  const conversation = await api('/conversations', { title: 'Container persistent consensus' });
  const run = await api(`/conversations/${conversation.id}/runs`, { protocolVersion: 2, participantIds: seats.slice(0, 4).map(seat => seat.id), orchestratorId: seats[4].id, maxRounds: 2, interventionSeconds: 0, idempotencyKey: 'container-run' });
  await api(`/conversations/${conversation.id}/interjections`, { text: 'Require a reversible rollout.', deliveryMode: 'next_turn', pinned: true, idempotencyKey: 'container-context' });
  let state;
  for (let attempt = 0; attempt < 200; attempt++) {
    state = (await api(`/conversations/${conversation.id}/runs`))[0];
    if (['completed', 'paused', 'failed'].includes(state.state)) break;
    await delay(100);
  }
  check(state.state === 'completed' && state.outcome === 'consensus', `Container debate stopped: ${state.state}/${state.outcome}`);
  const proposalPath = `/conversations/${conversation.id}/runs/${run.id}/proposals`;
  check((await api(proposalPath))[0].agreements.filter(vote => vote.position === 'agree').length === 4, 'Container consensus lacks four explicit votes.');
  const key = await api('/configuration/credential-keys');
  const rotated = await api('/configuration/credential-keys/rotate', { expectedKeyId: key.activeKeyId });
  check(rotated.reencryptedCredentials === 3, 'Container rotation did not re-encrypt all credential kinds.');
  check(exec(['node', '-e', 'const fs=require("fs");for(const p of ["/data/secrets","/data/secrets/credential.key","/data/secrets/credential.keys.json"]){const s=fs.statSync(p);if(s.mode&0o077)process.exit(1)}console.log("private")']) === 'private', 'Key material has permissive file modes.');
  compose(['down']);
  await start();
  provider();
  check((await api('/configuration/credential-keys')).activeKeyId === rotated.activeKeyId, 'Replacement lost the rotated key identity.');
  check((await api(proposalPath))[0].agreements.length === 4, 'Replacement lost debate history.');
  check((await api('/configuration/credentials')).length === 3, 'Replacement lost imported credentials.');
  for (const configured of credentialModels) {
    const observation = await api(`/configuration/models/${configured.id}/checks`, { idempotencyKey: 'container-credential-injection', checkTools: false });
    check(observation.completionVerified, `Persisted CLI credential injection failed: ${observation.errorCode}`);
  }
  const observed = await api(`/configuration/models/${model.id}/checks`, { idempotencyKey: 'container-after-replacement', checkTools: false });
  check(observed.completionVerified, 'Encrypted token was not usable after container replacement.');
  const exposed = JSON.stringify(await api('/configuration/credentials')) + compose(['logs', '--no-color']);
  check(!['fictional-gateway-token', 'fictional-claude-oauth', 'fictional-codex-key'].some(value => exposed.includes(value)), 'Container metadata or logs exposed a credential.');
  check(exec(['node', '-e', 'const fs=require("fs");console.log(fs.readdirSync("/data/runtime/credential-homes").length)']) === '0', 'Container retained temporary credential homes.');
  compose(['down']);
  const keyFiles = ['credential.key', 'credential.keys.json'];
  compose(['run', '--rm', '--no-deps', '--entrypoint', 'node', 'arguebot', '-e', `const fs=require('fs');for(const name of ${JSON.stringify(keyFiles)})fs.renameSync('/data/secrets/'+name,'/data/secrets/'+name+'.held')`]);
  compose(['up', '-d', '--no-build']);
  let missingKeyLogs = '';
  for (let attempt = 0; attempt < 100; attempt++) {
    missingKeyLogs = compose(['logs', '--no-color']);
    if (missingKeyLogs.includes('credential encryption key is missing')) break;
    await delay(100);
  }
  check(missingKeyLogs.includes('credential encryption key is missing'), 'Missing keys did not produce a useful startup failure.');
  compose(['down']);
  check(compose(['run', '--rm', '--no-deps', '--entrypoint', 'node', 'arguebot', '-e', `const fs=require('fs');for(const name of ${JSON.stringify(keyFiles)}){if(fs.existsSync('/data/secrets/'+name))process.exit(1);fs.renameSync('/data/secrets/'+name+'.held','/data/secrets/'+name)}console.log('restored')`]) === 'restored', 'Startup replaced missing credential keys.');
  await start();
  provider();
  check((await api(`/configuration/models/${model.id}/checks`, { idempotencyKey: 'container-restored-key', checkTools: false })).completionVerified, 'Restoring the original keys did not recover credential usability.');
  console.log(`Container acceptance passed ${assertions} assertions: packaged surfaces, four-seat consensus, encrypted imports, key rotation and replacement persistence. Provider calls used the local Postman fixture; live logins were not tested.`);
} finally {
  try { await fs.writeFile(path.join(directory, 'container.log'), compose(['logs', '--no-color'])); }
  finally { compose(['down', '--volumes', '--remove-orphans']); }
  console.log(`Container evidence: ${path.relative(root, directory)}`);
}

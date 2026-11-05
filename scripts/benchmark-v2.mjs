// Runs real service/database work against the repository's Postman provider simulation.
// Every database, process and home created here is isolated and removed on exit.
import fs from 'node:fs/promises';
import path from 'node:path';
import net from 'node:net';
import os from 'node:os';
import { createHash } from 'node:crypto';
import { spawn, spawnSync } from 'node:child_process';
import { setTimeout as delay } from 'node:timers/promises';
const root = path.resolve(import.meta.dirname, '..');
const simulation = process.env.ARGUEBOT_SIMULATION_URL;
if (!simulation || new URL(simulation).hostname !== '127.0.0.1') throw new Error('Set ARGUEBOT_SIMULATION_URL to the loopback Postman runner simulation.');
await fs.mkdir(path.join(root, '.run'), { recursive: true });
const home = await fs.mkdtemp(path.join(root, '.run/benchmark-v2-'));
await fs.chmod(home, 0o700);
const pgBin = process.env.ARGUEBOT_BENCHMARK_POSTGRES_BIN;
const provider = pgBin ? 'Postgresql' : 'Sqlite';
const report = { recordedUtc: new Date().toISOString(), platform: os.platform(), architecture: os.arch(), cpus: os.cpus().length, memoryBytes: os.totalmem(), provider, providerFixture: 'Postman multi-agent-slow, 300ms per HTTP completion', endpointCapacity: 2, concurrentRuns: 2, requestCapture: false, pollingResolutionMilliseconds: 50, cases: [] };
report.commit = spawnSync('git', ['rev-parse', 'HEAD'], { cwd: root, encoding: 'utf8' }).stdout.trim();
report.dirty = Boolean(spawnSync('git', ['status', '--porcelain'], { cwd: root, encoding: 'utf8' }).stdout.trim());
const inventory = spawnSync('git', ['ls-files', '-z', '--cached', '--others', '--exclude-standard'], { cwd: root, encoding: 'utf8' });
if (inventory.status !== 0) throw new Error('Cannot identify the benchmark source tree.');
const sourceHash = createHash('sha256');
for (const name of [...new Set(inventory.stdout.split('\0').filter(Boolean))].sort()) {
  sourceHash.update(name + '\0');
  try { sourceHash.update(await fs.readFile(path.join(root, name))); } catch (error) { if (error.code !== 'ENOENT') throw error; sourceHash.update('<deleted>'); }
  sourceHash.update('\0');
}
report.sourceTreeSha256 = sourceHash.digest('hex');
async function port() { const listener = net.createServer(); await new Promise(resolve => listener.listen(0, '127.0.0.1', resolve)); const value = listener.address().port; await new Promise(resolve => listener.close(resolve)); return value; }
function pg(name, args) { const result = spawnSync(path.join(pgBin, name), args, { encoding: 'utf8', timeout: 30000 }); if (result.status !== 0) throw new Error(`${name} failed: ${result.stderr}`); return result.stdout; }
const servicePort = await port();
const base = `http://127.0.0.1:${servicePort}`;
let service; let pgStarted = false; let pgPort;
async function api(suffix, body) { const response = await fetch(base + '/v2.0/api' + suffix, { method: body === undefined ? 'GET' : 'POST', headers: { 'Content-Type': 'application/json' }, body: body === undefined ? undefined : JSON.stringify(body) }); const data = await response.json(); if (!response.ok) throw new Error(`${response.status}: ${data.code || suffix}`); return data; }
async function databaseBytes() {
  if (pgBin) return Number(pg('psql', ['-h', '127.0.0.1', '-p', String(pgPort), '-U', 'arguebot_benchmark', '-d', 'arguebot_benchmark', '-Atc', "SELECT pg_database_size(current_database());"]).trim());
  let total = 0; for (const name of ['arguebot.db', 'arguebot.db-wal', 'arguebot.db-shm']) total += await fs.stat(path.join(home, name)).then(value => value.size, () => 0); return total;
}
function residentBytes() { if (process.platform === 'win32') return null; const value = Number(spawnSync('ps', ['-o', 'rss=', '-p', String(service.pid)], { encoding: 'utf8' }).stdout.trim()); return Number.isFinite(value) ? value * 1024 : null; }
try {
  const settings = { port: servicePort, tlsEnabled: false, allowCli: false, maxParticipants: 32, maxConcurrentRuns: 4, contextMaxCharacters: 32768, contextMaxMessages: 64, requestHistoryEnabled: false, streamBatchCharacters: 128, streamCommitMilliseconds: 25 };
  const environment = { ...process.env };
  if (pgBin) {
    pgPort = await port(); const data = path.join(home, 'pgdata');
    pg('initdb', ['-D', data, '-U', 'arguebot_benchmark', '-A', 'trust', '--no-locale', '-E', 'UTF8']);
    pg('pg_ctl', ['-D', data, '-l', path.join(home, 'postgres.log'), '-o', `-h 127.0.0.1 -p ${pgPort} -k ''`, '-w', 'start']); pgStarted = true;
    pg('createdb', ['-h', '127.0.0.1', '-p', String(pgPort), '-U', 'arguebot_benchmark', 'arguebot_benchmark']);
    environment.ARGUEBOT_BENCHMARK_DATABASE = `Host=127.0.0.1;Port=${pgPort};Database=arguebot_benchmark;Username=arguebot_benchmark`;
    settings.database = { type: 'Postgresql', connectionEnvironmentVariable: 'ARGUEBOT_BENCHMARK_DATABASE' };
  }
  await fs.writeFile(path.join(home, 'arguebot.json'), JSON.stringify(settings), { mode: 0o600 });
  service = spawn('dotnet', [path.join(root, 'src/ArgueBot.Server/bin/Release/net10.0/ArgueBot.Server.dll'), '--home', home], { env: environment, stdio: 'ignore' });
  let ready = false; for (let attempt = 0; attempt < 200; attempt++) { try { ready = (await fetch(base + '/health')).ok; } catch {} if (ready) break; await delay(50); }
  if (!ready) throw new Error('Benchmark service did not start.');
  const endpoint = await api('/configuration/endpoints', { name: 'Benchmark provider', protocol: 'ollama', baseUrl: simulation, maxConcurrentInvocations: 2 });
  const model = await api('/configuration/models', { name: 'Benchmark model', endpointId: endpoint.id, model: 'multi-agent-slow', supportsTools: true });
  const profiles = []; for (let index = 0; index < 33; index++) profiles.push(await api('/participants', { name: 'Benchmark agent ' + index, modelProfileId: model.id }));
  for (const count of [2, 4, 8, 16, 32]) {
    const beforeBytes = await databaseBytes(); const start = performance.now();
    const rows = await Promise.all([0, 1].map(async instance => {
      const conversation = await api('/conversations', { title: `Benchmark ${count} seats / ${instance}` }); const prefix = `/conversations/${conversation.id}`;
      for (let index = 0; index < 40; index++) await api(prefix + '/messages', { action: 'note', text: `Source ${index}: Preserve reversibility and uncertainty. ` + 'evidence '.repeat(110), idempotencyKey: 'source-' + index });
      const previous = await api(prefix + '/events'); let after = previous.at(-1)?.sequence || 0;
      const submitted = performance.now();
      const run = await api(prefix + '/runs', { protocolVersion: 2, participantIds: profiles.slice(0, count).map(item => item.id), orchestratorId: profiles[32].id, maxRounds: 1, interventionSeconds: 0, idempotencyKey: 'benchmark', policy: { maxWallClockSeconds: 180 } });
      const admittedMs = performance.now() - submitted; let interjectionMs = null; let firstTextMs = null; let latest; let peakRssBytes = residentBytes();
      for (let poll = 0; poll < 3600; poll++) {
        latest = await api(prefix + '/runs/' + run.id);
        if (interjectionMs === null && latest.state === 'agent_turn') { const accepted = performance.now(); await api(prefix + '/interjections', { text: 'Retain minority uncertainty in the final decision.', deliveryMode: 'next_turn', pinned: true, idempotencyKey: 'benchmark-context' }); interjectionMs = performance.now() - accepted; }
        const events = await api(prefix + '/events?after=' + after); after = events.at(-1)?.sequence || after;
        if (firstTextMs === null && events.some(event => event.kind === 'delta')) firstTextMs = performance.now() - submitted;
        if (poll % 20 === 0) peakRssBytes = Math.max(peakRssBytes || 0, residentBytes() || 0) || null;
        if (latest.state === 'completed') break;
        if (['paused', 'failed', 'cancelled'].includes(latest.state)) { const failure = await api(prefix + '/runs/' + run.id + '/attempts?pageSize=1'); throw new Error(`Benchmark ${count} paused: ${latest.outcome}; phase ${failure.items[0]?.purpose}; context ${failure.items[0]?.contextCharacters}`); }
        await delay(50);
      }
      if (latest?.outcome !== 'consensus') throw new Error('Benchmark did not reach unanimity.');
      const elapsedMs = performance.now() - submitted;
      const attempts = []; for (let page = 1; ; page++) { const result = await api(prefix + `/runs/${run.id}/attempts?pageSize=100&pageNumber=${page}`); attempts.push(...result.items); if (attempts.length >= result.totalCount) break; }
      const proposals = await api(prefix + '/runs/' + run.id + '/proposals'); if (proposals[0].agreements.length !== count) throw new Error('Benchmark lost a vote.');
      return { count, instance, admittedMs, firstTextMs, interjectionMs, elapsedMs, peakRssBytes, invocations: attempts.length, endpointWaitMilliseconds: attempts.reduce((sum, item) => sum + (item.endpointWaitMilliseconds || 0), 0), maxContextCharacters: Math.max(...attempts.map(item => item.contextCharacters)), compactions: attempts.filter(item => item.purpose === 'compaction').length, usage: latest.usage };
    }));
    report.cases.push({ seats: count, totalElapsedMs: performance.now() - start, databaseGrowthBytes: (await databaseBytes()) - beforeBytes, runs: rows });
    console.log(`${provider}: ${count} seats x 2 completed; ${Math.round(performance.now() - start)}ms.`);
  }
  const latencies = report.cases.flatMap(item => item.runs.map(run => run.interjectionMs)).sort((a, b) => a - b);
  report.interjectionP95Milliseconds = latencies[Math.ceil(latencies.length * 0.95) - 1];
  report.databaseBytes = await databaseBytes();
  const output = path.join(root, `.run/v2-benchmark-${provider.toLowerCase()}.json`); await fs.writeFile(output, JSON.stringify(report, null, 2) + '\n');
  console.log(`Recorded ${report.cases.length} cases; interjection p95 ${report.interjectionP95Milliseconds.toFixed(1)}ms. Results: .run/v2-benchmark-${provider.toLowerCase()}.json`);
} finally {
  if (service) { service.kill('SIGTERM'); if (service.exitCode === null) await Promise.race([new Promise(resolve => service.once('exit', resolve)), delay(5000)]); if (service.exitCode === null) { service.kill('SIGKILL'); await new Promise(resolve => service.once('exit', resolve)); } }
  if (pgStarted) pg('pg_ctl', ['-D', path.join(home, 'pgdata'), '-m', 'fast', '-w', 'stop']);
  await fs.rm(home, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 });
}

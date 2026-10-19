import fs from 'node:fs/promises';
import net from 'node:net';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { setTimeout as delay } from 'node:timers/promises';

const root = path.resolve(import.meta.dirname, '..');
const cli = path.join(root, 'src/ArgueBot.Cli/bin/Release/net10.0/ArgueBot.Cli.dll');
await fs.access(cli);
await fs.mkdir(path.join(root, '.run'), { recursive: true });
const home = await fs.mkdtemp(path.join(root, '.run/authorization-contract-'));
await fs.chmod(home, 0o700);
const listener = net.createServer();
await new Promise((resolve, reject) => listener.once('error', reject).listen(0, '127.0.0.1', resolve));
const port = listener.address().port;
await new Promise(resolve => listener.close(resolve));
await fs.writeFile(path.join(home, 'arguebot.json'), JSON.stringify({ port }), { mode: 0o600 });
function command(words, body) {
  const result = spawnSync('dotnet', [cli, '--home', home, ...words], {
    cwd: root, encoding: 'utf8', timeout: 30000, input: body === undefined ? undefined : JSON.stringify(body),
    env: { ...process.env, ARGUEBOT_TOKEN: '' },
  });
  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error(result.stderr || 'Isolated service command failed.');
  return result.stdout;
}
let started = false;
try {
  started = true;
  command(['service', 'start']);
  const user = JSON.parse(command(['users', 'create', '-'], { name: 'Contract scoped user', email: 'contract-user@example.invalid' }));
  const session = JSON.parse(await fs.readFile(path.join(home, 'runtime/cli-session.json'), 'utf8'));
  const environment = {
    name: 'Disposable local authorization contract',
    values: [
      { key: 'userId', value: user.id, enabled: true },
      { key: 'baseUrl', value: `http://127.0.0.1:${port}`, enabled: true },
      { key: 'token', value: session.token ?? session.Token, enabled: true, type: 'secret' },
    ],
  };
  const file = path.join(home, 'environment.json');
  await fs.writeFile(file, JSON.stringify(environment), { mode: 0o600 });
  const result = spawnSync(process.env.ARGUEBOT_POSTMAN_BIN || 'postman', [
    'collection', 'run', 'assets/postman/arguebot.postman_collection.json',
    '-i', 'Roles', '-i', 'Permissions', '-i', 'Assignments', '-e', file, '--no-report-events',
  ], { cwd: root, encoding: 'utf8', timeout: 60000 });
  if (result.stdout) process.stdout.write(result.stdout);
  if (result.stderr) process.stderr.write(result.stderr);
  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error('Local authorization collection failed.');
} finally {
  if (started) {
    command(['service', 'stop']);
    const descriptor = path.join(home, 'runtime/service.json');
    for (let attempt = 0; attempt < 100; attempt++) {
      if (await fs.stat(descriptor).then(() => false, () => true)) break;
      await delay(50);
    }
    if (await fs.stat(descriptor).then(() => true, () => false)) throw new Error(`Isolated service did not stop; fixture retained at ${home}`);
  }
  await fs.rm(home, { recursive: true, force: true, maxRetries: 20, retryDelay: 100 });
}

import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import net from 'node:net';
import os from 'node:os';
import { spawnSync } from 'node:child_process';
import { setTimeout as delay } from 'node:timers/promises';

const source = process.argv[2];
if (!source) throw new Error('Usage: node scripts/test-packaged.mjs EXTRACTED_PACKAGE_DIRECTORY');
const bundle = path.resolve(source);
const directory = await fs.mkdtemp(path.join(os.tmpdir(), 'arguebot packaged 議論 '));
const home = path.join(directory, 'workspace');
const extension = process.platform === 'win32' ? '.exe' : '';
let assertions = 0;
let started = false;
function invoke(project, args) {
  const result = spawnSync(path.join(bundle, 'ArgueBot.' + project + extension), args, { cwd: directory, encoding: 'utf8', timeout: 30000 });
  if (result.error) throw result.error;
  assert.equal(result.status, 0, `${project} ${args.slice(0, 2).join(' ')} failed: ${result.stderr}`);
  return result.stdout.trim();
}
function cli(args) { return invoke('Cli', ['--home', home, ...args]); }
function check(value, message) { assert.ok(value, message); assertions++; }
const listener = net.createServer();
await new Promise((resolve, reject) => listener.once('error', reject).listen(0, '127.0.0.1', resolve));
const port = listener.address().port;
await new Promise(resolve => listener.close(resolve));
try {
  await fs.mkdir(home, { recursive: true, mode: 0o700 });
  await fs.writeFile(path.join(home, 'arguebot.json'), JSON.stringify({ port }), { mode: 0o600 });
  for (const project of ['Server', 'Cli', 'Tui']) check(invoke(project, ['--help']).includes('--home'), `${project} cannot start from a package.`);
  started = true;
  check(cli(['service', 'web', '--port', String(port)]).includes(`http://127.0.0.1:${port}/dashboard`), 'Packaged CLI cannot locate and start its sibling service.');
  const capabilities = JSON.parse(cli(['capabilities']));
  check(capabilities.debateProtocol === 2, 'Packaged service lost v2 capabilities.');
  const page = await fetch(`http://127.0.0.1:${port}/dashboard`).then(response => response.text());
  const asset = page.match(/src="([^"]+\.js)"/);
  check(Boolean(asset), 'Packaged dashboard entry point is missing.');
  check((await fetch(`http://127.0.0.1:${port}${asset[1]}`)).ok, 'Packaged dashboard JavaScript was not included.');
  const conversation = JSON.parse(cli(['conversations', 'create', 'Packaged lifecycle 議論']));
  check(conversation.title === 'Packaged lifecycle 議論', 'Packaged SQLite storage lost Unicode.');
  cli(['service', 'stop']);
  const descriptor = path.join(home, 'runtime', 'service.json');
  for (let attempt = 0; attempt < 100 && await fs.access(descriptor).then(() => true, () => false); attempt++) await delay(50);
  check(cli(['service', 'web']).includes(`http://127.0.0.1:${port}/dashboard`), 'Packaged service did not retain its configured port.');
  check(JSON.parse(cli(['conversations', 'list'])).items.some(item => item.id === conversation.id), 'Packaged restart lost its database.');
  if (['darwin', 'linux'].includes(process.platform) && await fs.access(path.join(bundle, 'arguebot-web')).then(() => true, () => false)) {
    const commands = path.join(directory, 'commands'); await fs.mkdir(commands);
    await fs.writeFile(path.join(commands, process.platform === 'darwin' ? 'open' : 'xdg-open'), '#!/bin/sh\nprintf "%s\\n" "$1"\n', { mode: 0o700 });
    const opened = spawnSync(path.join(bundle, 'arguebot-web'), [], { encoding: 'utf8', timeout: 30000, env: { ...process.env, ARGUEBOT_HOME: home, PATH: commands + path.delimiter + process.env.PATH } });
    check(opened.status === 0 && opened.stdout.trim() === `http://127.0.0.1:${port}/dashboard`, 'Packaged app launcher did not open the matching workspace URL.');
  }
  if (process.platform === 'win32') {
    const opened = spawnSync('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', 'function Start-Process { param([string] $FilePath) Write-Output $FilePath }; . $env:ARGUEBOT_TEST_LAUNCHER'], { encoding: 'utf8', timeout: 30000, env: { ...process.env, ARGUEBOT_HOME: home, ARGUEBOT_TEST_LAUNCHER: path.join(bundle, 'arguebot-web.ps1') } });
    check(opened.status === 0 && opened.stdout.trim() === `http://127.0.0.1:${port}/dashboard`, 'Windows browser launcher did not open the matching workspace URL.');
  }

  console.log(`Packaged native lifecycle passed ${assertions} assertions on ${process.platform}/${process.arch}: executable startup, sibling service launch, dashboard assets, SQLite persistence, Unicode paths and restart.`);
} finally {
  if (started) {
    cli(['service', 'stop']);
    const descriptor = path.join(home, 'runtime', 'service.json');
    for (let attempt = 0; attempt < 100 && await fs.access(descriptor).then(() => true, () => false); attempt++) await delay(50);
    if (await fs.access(descriptor).then(() => true, () => false)) throw new Error('Packaged test service did not stop; isolated home retained.');
  }
  await fs.rm(directory, { recursive: true, force: true, maxRetries: 20, retryDelay: 100 });
}

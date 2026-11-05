import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import net from 'node:net';
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { setTimeout as delay } from 'node:timers/promises';

const artifact = path.resolve(process.argv[2] || '');
const format = path.extname(artifact);
if (process.platform !== 'linux' || !['.deb', '.rpm'].includes(format)) throw new Error('Run on an isolated Linux host: node scripts/test-linux-installer.mjs PACKAGE.deb|PACKAGE.rpm');
await fs.access(artifact);
const installed = '/opt/arguebot';
const existing = await fs.readdir(installed, { recursive: true, withFileTypes: true }).catch(error => { if (error.code === 'ENOENT') return []; throw error; });
assert.equal(existing.some(entry => !entry.isDirectory()), false, 'An existing ArgueBot installation must not be changed by this test.');
const directory = await fs.mkdtemp(path.join(os.tmpdir(), 'arguebot installer 議論 '));
const home = path.join(directory, 'workspace');
const descriptor = path.join(home, 'runtime', 'service.json');
let ownsInstall = false;
let started = false;
let assertions = 0;
function run(command, args, privileged = false) {
  const useSudo = privileged && process.getuid() !== 0;
  const result = spawnSync(useSudo ? 'sudo' : command, useSudo ? ['-n', command, ...args] : args, { encoding: 'utf8', timeout: 180000 });
  if (result.error) throw result.error;
  assert.equal(result.status, 0, `${command} failed: ${result.stderr}\n${result.stdout}`);
  return result.stdout.trim();
}
function install() {
  ownsInstall = true;
  if (format === '.deb') run('dpkg', ['-i', artifact], true);
  else run('rpm', ['-U', '--replacepkgs', '--nodeps', artifact], true);
}
function remove() {
  if (format === '.deb') run('dpkg', ['-r', 'arguebot'], true);
  else run('rpm', ['-e', 'arguebot'], true);
  ownsInstall = false;
}
function cli(args) { return run(path.join(installed, 'ArgueBot.Cli'), ['--home', home, ...args]); }
async function stop() {
  cli(['service', 'stop']);
  for (let attempt = 0; attempt < 100 && await fs.access(descriptor).then(() => true, () => false); attempt++) await delay(50);
  assert.equal(await fs.access(descriptor).then(() => true, () => false), false, 'Installer test service did not stop.');
  started = false;
}
function check(value, message) { assert.ok(value, message); assertions++; }
try {
  install();
  for (const name of ['arguebot.desktop', 'arguebot-web.desktop']) run('desktop-file-validate', [path.join('/usr/share/applications', name)]);
  check(await fs.readFile('/usr/share/applications/arguebot.desktop', 'utf8').then(value => value.includes('Exec=arguebot-tui')), 'Installed terminal shortcut does not launch the TUI.');
  console.log(run(process.execPath, [path.resolve('scripts/test-packaged.mjs'), installed]));
  await fs.mkdir(home, { mode: 0o700 });
  const listener = net.createServer();
  await new Promise((resolve, reject) => listener.once('error', reject).listen(0, '127.0.0.1', resolve));
  const port = listener.address().port;
  await new Promise(resolve => listener.close(resolve));
  await fs.writeFile(path.join(home, 'arguebot.json'), JSON.stringify({ port }), { mode: 0o600 });
  started = true;
  cli(['service', 'web']);
  const conversation = JSON.parse(cli(['conversations', 'create', 'Preserved by installer 議論']));
  await stop();
  install();
  started = true;
  cli(['service', 'web']);
  check(JSON.parse(cli(['conversations', 'list'])).items.some(item => item.id === conversation.id && item.title === conversation.title), 'Package replacement lost workspace data.');
  await stop();
  const digest = async () => createHash('sha256').update(await fs.readFile(path.join(home, 'arguebot.db'))).digest('hex');
  const before = await digest();
  remove();
  check(await digest() === before, 'Uninstallation changed the retained database.');
  check(!await fs.access(path.join(installed, 'ArgueBot.Server')).then(() => true, () => false), 'Uninstallation left the service executable installed.');
  console.log(`Linux ${format.slice(1)} installation, same-version replacement and removal passed ${assertions} assertions, plus the packaged lifecycle checks. Workspace preserved.${format === '.rpm' ? ' RPM dependency resolution is not asserted (--nodeps).' : ''}`);
} finally {
  if (started) await stop();
  if (ownsInstall) remove();
  await fs.rm(directory, { recursive: true, force: true });
}

import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import { spawnSync } from 'node:child_process';

const root = path.resolve(import.meta.dirname, '..');
const runtime = process.argv[2];
if (!/^(win|linux)-(x64|arm64)$/.test(runtime || '')) throw new Error('Choose win-x64, win-arm64, linux-x64 or linux-arm64. macOS uses the installed platform tools.');
const directory = path.join(root, '.run', 'package-tools');
await fs.mkdir(directory, { recursive: true });
const toolchain = JSON.parse(await fs.readFile(path.join(root, 'packaging/toolchain.json'), 'utf8'));
async function download(name) {
  const definition = toolchain[name];
  const response = await fetch(definition.url, { signal: AbortSignal.timeout(120000) });
  if (!response.ok) throw new Error(`Downloading ${name} returned ${response.status}.`);
  const bytes = Buffer.from(await response.arrayBuffer());
  if (crypto.createHash('sha256').update(bytes).digest('hex') !== definition.sha256) throw new Error(`${name} checksum mismatch.`);
  const file = path.join(directory, path.basename(new URL(definition.url).pathname));
  await fs.writeFile(file, bytes, { mode: 0o700 }); return file;
}
if (process.platform === 'win32' && runtime.startsWith('win-')) {
  const installer = await download('inno');
  const destination = path.join(directory, 'inno');
  const result = spawnSync(installer, ['/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER', '/DIR=' + destination], { stdio: 'inherit', timeout: 120000 });
  if (result.error || result.status !== 0) throw result.error || new Error('Inno Setup installation failed.');
  if (process.env.GITHUB_PATH) await fs.appendFile(process.env.GITHUB_PATH, destination + '\n');
  console.log(`Inno Setup installed at ${destination}. Add this directory to PATH for local builds.`);
} else if (process.platform === 'linux' && runtime.startsWith('linux-')) {
  const tool = await download(process.arch === 'arm64' ? 'appimageToolArm64' : 'appimageToolX64');
  const runtimeFile = await download(runtime.endsWith('arm64') ? 'appimageRuntimeArm64' : 'appimageRuntimeX64');
  if (process.env.GITHUB_ENV) await fs.appendFile(process.env.GITHUB_ENV, `ARGUEBOT_APPIMAGETOOL=${tool}\nARGUEBOT_APPIMAGE_RUNTIME=${runtimeFile}\nAPPIMAGE_EXTRACT_AND_RUN=1\n`);
  console.log(`Use ARGUEBOT_APPIMAGETOOL=${tool}, ARGUEBOT_APPIMAGE_RUNTIME=${runtimeFile}, APPIMAGE_EXTRACT_AND_RUN=1. Install fpm 1.17.0 and rpm using the documented native toolchain.`);
} else throw new Error('Install package tools on the matching native operating system.');

import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';

// Offline parser compatibility only. Never read a developer's actual CLI login or invoke a model.
const executable = process.env.ARGUEBOT_CODEX_EXECUTABLE || 'codex';
const encode = value => Buffer.from(JSON.stringify(value)).toString('base64url');
const jwt = encode({ alg: 'none' }) + '.' + encode({ exp: 4102444800, email: 'fixture@example.invalid', 'https://api.openai.com/auth': { chatgpt_account_id: 'fixture-account', chatgpt_plan_type: 'plus' } }) + '.fictional';
const home = await fs.mkdtemp(path.join(os.tmpdir(), 'arguebot-codex-parser-'));
try {
  await fs.chmod(home, 0o700);
  await fs.writeFile(path.join(home, 'config.toml'), 'cli_auth_credentials_store = "file"\n', { mode: 0o600 });
  const environment = { ...process.env, CODEX_HOME: home };
  for (const key of ['OPENAI_API_KEY', 'CODEX_API_KEY', 'OPENAI_ACCESS_TOKEN']) delete environment[key];
  const version = spawnSync(executable, ['--version'], { env: environment, encoding: 'utf8', timeout: 15000 });
  if (version.status !== 0) throw new Error('The configured Codex executable is unavailable.');
  for (const kind of ['api_key', 'chatgpt']) {
    const document = kind === 'api_key' ? { OPENAI_API_KEY: 'fictional-parser-only-key' } : { auth_mode: 'chatgpt', tokens: { access_token: jwt, id_token: jwt, account_id: 'fixture-account', refresh_token: '' }, last_refresh: new Date().toISOString() };
    await fs.writeFile(path.join(home, 'auth.json'), JSON.stringify(document), { mode: 0o600 });
    const check = spawnSync(executable, ['login', 'status'], { env: environment, encoding: 'utf8', timeout: 15000 });
    if (check.status !== 0) throw new Error('Codex rejected the isolated ' + kind + ' run document. Credential output is intentionally omitted.');
    const after = JSON.parse(await fs.readFile(path.join(home, 'auth.json'), 'utf8'));
    if (kind === 'chatgpt' && after.tokens.refresh_token !== '') throw new Error('The child login unexpectedly acquired refresh authority.');
  }
  console.log(version.stdout.trim() + ': API-key and sanitized ChatGPT login documents parse. No live authentication or generation was tested.');
} finally { await fs.rm(home, { recursive: true, force: true }); }

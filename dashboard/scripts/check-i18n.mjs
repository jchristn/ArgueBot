import fs from 'node:fs';
import path from 'node:path';
const root = path.resolve(import.meta.dirname, '../..');
const en = JSON.parse(fs.readFileSync(path.join(root, 'assets/i18n/en.json'), 'utf8'));
const registry = JSON.parse(fs.readFileSync(path.join(root, 'assets/i18n/registry.json'), 'utf8'));
const missing = [];
function inspect(directory) {
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const full = path.join(directory,entry.name);
    if (entry.isDirectory()) inspect(full);
    else if (/\.[jt]sx?$/.test(entry.name)) {
      const source = fs.readFileSync(full,'utf8');
      for (const match of source.matchAll(/(?:\bt\(\s*['"]([^'"]+)['"]|\b(?:label|help|title)=['"]([a-z][a-zA-Z0-9]+\.[^'"]+)['"])/g)) {
        const key = match[1] || match[2];
        if (key.endsWith('.') || key.includes(' ')) continue;
        if (!(key in en)) missing.push(`${full}: ${key}`);
      }
      if (source.includes('\uFFFD')) missing.push(`${full}: corrupted UTF-8`);
    }
  }
}
inspect(path.join(root,'dashboard/src'));
for (const locale of registry) {
  if (!locale.code || !locale.dir || !locale.nativeName) missing.push('Invalid locale registry record');
}
if (missing.length) { console.error(missing.join('\n')); process.exit(1); }
console.log(`Checked ${Object.keys(en).length} source strings and ${registry.length} locales; no missing literal keys.`);

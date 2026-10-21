import fs from 'node:fs';
import path from 'node:path';
const root = path.resolve(import.meta.dirname,'..');
const references = new Map();
for (const project of fs.readdirSync(path.join(root,'src'),{withFileTypes:true})) {
  if (!project.isDirectory()) continue;
  const lock = path.join(root,'src',project.name,'packages.lock.json');
  if (!fs.existsSync(lock)) continue;
  for (const packages of Object.values(JSON.parse(fs.readFileSync(lock,'utf8')).dependencies)) {
    for (const [name,entry] of Object.entries(packages)) {
      if (!entry.resolved || !entry.contentHash) continue;
      const key = `nuget:${name.toLowerCase()}:${entry.resolved}`;
      const nuspec = path.join(root,'.run/nuget',name.toLowerCase(),entry.resolved,`${name.toLowerCase()}.nuspec`);
      if (!fs.existsSync(nuspec)) throw new Error(`Restore the locked solution before generating licenses: ${name} ${entry.resolved}`);
      const source = fs.readFileSync(nuspec,'utf8');
      const expression = source.match(/<license\b[^>]*type="expression"[^>]*>([^<]+)<\/license>/)?.[1];
      const file = source.match(/<license\b[^>]*type="file"[^>]*>([^<]+)<\/license>/)?.[1];
      const url = source.match(/<licenseUrl>([^<]+)<\/licenseUrl>/)?.[1];
      const license = (expression || (file ? `See package file: ${file}` : url) || 'REVIEW REQUIRED').replaceAll('&amp;','&');
      const development = !project.name.startsWith('ArgueBot.');
      const previous = references.get(key);
      references.set(key,{name,version:entry.resolved,ecosystem:'nuget',license,source:`https://www.nuget.org/packages/${name}/${entry.resolved}`,integrity:`sha512-${entry.contentHash}`,development:development && (previous?.development ?? true)});
    }
  }
}
for (const folder of ['dashboard']) {
  const lock = JSON.parse(fs.readFileSync(path.join(root,folder,'package-lock.json'),'utf8'));
  for (const [location,entry] of Object.entries(lock.packages || {})) {
    if (!location || !entry.version || !entry.integrity) continue;
    const name = location.split('node_modules/').at(-1);
    const key = `npm:${name}:${entry.version}`; const previous = references.get(key);
    references.set(key,{name,version:entry.version,ecosystem:'npm',license:entry.license || 'REVIEW REQUIRED',source:`https://www.npmjs.com/package/${name}/v/${entry.version}`,integrity:entry.integrity,development:!!entry.dev && (previous?.development ?? true)});
  }
}
const entries = [...references.values()].sort((a,b) => `${a.ecosystem}:${a.name}:${a.version}`.localeCompare(`${b.ecosystem}:${b.name}:${b.version}`,'en'));
fs.writeFileSync(path.join(root,'assets/dependencies.json'),JSON.stringify(entries,null,2)+'\n');
const pending = entries.filter(entry => entry.license === 'REVIEW REQUIRED');
console.log(`Recorded ${entries.length} exact dependency versions; ${pending.length} license declarations need review.`);
if (pending.length) console.log(pending.map(entry => `${entry.ecosystem}: ${entry.name} ${entry.version}`).join('\n'));

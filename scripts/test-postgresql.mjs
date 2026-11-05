// Local alternative to the CI/PostgreSQL container. Never attaches to an existing cluster.
import fs from 'node:fs/promises';
import path from 'node:path';
import net from 'node:net';
import {spawnSync} from 'node:child_process';
const root=path.resolve(import.meta.dirname,'..');
const bin=process.env.ARGUEBOT_POSTGRES_BIN;
if(!bin)throw new Error('Set ARGUEBOT_POSTGRES_BIN to a PostgreSQL bin directory.');
const home=await fs.mkdtemp(path.join(root,'.run/postgresql-v2-'));await fs.chmod(home,0o700);
const listener=net.createServer();await new Promise(resolve=>listener.listen(0,'127.0.0.1',resolve));const port=listener.address().port;await new Promise(resolve=>listener.close(resolve));
function pg(name,args){const value=spawnSync(path.join(bin,name),args,{encoding:'utf8',timeout:30000});if(value.status!==0)throw new Error(`${name} failed: ${value.stderr}`);}
let started=false;
try{
  pg('initdb',['-D',path.join(home,'data'),'-U','arguebot_test','-A','trust','--no-locale','-E','UTF8']);
  pg('pg_ctl',['-D',path.join(home,'data'),'-l',path.join(home,'postgres.log'),'-o',`-h 127.0.0.1 -p ${port} -k ''`,'-w','start']);started=true;
  pg('createdb',['-h','127.0.0.1','-p',String(port),'-U','arguebot_test','arguebot_test']);
  const result=spawnSync('dotnet',[path.join(root,'src/Test.Automated/bin/Release/net10.0/Test.Automated.dll'),'--suite','Providers','--results',path.join(root,'.run/v2-postgresql-results.json')],{stdio:'inherit',timeout:120000,env:{...process.env,ARGUEBOT_TEST_POSTGRESQL:`Host=127.0.0.1;Port=${port};Database=arguebot_test;Username=arguebot_test`}});
  if(result.error)throw result.error;process.exitCode=result.status??1;
}finally{
  if(started)pg('pg_ctl',['-D',path.join(home,'data'),'-m','fast','-w','stop']);
  await fs.rm(home,{recursive:true,force:true});
}

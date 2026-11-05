import fs from 'node:fs/promises';
import path from 'node:path';
import net from 'node:net';
import {spawn,spawnSync} from 'node:child_process';
import {setTimeout as delay} from 'node:timers/promises';
const root=path.resolve(import.meta.dirname,'..');
const home=await fs.mkdtemp(path.join(root,'.run/postman-v2-'));
const listener=net.createServer();await new Promise(resolve=>listener.listen(0,'127.0.0.1',resolve));const port=listener.address().port;await new Promise(resolve=>listener.close(resolve));
await fs.writeFile(path.join(home,'arguebot.json'),JSON.stringify({port,tlsEnabled:false}),{mode:0o600});
const service=spawn('dotnet',[path.join(root,'src/ArgueBot.Server/bin/Release/net10.0/ArgueBot.Server.dll'),'--home',home],{stdio:'ignore'});
try {
  let ready=false;for(let i=0;i<100;i++){try{ready=(await fetch(`http://127.0.0.1:${port}/health`)).ok;}catch{}if(ready)break;await delay(50);}
  if(!ready)throw new Error('Disposable service did not become ready.');
  const result=spawnSync('postman',['collection','run',path.join(root,'assets/postman/v2-acceptance.postman_collection.json'),'--env-var',`baseUrl=http://127.0.0.1:${port}`,'--env-var','isolatedFixture=true','--env-var',`simulationUrl=${process.env.ARGUEBOT_SIMULATION_URL||'http://127.0.0.1:4491/ollama'}`,'--no-report-events','--timeout','90000'],{stdio:'inherit',timeout:100000});
  if(result.error)throw result.error;process.exitCode=result.status??1;
}finally{
  service.kill('SIGTERM');await Promise.race([new Promise(resolve=>service.once('exit',resolve)),delay(5000)]);
  if(service.exitCode===null)service.kill('SIGKILL');
  await fs.rm(home,{recursive:true,force:true,maxRetries:10,retryDelay:100});
}

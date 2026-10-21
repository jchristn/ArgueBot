import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import net from 'node:net';
import { spawnSync } from 'node:child_process';
const root=path.resolve(import.meta.dirname,'..');
const home=await fs.mkdtemp(path.join(os.tmpdir(),'arguebot-stop-web '));
const cli=path.join(root,'src/ArgueBot.Cli/bin/Release/net10.0/ArgueBot.Cli.dll');
const run=(cmd,args,env={})=>{const result=spawnSync(cmd,args,{cwd:root,encoding:'utf8',timeout:30000,env:{...process.env,...env}});if(result.status!==0)throw new Error(result.stderr||result.stdout||String(result.error));return result.stdout;};
try{
 const server=net.createServer();await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));const port=server.address().port;await new Promise(resolve=>server.close(resolve));
 run('dotnet',[cli,'--home',home,'service','web','--port',String(port)]);
 const bin=path.join(home,'fake bin');await fs.mkdir(bin);
 const log=path.join(home,'docker.log');
 await fs.writeFile(path.join(bin,'docker'),'#!/bin/sh\nprintf "%s\\n" "$*" >> "$ARGUEBOT_STOP_DOCKER_LOG"\nexit 0\n',{mode:0o755});
 const env={PATH:bin+path.delimiter+process.env.PATH,ARGUEBOT_STOP_DOCKER_LOG:log};
 run('bash',[path.join(root,'stop-web.sh'),'--home',home],env);
 let alive=false;try{await fetch(`http://127.0.0.1:${port}/health`,{signal:AbortSignal.timeout(2000)});alive=true;}catch{}
 if(alive)throw new Error('Stop script left its isolated service running.');
 if(!run('bash',[path.join(root,'stop-web.sh'),'--home',home],env).includes('already stopped'))throw new Error('Repeated stop was not idempotent.');
 if(await fs.stat(log).then(()=>true,()=>false))throw new Error('Stop script unexpectedly invoked Docker.');
 console.log('Stop script verified against a real isolated service; repeated stop succeeds and Docker is never invoked.');
}finally{
 spawnSync('dotnet',[cli,'--home',home,'service','stop'],{cwd:root,timeout:20000,encoding:'utf8'});
 await fs.rm(home,{recursive:true,force:true,maxRetries:20,retryDelay:100});
}

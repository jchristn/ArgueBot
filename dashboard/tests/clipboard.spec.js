import { test, expect } from '@playwright/test';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs/promises';
import net from 'node:net';
import path from 'node:path';
let home, origin;
const cli = path.resolve('../src/ArgueBot.Cli/bin/Release/net10.0/ArgueBot.Cli.dll');
const invoke = (...args) => spawnSync('dotnet', [cli, '--home', home, ...args], { encoding:'utf8', timeout:10000, env:{...process.env, ARGUEBOT_TOKEN:''} });
test.beforeAll(async () => {
 home = await fs.mkdtemp(path.resolve('../.run/clipboard workspace '));
 const server=net.createServer(); await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve)); const port=server.address().port; await new Promise(resolve=>server.close(resolve));
 origin=`http://127.0.0.1:${port}`; expect(invoke('service','web','--port',String(port)).status).toBe(0);
});
test.afterEach(async ({ page }) => { await page.unrouteAll({ behavior: 'wait' }); });
test.afterAll(async()=>{if(home){invoke('service','stop');await fs.rm(home,{recursive:true,force:true,maxRetries:20,retryDelay:100});}});
async function seed(request,count=1){
 const conversation=await(await request.post(origin+'/v1.0/api/conversations',{data:{title:'Clipboard evidence'}})).json(); const messages=[];
 for(let index=0;index<count;index++) messages.push(await(await request.post(origin+`/v1.0/api/conversations/${conversation.id}/messages`,{data:{action:'note',text:index===0?'Full turn 議論\n'+'Long text without truncation. '.repeat(170)+'FINAL_MARKER':`Turn ${index}`,idempotencyKey:`clipboard-${index}`}})).json());
 return {conversation,messages};
}
async function readClipboard(context,page){
 await context.grantPermissions(['clipboard-read','clipboard-write'],{origin});const reader=await context.newPage();
 try{await reader.goto(origin+'/dashboard');await reader.bringToFront();return await reader.evaluate(()=>navigator.clipboard.readText());}finally{await reader.close();await page.bringToFront();}
}
test('HTTP loopback copies full paged conversations, full turns and monospace IDs',async({page,context,request})=>{
 const {conversation,messages}=await seed(request,103); await context.grantPermissions(['clipboard-read','clipboard-write'],{origin});
 await page.goto(origin+`/dashboard/conversations/${conversation.id}`);await page.getByRole('button',{name:'Copy conversation',exact:true}).click();await expect(page.getByRole('button',{name:'Copied',exact:true})).toBeVisible();
 const whole=await readClipboard(context,page);expect(whole).toContain('FINAL_MARKER');expect(whole).toContain('Turn 102');expect(whole.match(/## user \(note\)/g)).toHaveLength(103);
 await page.getByRole('button',{name:'First page',exact:true}).click();const turn=page.locator('article.message').filter({hasText:'Full turn 議論'});
 await turn.getByRole('button',{name:'Copy turn',exact:true}).click();await expect(turn.getByRole('button',{name:'Copied',exact:true})).toBeVisible();expect(await readClipboard(context,page)).toBe(messages[0].content);
 const id=turn.locator('.copyable-id').filter({hasText:messages[0].id});await id.getByRole('button',{name:'Copy ID',exact:true}).click();expect(await readClipboard(context,page)).toBe(messages[0].id);expect(await id.locator('code').evaluate(node=>getComputedStyle(node).fontFamily)).toMatch(/mono|Consolas/);
});
test('insecure non-localhost HTTP starts, saves and copies without the clipboard API',async({page,context,request})=>{
 const {conversation,messages}=await seed(request);
 await page.route('http://arguebot.test/**',async route=>{
  const incoming=new URL(route.request().url());if(incoming.pathname.endsWith('/events'))return route.fulfill({status:200,contentType:'text/event-stream',body:': heartbeat\n\n'});
  const headers={...route.request().headers(),origin};delete headers.host;const response=await route.fetch({url:origin+incoming.pathname+incoming.search,headers});await route.fulfill({response});
 });
 await page.goto(`http://arguebot.test/dashboard/conversations/${conversation.id}`);expect(await page.evaluate(()=>({secure:isSecureContext,clipboard:!!navigator.clipboard,uuid:!!crypto.randomUUID}))).toEqual({secure:false,clipboard:false,uuid:false});
 await page.getByRole('textbox',{name:'Your message'}).fill('Saved from insecure HTTP');await page.getByRole('button',{name:'Send',exact:true}).click();await expect(page.getByText('Saved from insecure HTTP',{exact:true})).toBeVisible();
 const turn=page.locator('article.message').filter({hasText:'Full turn 議論'});await turn.getByRole('button',{name:'Copy turn',exact:true}).click();await expect(turn.getByRole('button',{name:'Copied',exact:true})).toBeVisible();expect(await readClipboard(context,page)).toBe(messages[0].content);
 await page.getByRole('button',{name:'Copy conversation',exact:true}).click();await expect(page.locator('.workspace-heading').getByRole('button',{name:'Copied',exact:true})).toBeVisible();expect(await readClipboard(context,page)).toContain('Saved from insecure HTTP');
});
test('HTTPS inspector copies records and IDs, survives clipboard denial and offers manual copying',async({page,context})=>{
 await context.grantPermissions(['clipboard-read','clipboard-write'],{origin:'https://127.0.0.1:4420'});await page.goto('/');await expect(page.getByText('Your thinking, in progress',{exact:true})).toBeVisible();
 await page.goto('/dashboard/history');await page.getByRole('button',{name:'Inspect request',exact:true}).first().click();const inspector=page.getByRole('dialog');await expect(inspector.getByRole('heading',{name:'Request details',exact:true})).toBeVisible();
 await inspector.getByRole('button',{name:'Copy record',exact:true}).click();await expect(inspector.getByRole('button',{name:'Copied',exact:true})).toBeVisible();const record=JSON.parse(await page.evaluate(()=>navigator.clipboard.readText()));expect(record.id).toBeTruthy();
 await page.evaluate(()=>Object.defineProperty(navigator.clipboard,'writeText',{configurable:true,value:()=>Promise.reject(new Error('denied'))}));const id=inspector.locator('.copyable-id').filter({hasText:record.id}).first();await id.getByRole('button',{name:'Copy ID',exact:true}).click();await expect(id.getByRole('button',{name:'Copied',exact:true})).toBeVisible();expect(await page.evaluate(()=>navigator.clipboard.readText())).toBe(record.id);
 for(const width of [1280,390]){await page.setViewportSize({width,height:900});expect(await inspector.evaluate(node=>node.scrollWidth<=node.clientWidth+1)).toBe(true);await page.screenshot({path:`../.run/screenshots/request-inspector-${width}.png`});}
 await page.evaluate(()=>{document.execCommand=()=>false;});await inspector.getByRole('button',{name:'Copy record',exact:true}).click();const manual=page.getByRole('dialog',{name:'Copy text manually'});await expect(manual).toBeVisible();const text=manual.getByRole('textbox',{name:'Text to copy'});expect(JSON.parse(await text.inputValue()).id).toBe(record.id);expect(await text.evaluate(node=>node.selectionEnd-node.selectionStart)).toBe((await text.inputValue()).length);
});

test("HTTPS non-localhost uses the native clipboard", async ({ page, context, request }) => {
  const { conversation, messages } = await seed(request);
  await context.grantPermissions(["clipboard-read", "clipboard-write"], { origin: "https://arguebot.test" });
  await page.route("https://arguebot.test/**", async route => {
    const incoming = new URL(route.request().url());
    if (incoming.pathname.endsWith("/events")) return route.fulfill({ status: 200, contentType: "text/event-stream", body: ": heartbeat\n\n" });
    const headers = { ...route.request().headers(), origin }; delete headers.host;
    const response = await route.fetch({ url: origin + incoming.pathname + incoming.search, headers });
    await route.fulfill({ response });
  });
  await page.goto(`https://arguebot.test/dashboard/conversations/${conversation.id}`);
  expect(await page.evaluate(() => isSecureContext && !!navigator.clipboard)).toBe(true);
  await page.getByRole("button", { name: "Copy turn", exact: true }).click();
  await expect(page.getByRole("button", { name: "Copied", exact: true })).toBeVisible();
  expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(messages[0].content);
});

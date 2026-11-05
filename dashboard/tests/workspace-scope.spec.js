import { test, expect } from './fixtures.js';
test('workspace excludes backup screens and removed routes, and the contract requires bearer tokens',async({page})=>{
 await page.goto('/dashboard');await expect(page.getByRole('heading',{name:'Your thinking, in progress'})).toBeVisible();
 for(const suffix of ['auth/me','auth/login','auth/pair','auth/logout','users','roles','permissions','audit','backups','backup-operations']){
  const response=await page.request.fetch('/v1.0/api/'+suffix,{method:suffix.startsWith('auth/')&&suffix!=='auth/me'?'POST':'GET'});expect(response.status()).toBe(404);
 }
 const spec=await(await page.request.get('/openapi.json')).json();expect(Object.keys(spec.paths).some(path=>/\/(roles|permissions|backups|backup-operations)(\/|$)/.test(path))).toBe(false);expect(spec.security).toEqual([{bearerAuth:[]},{xToken:[]}]);expect(spec.paths['/v2.0/api/token'].post.security).toEqual([]);
 const settings=await(await page.request.get('/v1.0/api/settings')).json();expect(settings.settings).not.toHaveProperty('requireAuthentication');expect(settings.settings).not.toHaveProperty('telemetry');
 await expect(page.getByRole('link',{name:'Backups',exact:true})).toHaveCount(0);
 await page.goto('/dashboard/backups');await expect(page).toHaveURL(/\/dashboard\/home$/);
 await page.goto('/dashboard/home');
 await expect(page.getByRole('heading',{name:'Local observability',exact:true})).toHaveCount(0);
 for(const name of ['Grafana','Prometheus','Tempo','Loki'])await expect(page.getByRole('link',{name,exact:true})).toHaveCount(0);
});
test('conversation rows open the conversation, agent rows open edit forms, without hijacking copy',async({page})=>{
 const original='Row editing '+Date.now();
 const conversation=await(await page.request.post('/v1.0/api/conversations',{data:{title:original}})).json();
 await page.goto('/dashboard/conversations');await page.getByRole('searchbox').fill(original);
 const row=page.getByRole('row').filter({hasText:conversation.id});
 await row.getByRole('button',{name:/^Actions for /}).click();await page.getByRole('menuitem',{name:'Rename',exact:true}).click();
 await expect(page.getByRole('dialog')).toBeVisible();await page.getByRole('textbox',{name:'Title',exact:true}).fill(original+' edited');await page.getByRole('dialog').getByRole('button',{name:'Save',exact:true}).click();
 await expect(row).toContainText(original+' edited');await row.getByRole('button',{name:'Copy ID',exact:true}).click();await expect(page.getByRole('dialog')).toHaveCount(0);await expect(page).toHaveURL(/\/dashboard\/conversations$/);
 await row.getByText(original+' edited',{exact:true}).click();await expect(page.getByRole('heading',{name:original+' edited',exact:true})).toBeVisible();
 await page.goBack();await page.getByRole('searchbox').fill(original);await row.focus();await row.press('Enter');await expect(page.getByRole('heading',{name:original+' edited',exact:true})).toBeVisible();
 const profile=await(await page.request.post('/v1.0/api/participants',{data:{name:original,kind:'cli',provider:'claude'}})).json();
 await page.goto('/dashboard/participants');const agent=page.getByRole('row').filter({hasText:profile.id});await agent.getByText(original,{exact:true}).click();
 await expect(page.getByRole('dialog',{name:'Edit participant'})).toBeVisible();await expect(page.getByLabel('Display name',{exact:true})).toHaveValue(original);
 await page.getByLabel('Display name',{exact:true}).fill(original+' agent edited');await page.getByRole('dialog').getByRole('button',{name:'Save',exact:true}).click();await expect(agent).toContainText(original+' agent edited');
 await agent.focus();await agent.press('Space');await expect(page.getByRole('dialog',{name:'Edit participant'})).toBeVisible();await page.keyboard.press('Escape');
});
test('request rows inspect operational data',async({page})=>{
 await page.goto('/dashboard/history');await page.getByRole('row').filter({has:page.getByRole('button',{name:/^Actions for /})}).first().getByRole('cell').first().click();
 const dialog=page.getByRole('dialog',{name:'Inspect request'});await expect(dialog).toBeVisible();
 await expect(dialog.getByText('Request ID',{exact:true})).toBeVisible();
});

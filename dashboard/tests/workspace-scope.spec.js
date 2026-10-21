import { test, expect } from '@playwright/test';
test('workspace excludes identity and backup screens, endpoints and contract operations',async({page})=>{
 await page.goto('/dashboard');await expect(page.getByRole('heading',{name:'Your thinking, in progress'})).toBeVisible();
 for(const name of ['Users','Roles','Security audit','Sign out'])await expect(page.getByRole('link',{name,exact:true})).toHaveCount(0);
 for(const suffix of ['auth/me','auth/login','auth/pair','auth/logout','users','roles','permissions','audit','backups','backup-operations']){
  const response=await page.request.fetch('/v1.0/api/'+suffix,{method:suffix.startsWith('auth/')&&suffix!=='auth/me'?'POST':'GET'});expect(response.status()).toBe(404);
 }
 const spec=await(await page.request.get('/openapi.json')).json();expect(Object.keys(spec.paths).some(path=>/\/(auth|users|roles|permissions|audit|backups|backup-operations)(\/|$)/.test(path))).toBe(false);expect(spec.security).toEqual([]);
 const settings=await(await page.request.get('/v1.0/api/settings')).json();expect(settings.settings).not.toHaveProperty('requireAuthentication');expect(settings.settings).not.toHaveProperty('telemetry');
 await expect(page.getByRole('link',{name:'Backups',exact:true})).toHaveCount(0);
 await page.goto('/dashboard/backups');await expect(page).toHaveURL(/\/dashboard\/home$/);
 await page.goto('/dashboard/home');
 await expect(page.getByRole('heading',{name:'Local observability',exact:true})).toHaveCount(0);
 for(const name of ['Grafana','Prometheus','Tempo','Loki'])await expect(page.getByRole('link',{name,exact:true})).toHaveCount(0);
 await page.goto('/dashboard/users');await expect(page).toHaveURL(/\/dashboard\/home$/);
});
test('conversation and agent rows open edit forms without hijacking copy or explicit open actions',async({page})=>{
 const original='Row editing '+Date.now();
 const conversation=await(await page.request.post('/v1.0/api/conversations',{data:{title:original}})).json();
 await page.goto('/dashboard/conversations');await page.getByRole('searchbox').fill(original);
 const row=page.getByRole('row').filter({hasText:conversation.id});await row.getByText(original,{exact:true}).click();
 await expect(page.getByRole('dialog')).toBeVisible();await page.getByRole('textbox',{name:'Question or title'}).fill(original+' edited');await page.getByRole('dialog').getByRole('button',{name:'Save',exact:true}).click();
 await expect(row).toContainText(original+' edited');await row.getByRole('button',{name:'Copy ID',exact:true}).click();await expect(page.getByRole('dialog')).toHaveCount(0);
 await row.focus();await row.press('Enter');await expect(page.getByRole('dialog')).toBeVisible();await page.keyboard.press('Escape');
 await row.getByRole('button',{name:'Open conversation',exact:true}).click();await expect(page.getByRole('heading',{name:original+' edited',exact:true})).toBeVisible();
 const profile=await(await page.request.post('/v1.0/api/participants',{data:{name:original,kind:'cli',provider:'claude'}})).json();
 await page.goto('/dashboard/participants');const agent=page.getByRole('row').filter({hasText:profile.id});await agent.getByText(original,{exact:true}).click();
 await expect(page.getByRole('dialog',{name:'Edit participant'})).toBeVisible();await expect(page.getByLabel('Display name',{exact:true})).toHaveValue(original);
 await page.getByLabel('Display name',{exact:true}).fill(original+' agent edited');await page.getByRole('dialog').getByRole('button',{name:'Save',exact:true}).click();await expect(agent).toContainText(original+' agent edited');
 await agent.focus();await agent.press('Space');await expect(page.getByRole('dialog',{name:'Edit participant'})).toBeVisible();await page.keyboard.press('Escape');
});
test('request rows inspect operational data without principal or tenant fields',async({page})=>{
 await page.goto('/dashboard/history');await page.getByRole('row').filter({has:page.getByRole('button',{name:'Inspect request',exact:true})}).first().getByRole('cell').first().click();
 const dialog=page.getByRole('dialog',{name:'Inspect request'});await expect(dialog).toBeVisible();
 for(const name of ['Principal','Tenant','Validated actor'])await expect(dialog.getByText(name,{exact:true})).toHaveCount(0);
 await expect(dialog.getByText('Request ID',{exact:true})).toBeVisible();
});

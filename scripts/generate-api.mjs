import fs from 'node:fs';
import path from 'node:path';
const root = path.resolve(import.meta.dirname,'..');
const { operations } = JSON.parse(fs.readFileSync(path.join(root,'assets/api-contract.json'),'utf8'));
const schemas = {};
const primitive = { string: { type:'string' }, int: { type:'integer', format:'int32' }, long: { type:'integer',format:'int64' }, bool: { type:'boolean' }, DateTime: { type:'string',format:'date-time' }, object: { type:'object' } };
function schema(type) {
  if (type.endsWith('?')) return { ...schema(type.slice(0,-1)),nullable:true };
  const list = type.match(/^(?:List|IReadOnlyList)<(.+)>$/);
  if (list) return {type:'array',items:schema(list[1])};
  return primitive[type] ? { ...primitive[type] } : { $ref:'#/components/schemas/'+type };
}
const inputs = ['src/ArgueBot.Core/Models','src/ArgueBot.Core/Backups','src/ArgueBot.Core/Configuration','src/ArgueBot.Core/Database','src/ArgueBot.Server'];
for (const folder of inputs) for (const entry of fs.readdirSync(path.join(root,folder),{withFileTypes:true})) {
  if (!entry.isFile() || !entry.name.endsWith('.cs')) continue;
  const text = fs.readFileSync(path.join(root,folder,entry.name),'utf8');
  const name = text.match(/public (?:sealed )?class (\w+)/)?.[1]; if (!name) continue;
  const properties = {};
  for (const match of text.matchAll(/\/\/\/ <summary>([^\n]+)<\/summary>\s*public ([\w<>?]+) (\w+) \{\s*get/g)) {
    const [,description,type,property] = match; properties[property[0].toLowerCase()+property.slice(1)] = { ...schema(type),description };
  }
  if (Object.keys(properties).length) schemas[name] = { type:'object',properties };
}
schemas.DatabaseTypeEnum = {type:'string',enum:['Sqlite','Postgresql']};
const protocolSource = fs.readFileSync(path.join(root,'src/ArgueBot.Core/Configuration/ServiceProtocol.cs'),'utf8');
const protocolValue = name => Number(protocolSource.match(new RegExp(name+' = (\\d+)'))[1]);
const protocol = protocolValue('CurrentVersion');
const transport = {version:protocol,maxEventFrameCharacters:protocolValue('MaximumEventFrameCharacters'),eventIdleSeconds:protocolValue('EventIdleSeconds'),heartbeatSeconds:protocolValue('EventHeartbeatSeconds'),connectionSeconds:protocolValue('EventConnectionSeconds')};
fs.writeFileSync(path.join(root,'assets/transport.json'),JSON.stringify(transport,null,2)+'\n');
Object.assign(schemas.HealthResponse.properties.protocol,{enum:[protocol]});
for (const [field,minimum,maximum,value] of [['historyPreviewCodePoints',128,16384,4096],['contextMaxCharacters',1024,16777216,262144],['contextMaxMessages',1,10000,1000],['eventRetentionDays',1,3650,30],['eventMaxPerConversation',1,1000000,10000],['eventMaintenanceSeconds',5,3600,60],['eventRetentionBatchSize',1,1000,100],['eventReplayPageSize',1,1000,1000],['streamBatchCharacters',1,65536,4096],['streamCommitMilliseconds',10,5000,250]]) {
  Object.assign(schemas.ServiceSettings.properties[field],{minimum,maximum,default:value});
  Object.assign(schemas.SettingsUpdate.properties[field],{minimum,maximum});
}
for (const name of ['UserRequest','LoginRequest']) Object.assign(schemas[name].properties.passwordSha256,{writeOnly:true,minLength:64,maxLength:64,pattern:'^[A-Fa-f0-9]{64}$',description:'Sensitive SHA-256 password-equivalent input over HTTPS. The server derives a salted PBKDF2-HMAC-SHA256 storage verifier; neither value is returned.'});
Object.assign(schemas.UserRequest.properties.name,{minLength:1,maxLength:200});
Object.assign(schemas.UserRequest.properties.email,{format:'email',maxLength:254});
Object.assign(schemas.UserRequest.properties.role,{enum:['viewer','operator','admin'],default:'viewer'});
Object.assign(schemas.UserRequest.properties.expectedRevision,{minimum:0});
for (const name of ['PermissionRequest','PermissionRecord']) {
  Object.assign(schemas[name].properties.resourceTypes,{minItems:1,maxItems:32,items:{type:'string',pattern:'^[A-Za-z][A-Za-z0-9_]*$',maxLength:64}});
  Object.assign(schemas[name].properties.operations,{minItems:1,maxItems:16,items:{type:'string',enum:['Read','Create','Update','Delete','Write','Execute','Admin','All']}});
  Object.assign(schemas[name].properties.effect,{enum:['Permit','Deny']});
}
Object.assign(schemas.PermissionRequest.properties.name,{minLength:1,maxLength:200});
Object.assign(schemas.PermissionRequest.properties.expectedRevision,{minimum:0});
schemas.PermissionRequest.required=['name','resourceTypes','operations'];
schemas.PermissionRequest.description='Tenant custom permit/deny definition. All is an explicit wildcard; Write expands to Create/Update/Delete and grants neither Execute nor Admin. Updates require expectedRevision; protected global definitions are immutable. Disable to preserve historical references.';
Object.assign(schemas.RolePermissionsRequest.properties.permissionIds,{maxItems:128,uniqueItems:true,items:{type:'string',minLength:1,maxLength:64}});
Object.assign(schemas.RolePermissionsRequest.properties.expectedRevision,{minimum:0});
schemas.RolePermissionsRequest.required=['permissionIds','expectedRevision'];
schemas.RolePermissionsRequest.description='Complete desired active permission mapping set for a tenant custom role. Replacement requires current role revision, refuses foreign IDs and protected changes, deactivates omitted mappings and reuses historical mapping IDs. Permission activity remains independent of mapping activity.';
schemas.PermissionPage.required=['items','pageNumber','pageSize','totalCount'];
schemas.RolePermissionPage.required=['items','pageNumber','pageSize','totalCount','roleRevision'];
for (const name of ['UserRoleAssignmentRequest','UserRoleAssignmentRecord']) {
  Object.assign(schemas[name].properties.resourceScope,{enum:['Tenant','Resource']});
  Object.assign(schemas[name].properties.roleId,{maxLength:64});
  Object.assign(schemas[name].properties.roleName,{maxLength:200});
  Object.assign(schemas[name].properties.resourceId,{maxLength:64});
}
Object.assign(schemas.UserRoleAssignmentRequest.properties.expectedRevision,{minimum:0});
Object.assign(schemas.UserRoleAssignmentRequest.properties.resourceScope,{default:'Tenant'});
Object.assign(schemas.UserRoleAssignmentRequest.properties.inheritsToChildren,{default:false});
Object.assign(schemas.UserRoleAssignmentRequest.properties.active,{default:true});
schemas.UserRoleAssignmentRequest.anyOf=[{required:['roleId'],properties:{roleId:{minLength:1}}},{required:['roleName'],properties:{roleName:{minLength:1}}}];
schemas.UserRoleAssignmentRequest.allOf=[{oneOf:[{properties:{resourceScope:{enum:['Tenant']},resourceId:{maxLength:0}}},{required:['resourceScope','resourceId'],properties:{resourceScope:{enum:['Resource']},resourceId:{minLength:1},inheritsToChildren:{enum:[false]}}}]}];
schemas.UserRoleAssignmentRequest.description='Supply a role ID and/or fallback name. An existing inactive role blocks fallback. Tenant scope requires empty resourceId and explicit inheritance to reach children; Resource scope requires an exact ID with inheritance disabled. ResourceAdmin is resource-only. Updates require expectedRevision. Protected identities/assignments cannot be changed. Stored resolution changes immediately; compatibility route enforcement remains an integration gate.';
schemas.UserRoleAssignmentPage.required=['items','pageNumber','pageSize','totalCount'];
Object.assign(schemas.RoleRequest.properties.name,{minLength:1,maxLength:200});
Object.assign(schemas.RoleRequest.properties.expectedRevision,{minimum:0});
Object.assign(schemas.RoleRequest.properties.cloneRoleId,{maxLength:64});
schemas.RoleRequest.required=['name'];
schemas.RolePage.required=['items','pageNumber','pageSize','totalCount'];
schemas.RoleRecord.description='Global protected built-ins have null tenantId; all created roles are current-tenant custom definitions. Deactivate instead of deleting to preserve historical assignments.';
schemas.RoleRequest.description='Create optionally clones active permission mappings from an accessible role. Updates replace name/active and require expectedRevision; cloneRoleId is create-only. Built-in names are reserved and built-in definitions are immutable. Current database tenant/system administrator flags are required until full scoped administration integration.';
schemas.UserRecord.description='Public tenant identity metadata. No password verifier or hash is returned. Protected identities cannot be changed through normal management. Administrator flags are separate from compatibility domain role names.';
schemas.UserRequest.description='Complete metadata replacement. Omit passwordSha256 to preserve its password; a new value revokes existing sessions. Disablement also revokes sessions atomically. Updates require expectedRevision. Only system administrators may manage system administrators; all management remains bound to the current tenant.';
schemas.MessagePage.required=['items','pageNumber','pageSize','totalCount','throughSequence','latestSequence'];
schemas.Message.properties.contentBytes.readOnly=true;
schemas.Message.properties.contentTruncated.readOnly=true;
const required = { UserRequest:['name','email'],LoginRequest:['tenantId','email','passwordSha256'],SessionResponse:['token'],UserPage:['items','pageNumber','pageSize','totalCount'],HealthResponse:['status','schema','protocol','homeId'],PairRequest:['code'],PairResponse:['token'],ConversationRequest:['title'],CreateRunRequest:['participantIds','idempotencyKey'],CommandRequest:['action'],RunnerProfile:['name','kind','provider'],SettingsUpdate:['revision','port','maxConcurrentRuns','allowCli','databaseType'] };
for (const [name,keys] of Object.entries(required)) if (schemas[name]) schemas[name].required=keys;
Object.assign(schemas.CreateRunRequest.properties.maxRounds,{minimum:1,maximum:50,default:5});
Object.assign(schemas.CreateRunRequest.properties.interventionSeconds,{minimum:0,maximum:600,default:10});
Object.assign(schemas.CreateRunRequest.properties.mode,{enum:['debate','followup'],default:'debate'});
Object.assign(schemas.RunnerProfile.properties.kind,{enum:['cli','rest']});
Object.assign(schemas.RunnerProfile.properties.timeoutSeconds,{minimum:1,maximum:3600,default:300});
Object.assign(schemas.RunnerProfile.properties.maxOutputBytes,{minimum:1024,maximum:16777216,default:1048576});
schemas.CreateRunRequest.description='Debate requires two or three distinct participant IDs. Followup requires one participant and a nonempty question, and forces one round with no countdown.';
Object.assign(schemas.BackupRequest.properties.password,{minLength:0,maxLength:1024,writeOnly:true,description:'Optional encryption passphrase. A nonempty value requires at least twelve characters and at most 1024 UTF-8 bytes; never persisted in job metadata.'});
Object.assign(schemas.BackupRequest.properties.destinationName,{pattern:'^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$',description:'Required for restore. New child directory beneath the service home restores directory.'});
schemas.BackupRequest.description='Create accepts name/includeSecrets/password. Verify accepts password. Restore accepts password/destinationName/port/restoreDatabaseType/restoreConnectionEnvironmentVariable. Secret-inclusive creation requires encryption. Port zero retains the archived listener value; otherwise use 1024 through 65535. SQLite creates a new database file; PostgreSQL requires an empty pre-provisioned destination configured through a service-machine environment reference.';
schemas.RunnerProfile.description='CLI requires executable and uses ordered arguments plus stdin. REST requires endpoint and model. secretEnvironmentVariable is a reference, never the secret itself. Profiles are frozen into relational snapshots for each run.';
// Publish only transport-reachable schemas; implementation/helper classes are not API contracts.
const reachable = new Set();
function visit(node) {
  if (!node || typeof node !== 'object') return;
  if (node.$ref) {
    const name = node.$ref.split('/').at(-1);
    if (!schemas[name]) throw new Error(`Unresolved public transport type: ${name}`);
    if (!reachable.has(name)) {reachable.add(name);visit(schemas[name]);}
  }
  for (const value of Object.values(node)) if (value && typeof value === 'object') visit(value);
}
for (const operation of operations) {
  if (operation.request) visit(schema(operation.request));
  if (operation.response !== 'BinaryArchive') visit(schema(operation.response));
}
visit(schema('ApiError'));
for (const name of Object.keys(schemas)) if (!reachable.has(name)) delete schemas[name];
const spec={'x-arguebot-transport':transport,openapi:'3.0.3',info:{title:'ArgueBot API',version:'0.1.0',description:'Shared conversation service under implementation. Local pairing is loopback/origin restricted. All private operations require an active revocable bearer session. Credentials never appear in URLs.'},servers:[{url:'http://127.0.0.1:4317'}],security:[{Bearer:[]},{XToken:[]}],paths:{},components:{securitySchemes:{Bearer:{type:'http',scheme:'bearer'},XToken:{type:'apiKey',in:'header',name:'x-token'}},schemas}};
const collection={info:{name:'ArgueBot local conversation service',description:'Runnable local contract coverage. Set baseUrl, pairingCode or token. State-creating requests use fictional records. Model execution, settings changes and restart require allowExecution=true; use the committed local simulation before enabling it. Generated from assets/api-contract.json and the typed C# DTOs.',schema:'https://schema.getpostman.com/json/collection/v2.1.0/collection.json'},auth:{type:'bearer',bearer:[{key:'token',value:'{{token}}',type:'string'}]},variable:[{key:'baseUrl',value:'http://127.0.0.1:4317'},{key:'token',value:''},{key:'pairingCode',value:''},{key:'conversationId',value:''},{key:'conversationRevision',value:'1'},{key:'messageId',value:''},{key:'profileId',value:''},{key:'runId',value:''},{key:'requestId',value:''},{key:'backupId',value:''},{key:'operationId',value:''},{key:'roleId',value:''},{key:'permissionId',value:''},{key:'assignmentId',value:''},{key:'settingsRevision',value:''},{key:'allowExecution',value:'false'},{key:'simulationEndpoint',value:'http://127.0.0.1:4491/ollama'},{key:'fromUtc',value:'2026-01-01T00:00:00Z'},{key:'toUtc',value:'2026-01-02T00:00:00Z'}],item:[]};
const document=['# REST API','', 'The replacement service owns persistent conversations, execution and configuration. This reference documents the implemented route contract; it is generated with the OpenAPI document and Postman collection from `assets/api-contract.json` and named C# DTOs. Implementation and release limitations remain in `IMPROVEMENTS.md` and `docs/DEVELOPMENT.md`.','', 'Use `127.0.0.1` for local connections. Exchange the owner-readable single-use pairing code for an opaque one-hour session, then send `Authorization: Bearer <token>`. Keys live outside the database. Tokens are revocable and never belong in URL parameters. `x-token` may carry the identical session; conflicting carriers, repeated/empty authentication headers and tenant hints are rejected. `x-tenant-guid` and `x-tenant-id` must agree with the session. Unrelated password, access-key and signature material is rejected, including alongside a valid session. The local-only listener also supports tenant/email/passwordSha256 JSON exchange over HTTPS at /v1.0/api/auth/login, without an existing session or unrelated authentication headers. Password equivalents are passwords: never put them in URLs or logs. Twenty exchange attempts per minute are allowed per service. Hosted ingress, credential schemes and scoped RBAC remain open delivery gates.','', 'Protocol 2 changes GET message history from an unbounded array to MessagePage. Upgrade native clients and service together; product and database schema versions are independent. Message pages contain bounded previews and explicit contentTruncated/contentBytes metadata. Fetch a message by ID for complete text. Reuse throughSequence to keep page membership stable as later messages arrive; streaming text may still change. Tail selects the last page. Exports enumerate pages and hydrate previews instead of exporting truncated content. List routes use server-side filters with `pageNumber`, `pageSize`, `totalCount` and ordered items where the response is a page. Conversation search uses literal case-insensitive substring matching across titles and message content; pagination never changes its scope. Conversation mutations use expected revisions. Note/run creation and durable moderator controls use idempotency keys, bounded to 200 characters. An exact committed retry succeeds before stale-revision/state checks; a reused key with different input or run returns 409. Run checkpoints, status events and moderator receipts commit atomically. Schema 9 also records pending/streaming/terminal generation attempts, prompt hashes and exact ordered source identifiers. Answer finalization, successful directive consumption and speaker advance commit together. Inspect the run attempts route; over-budget context pauses before invocation without dropping transcript text. See `docs/EXECUTION.md`. Hold/release are transient per-client leases. Startup pauses uncertain work and completes a committed stop intent without automatically replaying generation. Accepted work survives session logout, but current user/tenant inactivity or execution-role removal cancels generation and pauses with authorization_revoked. Permission lookup failure cancels with authorization_unavailable; unavailable storage may require uncertain-work reconciliation. Denials are recorded independently of request history.','', 'Recovery archives and operations are protected global resources requiring an authenticated system administrator. Jobs return 202 before completion; poll `/v1.0/api/backup-operations/{operationId}`. Passphrases are not persisted in operation metadata. Controlled archive restore creates a new home beneath the service home and leaves the recovered service stopped. SQLite creates a new file; PostgreSQL requires an explicitly provisioned empty database through restoreConnectionEnvironmentVariable. Existing homes and nonempty destination databases are refused. If an external restore commits but reconciliation or publication fails, retainedStagingHome identifies protected operator recovery state. Binary download uses the existing bearer header. See `docs/RECOVERY.md`.','', 'Streaming commits are batched by a configured size threshold or timer. Schema-6 events carry `textOffset` in UTF-16 code units; reconcile older events from a message snapshot. Finalization flushes pending fragments before its terminal event. Standard failures use `ApiError`: stable `code`, safe `message`, and `requestId`. Typical statuses are 400 validation, 401 missing/expired/revoked session, 403 insufficient permission or conflicting tenant hint, 404 unowned/missing resource, 409 state/revision/key conflict, 413 request body limit, 429 enrollment limit, and 500 internal failure. Request bodies are bounded to one MiB. Do not automatically retry an uncertain generation.',''];
for (const operation of operations) {
  const {method,path:route,group,summary,response,request,status,public:anonymous,example,query,execute,save}=operation;
  const params=[...route.matchAll(/{(\w+)}/g)].map(match=>({name:match[1],in:'path',required:true,schema:{type:'string'},description:'Identifier in the authenticated ownership scope.'}));
  for (const key of query) params.push({name:key,in:'query',required:route.endsWith('/summary') && ['fromUtc','toUtc'].includes(key),schema:['pageNumber','pageSize'].includes(key)?{type:'integer',minimum:1,...(key==='pageSize'?{maximum:100,default:25}:{default:1})}:['status','bucketMinutes','after','throughSequence'].includes(key)?{type:'integer',minimum:['after','throughSequence'].includes(key)?0:1,...(['after','throughSequence'].includes(key)?{format:'int64'}:{})}:['archived','tail','active'].includes(key)?{type:'boolean',default:false}:{type:'string'},description:'Backend filter; date/time values use UTC ISO 8601.'});
  if (group === 'Roles' && query.includes('active')) params.find(parameter=>parameter.name==='active').schema.default=true;
  if (route === '/health') {
    const challenge = params.find(parameter => parameter.name === 'challenge');
    challenge.schema = {type:'string',pattern:'^[A-Fa-f0-9]{64}$',minLength:64,maxLength:64};
    challenge.description = 'Optional fresh random 32-byte hex nonce. The listener returns homeProof bound to this challenge, actual port and private local-home key. This is local process attestation, not user authentication.';
  }
  if (response === 'MessagePage') {
    params.find(parameter=>parameter.name==='throughSequence').description='Optional captured membership boundary. Omission captures the latest sequence. Streaming content itself is not frozen. Future boundaries are rejected.';
    params.find(parameter=>parameter.name==='tail').description='Select the last page of the captured membership, overriding pageNumber. Preview text is bounded; use individual message reads for full content.';
  }
  const responses={}; for (const code of [400,401,403,404,409,413,429,500]) responses[code]={description:({400:'Invalid input',401:'Session invalid',403:'Permission denied',404:'Resource not found in scope',409:'State, revision or command conflict',413:'Body too large',429:'Rate limited',500:'Internal failure'})[code],content:{'application/json':{schema:{$ref:'#/components/schemas/ApiError'}}}};
  responses[status]={description:'Successful '+summary.toLowerCase(),content:{'application/json':{schema:schema(response)}}};
  if (response === 'BinaryArchive') responses[status].content={'application/octet-stream':{schema:{type:'string',format:'binary'}}};
  if (route.endsWith('/events')) { const size=params.find(parameter=>parameter.name==='pageSize'); if(size)size.schema={type:'integer',minimum:1,maximum:1000,default:1000}; params.push({name:'Accept',in:'header',schema:{type:'string',enum:['application/json','text/event-stream']}},{name:'Last-Event-ID',in:'header',schema:{type:'string'},description:'SSE cursor formatted conversationId:sequence; foreign conversation cursors fail.'}); responses[status].content['text/event-stream']={schema:{type:'string'},example:'id: cnv_example:1\nevent: change\ndata: {"sequence":1,"kind":"message"}\n\n'}; }
  const op={operationId:method.toLowerCase()+route.replace(/[^A-Za-z0-9]+/g,'_'),tags:[group],summary,description:(anonymous?'Public local endpoint. ':'Requires an active bearer session. ')+(execute?'May execute work or change service state; do not blindly replay requests. ': '')+((method==='GET')?'Read permission required.':'Mutation permission required; service settings/restart require administrator.') ,parameters:params,responses};
  if(group==='Roles'||group==='Permissions'||group==='Assignments')op.description='Requires current database tenant or system administrator flags in the authenticated tenant. Global built-ins are visible and immutable; custom definitions are tenant-bound. Foreign custom IDs return 404. Updates require the current revision. Scoped administration roles remain an integration gate.';
  if(anonymous)op.security=[];
  if(request)op.requestBody={required:true,content:{'application/json':{schema:schema(request),example}}};
  (spec.paths[route]??={})[method.toLowerCase()]=op;
  let folder=collection.item.find(item=>item.name===group); if(!folder){folder={name:group,description:'Lifecycle operations for '+group.toLowerCase()+'. Variables, ownership and validation requirements are described by each request.',item:[]};collection.item.push(folder);}
  let url='{{baseUrl}}'+route.replace(/{(\w+)}/g,'{{$1}}');
  if(route.endsWith('/summary'))url+='?fromUtc={{fromUtc}}&toUtc={{toUtc}}&bucketMinutes=60';
  const event=[]; let before=[];
  if(execute)before.push("if (pm.variables.get('allowExecution') !== 'true') pm.execution.skipRequest();");
  for (const parameter of params.filter(item=>item.in==='path'))before.push(`if (!pm.variables.get('${parameter.name}')) pm.execution.skipRequest();`);
  if(route.endsWith('/auth/pair'))before.push("if (!pm.variables.get('pairingCode')) pm.execution.skipRequest();");
  if(before.length)event.push({listen:'prerequest',script:{type:'text/javascript',exec:before}});
  const after=[`pm.test('${status} expected', () => pm.response.to.have.status(${status}));`];
  if(save==='token')after.push("if(pm.response.code===200) pm.collectionVariables.set('token',pm.response.json().token);");
  else if(save)after.push(`if(pm.response.code===${status}) pm.collectionVariables.set('${save}',pm.response.json().id);`);
  if(response==='BackupOperation') after.push("if(pm.response.code<300) { const job=pm.response.json(); pm.collectionVariables.set('backupId',job.backupId); pm.test('Recovery state is explicit',()=>pm.expect(['queued','running','completed','failed']).to.include(job.state)); pm.test('Passphrase is absent from job metadata',()=>pm.expect(job).not.to.have.property('password')); }");
  if(response==='RolePermissionPage')after.push("if(pm.response.code===200) pm.test('Mapping page is bounded and tied to role revision',()=>{const page=pm.response.json();pm.expect(page.items.length).to.be.at.most(page.pageSize);pm.expect(page.roleRevision).to.be.at.least(1);for(const mapping of page.items)pm.expect(mapping.permission.id).to.equal(mapping.permissionId);});");
  if(response==='PermissionRecord')after.push("if(pm.response.code<300) pm.test('Custom permission is tenant-bound and validated',()=>{const value=pm.response.json();pm.expect(value.tenantId).to.be.a('string');pm.expect(value.protected).to.be.false;pm.expect(['Permit','Deny']).to.include(value.effect);pm.expect(value.resourceTypes.length).to.be.within(1,32);});");
  if(response==='UserRoleAssignmentRecord')after.push("if(pm.response.code<300) pm.test('Assignment is tenant/user-bound with explicit scope',()=>{const value=pm.response.json();pm.expect(value.userId).to.equal(pm.environment.get('userId')||pm.collectionVariables.get('userId'));pm.expect(['Tenant','Resource']).to.include(value.resourceScope);pm.expect(value.protected).to.be.false;pm.expect(value.revision).to.be.at.least(1);});");
  if(response==='RolePage')after.push("if(pm.response.code===200) pm.test('Role page preserves bounds and protected built-ins',()=>{const page=pm.response.json();pm.expect(page.items.length).to.be.at.most(page.pageSize);for(const role of page.items){if(role.builtin){pm.expect(role.protected).to.be.true;pm.expect(role.tenantId).to.equal(null);}else pm.expect(role.tenantId).to.be.a('string');}});");
  if(response==='RoleRecord')after.push("if(pm.response.code<300) pm.test('Custom role is tenant-bound and versioned',()=>{const role=pm.response.json();pm.expect(role.tenantId).to.be.a('string');pm.expect(role.builtin).to.be.false;pm.expect(role.protected).to.be.false;pm.expect(role.revision).to.be.at.least(1);});");
  if(response==='MessagePage')after.push("if(pm.response.code===200) pm.test('History page is bounded with membership metadata',()=>{const page=pm.response.json();pm.expect(page.items.length).to.be.at.most(page.pageSize);pm.expect(page.throughSequence).to.be.at.most(page.latestSequence);});");
  if(response==='Conversation')after.push(`if(pm.response.code<300) pm.collectionVariables.set('conversationRevision',String(pm.response.json().revision));`);
  if(response==='SettingsView')after.push("if(pm.response.code===200) pm.collectionVariables.set('settingsRevision',pm.response.json().revision);");
  event.push({listen:'test',script:{type:'text/javascript',exec:after}});
  const req={method,header:[{key:'Content-Type',value:'application/json'}],url,description:op.description+' '+summary};
  if(anonymous)req.auth={type:'noauth'};
  if(example!==null)req.body={mode:'raw',raw:JSON.stringify(example,null,2),options:{raw:{language:'json'}}};
  folder.item.push({name:summary,request:req,event});
  document.push(`## ${method} ${route}`,'',summary+'. '+op.description,'',`Success: **${status}**, response: \`${response}\`.${request?' Request: `'+request+'`.':''}`,'');
  if(params.length)document.push('| Parameter | Location | Required | Meaning |','| --- | --- | --- | --- |',...params.map(value=>`| ${value.name} | ${value.in} | ${!!value.required} | ${value.description || 'Protocol control.'} |`),'');
  if(example!==null)document.push('Representative request:','','```json',JSON.stringify(example,null,2),'```','');
}
document.push('## DTO schemas','', 'Exact field types, defaults and validation bounds are in the checked-in OpenAPI schemas. The tables below describe known contracts; server state and authorization remain authoritative. Diagnostic request/response bodies are explicitly unmanaged retained text, always subject to redaction/truncation and retention.','');
for(const[name,definition]of Object.entries(schemas)){if(!definition.properties)continue;document.push('### '+name,'',definition.description || 'Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.','', '| Field | Type | Meaning |','| --- | --- | --- |',...Object.entries(definition.properties).map(([key,value])=>`| ${key} | ${value.$ref?.split('/').at(-1) || value.type || 'object'}${value.nullable?' (nullable)':''} | ${value.description || ''} |`),'');}
document.push('## Security accounting','', 'The append-only audit route permits tenant administrators to inspect their own tenant and system administrators to inspect all events, including anonymous failures. Session issuance/revocation and HTTP 401/403 denials are recorded independently of optional request capture. Audit inspection supports an exact operation filter and bounded paging. No HTTP edit/delete route exists. Direct privileged database access is outside application-level immutability; comprehensive credential/RBAC/administrative accounting remains under implementation.','', '## Streaming and recovery','', 'Send `Accept: text/event-stream` and the same bearer header to the event route. Each committed change has an ID `conversationId:sequence`. Save the last processed ID and pass it in `Last-Event-ID` on reconnect. Heartbeats carry no application changes. Connections recycle after thirty seconds; `session_revoked` terminates access. Clients may use paged JSON events and snapshots when streaming is unavailable. Changes are read from committed storage, so replay and live delivery share one order. Clients must deduplicate IDs and refresh entity snapshots rather than append a repeated full message. Replay retention prunes only contiguous prefixes, preserving the latest sequence anchor. A nonzero expired cursor returns event_history_gap (HTTP 409 before streaming, or an error frame after headers). Capture lastEventSequence from the conversation, reload snapshots, then reconnect after that head. Transcripts and audit records are retained independently. Frames are bounded by the generated transport metadata in assets/transport.json.','', 'Closing a client does not stop accepted work. Service shutdown and crashes leave uncertain generations interrupted and their runs paused. Resume/retry is explicit. Historical participant snapshots survive profile edits/deletion. CLI and REST runs have separate capabilities; REST tool execution is disabled.','');
for(const[target,value]of [['assets/postman/openapi.json',JSON.stringify(spec,null,2)+'\n'],['assets/postman/arguebot.postman_collection.json',JSON.stringify(collection,null,2)+'\n'],['REST_API.md',document.join('\n').trimEnd()+'\n']])fs.writeFileSync(path.join(root,target),value);
console.log(`Generated ${operations.length} operations, ${Object.keys(schemas).length} DTO schemas, API reference and Postman collection.`);

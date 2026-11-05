import fs from 'node:fs';
import path from 'node:path';
const root = path.resolve(import.meta.dirname,'..');
const { operations } = JSON.parse(fs.readFileSync(path.join(root,'assets/api-contract.json'),'utf8'));
const schemas = {};
const primitive = { double: {type:'number',format:'double'}, string: { type:'string' }, int: { type:'integer', format:'int32' }, long: { type:'integer',format:'int64' }, bool: { type:'boolean' }, DateTime: { type:'string',format:'date-time' }, object: { type:'object' } };
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
Object.assign(schemas.ModelProfile.properties.maxOutputBytes, {minimum:1024,maximum:67108864,default:67108864});
Object.assign(schemas.ModelGenerationSettings.properties.maxOutputTokens, {minimum:1,maximum:1048576});
Object.assign(schemas.ModelGenerationSettings.properties.temperature, {minimum:0,maximum:2});
Object.assign(schemas.ModelGenerationSettings.properties.topP, {minimum:0,maximum:1});
Object.assign(schemas.EndpointProfile.properties.maxConcurrentInvocations, {minimum:1,maximum:64,default:1});
schemas.MessagePage.required=['items','pageNumber','pageSize','totalCount','throughSequence','latestSequence'];
schemas.Message.properties.contentBytes.readOnly=true;
schemas.Message.properties.contentTruncated.readOnly=true;
const queryHelp = {
  after:'Exclusive sequence cursor: return items after this sequence number. Use the last sequence you received.',
  archived:'true lists archived conversations instead of active ones.',
  bucketMinutes:'Chart interval in minutes; the range is divided into equal buckets.',
  challenge:'One-time value from the pairing prompt.',
  fromUtc:'Start of the range, UTC ISO 8601, for example 2026-01-01T00:00:00Z.',
  toUtc:'End of the range, UTC ISO 8601, for example 2026-01-02T00:00:00Z.',
  id:'Restrict the result to this identifier.',
  method:'HTTP method to match, for example GET or POST.',
  pageNumber:'Page to return, starting at 1.',
  pageSize:'Items per page, 1 through 100 unless noted.',
  revision:'Expected current revision; the request is refused if the resource changed.',
  search:'Case-insensitive text to find in paths or titles.',
  sourcePageNumber:'Page of original summary sources to return, 100 per page, starting at 1.',
  status:'HTTP status code to match, for example 409.',
  tail:'true returns the newest page instead of the first.',
  throughSequence:'Upper sequence bound that keeps paging stable while new messages arrive; use the value from the first page.',
  operation:'Exact audit event type, for example AuthFailure, AuthorizationDenied, SessionIssued or UserCreated.',
  outcome:'success, failure or denied.',
  userId:'User ID (usr_...).',
  tenantId:'Tenant ID (ten_...). System administrators only; tenant administrators always see their own tenant.',
  beforeUtc:'Delete records created before this UTC ISO 8601 time.',
  confirmName:'The tenant\'s exact current name, to confirm deletion.'
};
const pathHelp = name => ({conversationId:'Conversation ID (cnv_...). Copy it from the workspace heading or a conversation list.',runId:'Run ID (run_...). Copy it from the run bar in the workspace.',messageId:'Message ID (msg_...). Copy it from a message header.',profileId:'Participant profile ID. Copy it from the Participants page.',requestId:'Request ID from request history.',summaryId:'Summary ID (sum_...) from the memory panel.',attemptId:'Generation attempt ID from attempt history.',userId:'User ID (usr_...). Copy it from Administration > Users.',tenantId:'Tenant ID (ten_...). Copy it from Administration > Tenants.',credentialId:'Bearer-token credential ID (crd_...). Copy it from the credential list.',sessionId:'Session ID (ses_...). Copy it from the session list.',shareId:'Public link ID (shr_...). Copy it from the Share dialog.',shareToken:'The token at the end of a /share/ link. It is shown only when the link is created.',id:'Resource ID. Copy it from the matching list.'})[name] || 'Identifier in this workspace.';
const required = { LoginRequest:['email','password'],UserRequest:['email'],TenantRequest:['name'],AuthCredentialRequest:['name'],PasswordChangeRequest:['newPassword'],HealthResponse:['status','schema','protocol','homeId'],ConversationRequest:['title'],CreateRunRequest:['participantIds','idempotencyKey'],CommandRequest:['action'],RunnerProfile:[],SettingsUpdate:['revision','port','maxConcurrentRuns','allowCli','databaseType'] };
for (const [name,keys] of Object.entries(required)) if (schemas[name]) schemas[name].required=keys;
Object.assign(schemas.CreateRunRequest.properties.maxRounds,{minimum:1,maximum:50,default:5});
Object.assign(schemas.CreateRunRequest.properties.interventionSeconds,{minimum:0,maximum:600,default:10});
Object.assign(schemas.CreateRunRequest.properties.mode,{enum:['debate','followup'],default:'debate'});
Object.assign(schemas.RunnerProfile.properties.kind,{enum:['cli','rest']});
Object.assign(schemas.RunnerProfile.properties.timeoutSeconds,{minimum:1,maximum:3600,default:300});
Object.assign(schemas.RunnerProfile.properties.maxOutputBytes,{minimum:1024,maximum:67108864,default:67108864});
schemas.CreateRunRequest.description='Protocol 2 on /v2.0 requires an explicit orchestratorId and 2 through the advertised maximum participant IDs; consensus requires every active seat to agree on one proposal. Protocol 1 retains two or three distinct participant IDs and accepts a question up to 4000 characters; blank uses the conversation title. Followup requires one participant and a nonempty question, and forces one round with no countdown.';
schemas.RunnerProfile.description='CLI protocols are plain, claude, codex, gemini and mux. Named CLI providers fill omitted name, executable and empty arguments on create/update from assets/harness-defaults.json. Claude Code includes --dangerously-skip-permissions; Codex includes --yolo. Generic CLI (plain) requires an executable. CLI uses ordered arguments plus stdin. Mux defaults to print --output-format text --no-stats --no-session-persistence; custom arguments replace defaults. REST requires endpoint and model and supports native local tools (read_file, write_file, list_directory, run_process) when allowCli is enabled. workingDirectory applies to both runner kinds; empty uses the service directory. Tool activity is persisted as kind=tool messages, separate from answer text. secretEnvironmentVariable is a reference, never the secret itself. Profiles are frozen into relational snapshots for each run.';
Object.assign(schemas.CreateRunRequest.properties.protocolVersion, { enum: [1,2], default: 1 });
Object.assign(schemas.ServiceSettings.properties.maxParticipants, {minimum:2, maximum:64, default:16});
Object.assign(schemas.SettingsUpdate.properties.maxParticipants, {minimum:2, maximum:64});
Object.assign(schemas.InterjectionRequest.properties.deliveryMode, {enum:['interrupt_now','next_turn']});
schemas.InterjectionRequest.required = ['text','deliveryMode','idempotencyKey'];
schemas.RosterChangeRequest.required = ['action','revision','idempotencyKey'];
Object.assign(schemas.RosterChangeRequest.properties.action, {enum:['add','remove','replace']});
for (const [key, minimum, maximum] of [['maxParticipantTurns',1,100000],['maxOrchestrationCalls',1,10000],['maxCompactionCalls',1,10000],['maxToolCalls',1,100000],['maxWallClockSeconds',1,604800],['maxContextCharacters',8192,16777216],['maxEstimatedInputTokens',0,2147483647]]) Object.assign(schemas.DebatePolicy.properties[key], {minimum,maximum});
for (const key of ['questionInstructions','synthesisInstructions','summaryInstructions']) Object.assign(schemas.DebatePolicy.properties[key], {maxLength:4000});
Object.assign(schemas.CredentialRequest.properties.secretValue, {writeOnly:true, maxLength:65536});
schemas.CredentialRequest.required = ['name','kind','secretValue'];
Object.assign(schemas.CredentialRequest.properties.kind, {enum:['http_bearer','http_header','http_query','token','claude_oauth','codex_auth_json']});
Object.assign(schemas.CredentialRequest.properties.parameterName, {maxLength:128, pattern:'^[A-Za-z0-9_-]{0,128}$'});
Object.assign(schemas.HttpAuthBinding.properties.kind, {enum:['bearer','api_key','header','query']});
Object.assign(schemas.PromptTemplate.properties.purpose, {enum:['question','response','synthesis','ratification','compaction']});
Object.assign(schemas.PromptTemplate.properties.body, {maxLength:32000});
schemas.PromptTemplate.required = ['name','purpose','body'];
for (const [name, field] of [['LoginRequest','password'],['UserRequest','password'],['PasswordChangeRequest','currentPassword'],['PasswordChangeRequest','newPassword']]) Object.assign(schemas[name].properties[field], {writeOnly:true});
for (const [name, field] of [['TokenResponse','token'],['AuthCredentialCreated','token']]) Object.assign(schemas[name].properties[field], {readOnly:true});
Object.assign(schemas.UserRequest.properties.password, {minLength:8, maxLength:256});
Object.assign(schemas.PasswordChangeRequest.properties.newPassword, {minLength:8, maxLength:256});
Object.assign(schemas.AuthSession.properties.principalType, {enum:['User','Credential']});
Object.assign(schemas.ConversationShareRequest.properties.expiresInHours, {minimum:1,maximum:2160,default:168});
for (const [name, field] of [['ConversationShareCreated','token'],['ConversationShareCreated','url'],['ConversationShareCreated','path']]) Object.assign(schemas[name].properties[field], {readOnly:true});
Object.assign(schemas.TokenDetails.properties.principalType, {enum:['User','Credential']});
Object.assign(schemas.AuthorizationError.properties.requiredPermission, {enum:['Member','TenantAdmin','SystemAdmin']});
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
  visit(schema(operation.response));
}
visit(schema('ApiError')); visit(schema('AuthorizationError')); visit(schema('TenantRequiredError'));
for (const name of Object.keys(schemas)) if (!reachable.has(name)) delete schemas[name];

Object.assign(schemas.HealthResponse.properties.authenticationRequired,{default:true,enum:[true]});
const accessHelp={Public:'Access: public; no bearer token.',Member:'Access: any signed-in user of an active tenant.',TenantAdmin:'Access: tenant administrators (and system administrators) of the caller\'s tenant. A system administrator may send x-tenant-id to act in another tenant; the bypass is audited.',SystemAdmin:'Access: system administrators.'};
const securitySchemes={bearerAuth:{type:'http',scheme:'bearer',description:'A session token from POST /v2.0/api/token (abs_..., 60 minutes, refresh with POST /v2.0/api/token/refresh) or a credential bearer token (abt_..., or the default arguebotadmin). Sent as Authorization: Bearer.'},xToken:{type:'apiKey',in:'header',name:'x-token',description:'The same token in the x-token header. If both carriers are sent they must match.'}};
const spec={'x-arguebot-transport':transport,openapi:'3.0.3',info:{title:'ArgueBot API',version:'2.1.0',description:'Conversations, agents and execution for signed-in users. Data is shared within a tenant. Sign in with POST /v2.0/api/token or use a credential bearer token. The default administrator is admin@arguebot with password password and bearer token arguebotadmin; change them after the first sign-in.'},servers:[{url:'http://127.0.0.1:4317'}],security:[{bearerAuth:[]},{xToken:[]}],paths:{},components:{schemas,securitySchemes}};
const collection={info:{name:'ArgueBot local conversation service',description:'Runnable local contract coverage. Requests send {{token}} as a bearer token; it defaults to the default administrator token arguebotadmin, and Authentication > Sign in replaces it with a session token. Provider secrets are write-only configuration. State-creating requests use fictional records. Model execution, settings changes and restart require allowExecution=true; use the committed local simulation before enabling it. Generated from assets/api-contract.json and the typed C# DTOs.',schema:'https://schema.getpostman.com/json/collection/v2.1.0/collection.json'},auth:{type:'bearer',bearer:[{key:'token',value:'{{token}}',type:'string'}]},variable:[{key:'baseUrl',value:'http://127.0.0.1:4317'},{key:'token',value:'arguebotadmin'},...['shareId','shareToken','userId','userRevision','tenantId','tenantName','authCredentialId','sessionId','currentPassword','newPassword'].map(key=>({key,value:''})),{key:'conversationId',value:''},{key:'conversationRevision',value:'1'},{key:'messageId',value:''},{key:'profileId',value:''},{key:'runId',value:''},{key:'requestId',value:''},{key:'settingsRevision',value:''},{key:'allowExecution',value:'false'}, ...['resourceRevision','credentialValue','credentialId','endpointId','harnessId','modelProfileId','secondProfileId','thirdProfileId','fourthProfileId','orchestratorId','replacementProfileId','id','commandKey'].map(key=>({key,value:''})),{key:'simulationEndpoint',value:'http://127.0.0.1:4491/ollama'},{key:'bucketMinutes',value:'15'},{key:'fromUtc',value:'2026-01-01T00:00:00Z'},{key:'toUtc',value:'2026-01-02T00:00:00Z'}],item:[]};
const document=['# REST API','', 'The ArgueBot service owns persistent conversations, execution and configuration. This reference documents the implemented route contract; it is generated with the OpenAPI document and Postman collection from `assets/api-contract.json` and named C# DTOs. Implementation and release limitations remain in [ROADMAP.md](ROADMAP.md) and [development documentation](docs/DEVELOPMENT.md).','',
'## Authentication','',
'Every route except `/health`, `GET /v2.0/api/login-info`, `POST /v2.0/api/token`, the OpenAPI document and the dashboard files needs a bearer token. Send it as `Authorization: Bearer <token>` or as `x-token: <token>`; if you send both, they must be the same token.','',
'- **Session token**: `POST /v2.0/api/token` with `{ "email", "password", "tenantId" }`, or with the `x-email`, `x-password` and optional `x-tenant-id` headers. The response holds an opaque `abs_...` token that lasts 60 minutes. `POST /v2.0/api/token/refresh` replaces it with a new token and a fresh lifetime; the previous token stops working. `DELETE /v2.0/api/token` signs out.',
'- **Credential token**: a long-lived `abt_...` token created under `/v2.0/api/me/credentials`. It is shown once. Disable or delete the credential to revoke it.',
'- **Default administrator**: `admin@arguebot` with password `password` and bearer token `arguebotadmin`. The sign-in page always shows these. Change the password and create your own credentials, then disable nothing you still need: the default user, tenant and token are protected and cannot be deleted.','',
'If the email and password match accounts in several tenants, sign-in returns 409 `tenant_required` with the tenants to choose from; send `tenantId` and sign in again. `x-tenant-id` on other routes must match the token\'s tenant (403 `tenant_mismatch`), except that a system administrator may use it on user, session and audit routes to act in another tenant.','',
'Failures: 401 `authentication_required`, `invalid_token`, `session_expired`, `session_revoked`, `credential_inactive`, `credential_expired`, `user_inactive`, `tenant_inactive`, `invalid_credentials`, `account_inactive`, `conflicting_credentials`, `unsupported_scheme` or `password_not_accepted`; 403 `forbidden` or `tenant_mismatch` with an `AuthorizationError` body; 429 `rate_limited` with `Retry-After` after 10 failed sign-ins from one address or for one email in 5 minutes. Every failure and every administrative change is recorded in the audit log (`GET /v2.0/api/audit`).','',
'Access classes: **Member** routes are open to any signed-in user of an active tenant, and data is shared within the tenant. **TenantAdmin** routes need a tenant administrator. **SystemAdmin** routes need a system administrator. Each route below states its class.','',
'Host names: `127.0.0.1` and `localhost` are always accepted. Add other names, such as a reverse proxy name, to `allowedHosts` in the service settings or to `ARGUEBOT_ALLOWED_HOSTS`. Use TLS, directly or through a TLS-terminating proxy, whenever the service is reachable from another machine.',''];
for (const operation of operations) {
  const {method,path:route,group,summary,response,request,status,public:anonymous,example,query,execute,save,access,deprecated}=operation;
  if (!accessHelp[access]) throw new Error(`Operation ${method} ${route} has no access class.`);
  if ((access === 'Public') !== !!anonymous) throw new Error(`Operation ${method} ${route} has inconsistent public and access fields.`);
  const params=[...route.matchAll(/{(\w+)}/g)].map(match=>({name:match[1],in:'path',required:true,schema:{type:'string'},description:pathHelp(match[1])}));
  for (const key of query) params.push({name:key,in:'query',required:route.endsWith('/summary') && ['fromUtc','toUtc'].includes(key),schema:['pageNumber','pageSize','sourcePageNumber'].includes(key)?{type:'integer',minimum:1,...(key==='pageSize'?{maximum:100,default:25}:{default:1})}:['status','bucketMinutes','after','throughSequence'].includes(key)?{type:'integer',minimum:['after','throughSequence'].includes(key)?0:1,...(['after','throughSequence'].includes(key)?{format:'int64'}:{})}:['archived','tail','active'].includes(key)?{type:'boolean',default:false}:{type:'string'},description:queryHelp[key]||'Backend filter.'});
  if (route.startsWith('/v2.0/api/configuration/')) for (const parameter of params) if (parameter.name === 'pageSize') parameter.schema.default = 100;
  if (route.endsWith('/request-history/summary')) {
    const bucket = params.find(parameter => parameter.name === 'bucketMinutes');
    bucket.schema = {type:'integer',minimum:1,maximum:1440,default:60};
    bucket.description = 'Fixed UTC interval in minutes. Dashboard presets: last hour 1 (60 buckets), last day 15 (96), last week 120 (84), rolling 30 days 480 (90). The range is [fromUtc, toUtc), limited to 31 days and 2000 buckets, including zero-count intervals. HTTP statuses below 400 count as success; 400 and above count as errors. Disabled request history returns an empty array.';
  }
  if (route === '/health') {
    const challenge = params.find(parameter => parameter.name === 'challenge');
    challenge.schema = {type:'string',pattern:'^[A-Fa-f0-9]{64}$',minLength:64,maxLength:64};
    challenge.description = 'Optional fresh random 32-byte hex nonce. The listener returns homeProof bound to this challenge, actual port and private local-home key. This is local process attestation, not user authentication.';
  }
  if (response === 'MessagePage') {
    params.find(parameter=>parameter.name==='throughSequence').description='Optional captured membership boundary. Omission captures the latest sequence. Streaming content itself is not frozen. Future boundaries are rejected.';
    params.find(parameter=>parameter.name==='tail').description='Select the last page of the captured membership, overriding pageNumber. Preview text is bounded; use individual message reads for full content.';
  }
  const responses={}; for (const code of [400,...(anonymous?[]:[401]),403,404,409,413,429,500]) responses[code]={description:({400:'Invalid input',401:'Missing, invalid, expired or revoked bearer token',403:access==='Member'||anonymous?'Host, origin, tenant or execution policy denied':'Host, origin or tenant denied, or the caller lacks the '+access+' access class',404:'Resource not found in scope',409:'State, revision or command conflict',413:'Body too large',429:'Rate limited',500:'Internal failure'})[code],content:{'application/json':{schema:{$ref:'#/components/schemas/'+(code===403&&!anonymous?'AuthorizationError':code===409&&route==='/v2.0/api/token'?'TenantRequiredError':'ApiError')}}}};
  responses[status]={description:'Successful '+summary.toLowerCase(),content:{'application/json':{schema:schema(response)}}};
  if (route.endsWith('/events')) { const size=params.find(parameter=>parameter.name==='pageSize'); if(size)size.schema={type:'integer',minimum:1,maximum:1000,default:1000}; params.push({name:'Accept',in:'header',schema:{type:'string',enum:['application/json','text/event-stream']}},{name:'Last-Event-ID',in:'header',schema:{type:'string'},description:'SSE cursor formatted conversationId:sequence; foreign conversation cursors fail.'}); responses[status].content['text/event-stream']={schema:{type:'string'},example:'id: cnv_example:1\nevent: change\ndata: {"sequence":1,"kind":"message"}\n\n'}; }
  const op={operationId:method.toLowerCase()+route.replace(/[^A-Za-z0-9]+/g,'_'),tags:[group],summary,description:accessHelp[access]+' '+(deprecated?'Deprecated: use the /configuration/secrets or /configuration/secret-keys route instead; this alias is removed after 2.1.x. ':'')+(execute?'May execute work or change service state; do not blindly replay requests. ':''),parameters:params,responses,'x-arguebot-access':access};
  if(deprecated)op.deprecated=true;
  if(anonymous)op.security=[];
  if(request)op.requestBody={required:true,content:{'application/json':{schema:schema(request),example}}};
  (spec.paths[route]??={})[method.toLowerCase()]=op;
  let folder=collection.item.find(item=>item.name===group); if(!folder){folder={name:group,description:'Lifecycle operations for '+group.toLowerCase()+'. Variables, ownership and validation requirements are described by each request.',item:[]};collection.item.push(folder);}
  let url='{{baseUrl}}'+route.replace(/{(\w+)}/g,'{{$1}}');
  if(query.includes('revision'))url+='?revision={{resourceRevision}}';
  if(query.includes('confirmName'))url+='?confirmName={{tenantName}}';
  if(query.includes('beforeUtc'))url+='?beforeUtc={{fromUtc}}';
  if(route.endsWith('/summary'))url+='?fromUtc={{fromUtc}}&toUtc={{toUtc}}&bucketMinutes={{bucketMinutes}}';
  const event=[]; let before=[];
  for (const match of JSON.stringify(example).matchAll(/{{(\w+)}}/g)) before.push(`if (!pm.variables.get('${match[1]}')) pm.execution.skipRequest();`);
  if(execute)before.push("if (pm.variables.get('allowExecution') !== 'true') pm.execution.skipRequest();");
  for (const parameter of params.filter(item=>item.in==='path'))before.push(`if (!pm.variables.get('${parameter.name}')) pm.execution.skipRequest();`);
  if(before.length)event.push({listen:'prerequest',script:{type:'text/javascript',exec:before}});
  const after=[`pm.test('${status} expected', () => pm.response.to.have.status(${status}));`];
  if(save==='token')after.push(`if(pm.response.code===${status}) pm.collectionVariables.set('token',pm.response.json().token);`);
  else if(save)after.push(`if(pm.response.code===${status}) pm.collectionVariables.set('${save}',pm.response.json().id);`);
  if(response==='PermissionRecord')after.push("if(pm.response.code<300) pm.test('Custom permission is tenant-bound and validated',()=>{const value=pm.response.json();pm.expect(value.tenantId).to.be.a('string');pm.expect(value.protected).to.be.false;pm.expect(['Permit','Deny']).to.include(value.effect);pm.expect(value.resourceTypes.length).to.be.within(1,32);});");
  if(response==='MessagePage')after.push("if(pm.response.code===200) pm.test('History page is bounded with membership metadata',()=>{const page=pm.response.json();pm.expect(page.items.length).to.be.at.most(page.pageSize);pm.expect(page.throughSequence).to.be.at.most(page.latestSequence);});");
  if(response==='Conversation')after.push(`if(pm.response.code<300) pm.collectionVariables.set('conversationRevision',String(pm.response.json().revision));`);
  if(route.endsWith('/request-history/summary'))after.push("if(pm.response.code===200) pm.test('Activity has fixed intervals and nonnegative counts',()=>{const rows=pm.response.json();const minutes=Number(pm.variables.get('bucketMinutes'));const from=Date.parse(pm.variables.get('fromUtc')),to=Date.parse(pm.variables.get('toUtc'));pm.expect(rows).to.be.an('array');if(rows.length){pm.expect(rows.length).to.equal(Math.ceil((to-from)/(minutes*60000)));rows.forEach((row,index)=>{pm.expect(Date.parse(row.startUtc)).to.equal(from+index*minutes*60000);pm.expect(row.success).to.be.at.least(0);pm.expect(row.failure).to.be.at.least(0);});}});");
  if(response==='SettingsView')after.push("if(pm.response.code===200) pm.collectionVariables.set('settingsRevision',pm.response.json().revision);");
  if(response==='CredentialMetadata') after.push("if(pm.response.code<300) pm.test('Credential response contains metadata only',()=>{const value=pm.response.json();pm.expect(value).not.to.have.property('secretValue');pm.expect(value).not.to.have.property('ciphertext');});");
  if(response==='ServiceCapabilities') after.push("pm.test('Explicit unanimous panel protocol is available',()=>{const value=pm.response.json();pm.expect(value.debateProtocol).to.equal(2);pm.expect(value.maxParticipants).to.be.at.least(2);pm.expect(value.consensusPolicy).to.equal('unanimous');pm.expect(value.deliveryModes).to.include.members(['interrupt_now','next_turn']);});");
  if(response==='List<DebateProposal>') after.push("if(pm.response.code===200) pm.test('Every proposal has explicit positions',()=>{pm.response.json().forEach(value=>{pm.expect(value.id).to.be.a('string');value.agreements.forEach(vote=>pm.expect(['agree','disagree','abstain','needs_information']).to.include(vote.position));});});");
  if(response==='InterjectionReceipt') after.push("if(pm.response.code<300) pm.test('Context has a durable delivery receipt',()=>{const value=pm.response.json();pm.expect(value.id).to.be.a('string');pm.expect(['interrupt_now','next_turn']).to.include(value.deliveryMode);pm.expect(value.contextEpoch).to.be.at.least(0);});");
  event.push({listen:'test',script:{type:'text/javascript',exec:after}});
  const req={method,header:[{key:'Content-Type',value:'application/json'}],url,description:op.description+' '+summary};
  if(anonymous)req.auth={type:'noauth'};
  if(example!==null)req.body={mode:'raw',raw:JSON.stringify(example,null,2),options:{raw:{language:'json'}}};
  folder.item.push({name:summary,request:req,event});
  document.push(`## ${method} ${route}`,'',summary+'. '+op.description.trim(),'',`Success: **${status}**, response: \`${response}\`.${request?' Request: `'+request+'`.':''}`,'');
  if(params.length)document.push('| Parameter | Location | Required | Meaning |','| --- | --- | --- | --- |',...params.map(value=>`| ${value.name} | ${value.in} | ${!!value.required} | ${value.description || 'Protocol control.'} |`),'');
  if(example!==null)document.push('Representative request:','','```json',JSON.stringify(example,null,2),'```','');
}
document.push('## DTO schemas','', 'Exact field types, defaults and validation bounds are in the checked-in OpenAPI schemas. The tables below describe known contracts; server state and validation remain authoritative. Diagnostic request/response bodies are explicitly unmanaged retained text, always subject to redaction/truncation and retention.','');
for(const[name,definition]of Object.entries(schemas)){if(!definition.properties)continue;document.push('### '+name,'',definition.description || 'Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.','', '| Field | Type | Meaning |','| --- | --- | --- |',...Object.entries(definition.properties).map(([key,value])=>`| ${key} | ${value.$ref?.split('/').at(-1) || value.type || 'object'}${value.nullable?' (nullable)':''} | ${value.description || ''} |`),'');}
document.push('## Streaming and recovery','', 'Send `Accept: text/event-stream` to the event route. Each committed change has an ID `conversationId:sequence`. Save the last processed ID and pass it in `Last-Event-ID` on reconnect. Heartbeats carry no application changes. Connections recycle after thirty seconds; Clients may use paged JSON events and snapshots when streaming is unavailable. Changes are read from committed storage, so replay and live delivery share one order. Clients must deduplicate IDs and refresh entity snapshots rather than append a repeated full message. Replay retention prunes only contiguous prefixes, preserving the latest sequence anchor. A nonzero expired cursor returns event_history_gap (HTTP 409 before streaming, or an error frame after headers). Capture lastEventSequence from the conversation, reload snapshots, then reconnect after that head. Transcripts are retained independently. Frames are bounded by the generated transport metadata in assets/transport.json.','', 'Closing a client does not stop accepted work. Service shutdown and crashes leave uncertain generations interrupted and their runs paused. Resume/retry is explicit. Historical participant snapshots survive profile edits/deletion. REST models can call service-owned local file and process tools when allowCli is enabled. Calls and results are persisted as tool messages and replay through normal history/events. Stopping cancels local processes. Tool execution has a 32-call/16-round turn limit, 30-second process limit and 64-KiB file/pipe limits. Interrupted operations may have side effects; inspect before explicitly retrying.','');
for(const[target,value]of [['assets/postman/openapi.json',JSON.stringify(spec,null,2)+'\n'],['assets/postman/arguebot.postman_collection.json',JSON.stringify(collection,null,2)+'\n'],['REST_API.md',document.map(line=>line.trimEnd()).join('\n').trimEnd()+'\n']])fs.writeFileSync(path.join(root,target),value);
console.log(`Generated ${operations.length} operations, ${Object.keys(schemas).length} DTO schemas, API reference and Postman collection.`);

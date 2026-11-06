// Contract source: installed PolyPrompt 3.4 tool-chat serializers/parsers and provider wire contracts.
// Only provider HTTP is simulated. ArgueBot executes the local tools for real in disposable test directories.
const http = require('http');
const server = http.createServer((req, res) => {
  const pathname = new URL(req.url, 'http://127.0.0.1').pathname;
  if (req.method === 'GET' && (pathname.endsWith('/api/tags') || pathname.endsWith('/models'))) {
    res.writeHead(pathname.includes('/unavailable/') ? 401 : 200, { 'Content-Type': 'application/json' });
    return res.end(JSON.stringify(pathname.includes('/unavailable/') ? { error: req.headers.authorization } : pathname.endsWith('/api/tags') ? { models: [{ name: 'multi-agent' }, { name: 'multi-agent-auth' }, { name: 'multi-agent' }] } : { data: [{ id: 'multi-agent' }, { id: 'multi-agent-auth' }] }));
  }
  const provider = pathname.endsWith('/api/chat') ? 'ollama' : pathname.endsWith('/chat/completions') ? 'openai' : pathname.endsWith('/messages') ? 'anthropic' : pathname.includes(':streamGenerateContent') ? 'gemini' : null;
  if (req.method !== 'POST' || !provider) {
    res.writeHead(501, { 'Content-Type': 'application/json' });
    return res.end(JSON.stringify({ error: 'Outside this simulation contract' }));
  }
  let raw = '';
  req.on('data', chunk => { raw += chunk; });
  req.on('end', async () => {
    let body;
    try { body = req.body || JSON.parse(raw); } catch {
      res.writeHead(400); return res.end('{"error":"invalid JSON"}');
    }
    const model = body.model || decodeURIComponent(pathname.split('/models/')[1]?.split(':')[0] || '');
    if (model === 'multi-agent-slow') await new Promise(resolve => setTimeout(resolve, 300));
    const messages = body.messages || body.contents;
    if (!model || !Array.isArray(messages)) {
      res.writeHead(400); return res.end('{"error":"model and messages required"}');
    }
    if (model === 'deadline-stream' && provider === 'ollama') {
      res.writeHead(200, { 'Content-Type': 'application/x-ndjson' });
      res.write(JSON.stringify({ model, message: { role: 'assistant', content: 'Partial deadline fixture.' }, done: false }) + '\n');
      const timer = setTimeout(() => res.end(), 10000);
      res.on('close', () => clearTimeout(timer));
      return;
    }
    if (model === 'forbidden') {
      res.writeHead(403, { 'Content-Type': 'application/json' });
      return res.end('{"error":"fixture key may not use this model"}');
    }
    if (model === 'server-error') {
      res.writeHead(503, { 'Content-Type': 'application/json' });
      return res.end('{"error":"fixture provider overloaded"}');
    }
    if (model === 'disconnect' && provider === 'ollama') {
      // Midstream disconnect: send part of a successful stream, then drop the connection without a final chunk.
      res.writeHead(200, { 'Content-Type': 'application/x-ndjson' });
      res.write(JSON.stringify({ model, message: { role: 'assistant', content: 'Partial answer before the connection drops.' }, done: false }) + '\n');
      return setTimeout(() => res.socket.destroy(), 50);
    }
    if (model === 'rate-limit') {
      res.writeHead(429, { 'Content-Type': 'application/json', 'Retry-After': '1' });
      return res.end('{"error":"fixture rate limit"}');
    }
    const emit = (text, calls = [], incomplete = false) => {
      res.writeHead(200, { 'Content-Type': provider === 'ollama' ? 'application/x-ndjson' : 'text/event-stream' });
      const sse = (data, event) => res.write((event ? `event: ${event}\n` : '') + `data: ${JSON.stringify(data)}\n\n`);
      if (provider === 'ollama') {
        res.write(JSON.stringify({ model, message: { role: 'assistant', content: text, tool_calls: calls.map(call => ({ function: { name: call.name, arguments: call.args } })) }, done: false }) + '\n');
        if (!incomplete) res.write(JSON.stringify({ model, message: { role: 'assistant', content: '' }, done: true, done_reason: calls.length ? 'tool_calls' : 'stop' }) + '\n');
      } else if (provider === 'openai') {
        // Deliberately split JSON arguments across events to exercise streamed assembly.
        if (text) sse({ choices: [{ index: 0, delta: { content: text }, finish_reason: null }] });
        calls.forEach((call, index) => {
          const argumentsJson = JSON.stringify(call.args); const middle = Math.floor(argumentsJson.length / 2);
          sse({ choices: [{ index: 0, delta: { tool_calls: [{ index, id: call.id, type: 'function', function: { name: call.name, arguments: argumentsJson.slice(0, middle) } }] }, finish_reason: null }] });
          sse({ choices: [{ index: 0, delta: { tool_calls: [{ index, function: { arguments: argumentsJson.slice(middle) } }] }, finish_reason: null }] });
        });
        if (!incomplete) { sse({ choices: [{ index: 0, delta: {}, finish_reason: calls.length ? 'tool_calls' : 'stop' }] }); res.write('data: [DONE]\n\n'); }
      } else if (provider === 'anthropic') {
        sse({ type: 'message_start', message: { id: 'fixture', type: 'message', role: 'assistant', content: [], model, usage: { input_tokens: 1, output_tokens: 0 } } }, 'message_start');
        if (text) {
          sse({ type: 'content_block_start', index: 0, content_block: { type: 'text', text: '' } }, 'content_block_start');
          sse({ type: 'content_block_delta', index: 0, delta: { type: 'text_delta', text } }, 'content_block_delta');
          sse({ type: 'content_block_stop', index: 0 }, 'content_block_stop');
        }
        calls.forEach((call, index) => {
          sse({ type: 'content_block_start', index, content_block: { type: 'tool_use', id: call.id, name: call.name, input: {} } }, 'content_block_start');
          sse({ type: 'content_block_delta', index, delta: { type: 'input_json_delta', partial_json: JSON.stringify(call.args) } }, 'content_block_delta');
          sse({ type: 'content_block_stop', index }, 'content_block_stop');
        });
        if (!incomplete) { sse({ type: 'message_delta', delta: { stop_reason: calls.length ? 'tool_use' : 'end_turn' }, usage: { output_tokens: 1 } }, 'message_delta'); sse({ type: 'message_stop' }, 'message_stop'); }
      } else {
        const parts = calls.length ? calls.map(call => ({ functionCall: { name: call.name, args: call.args }, thoughtSignature: 'fixture-thought-signature' })) : [{ text }];
        sse({ candidates: [{ content: { role: 'model', parts }, ...(incomplete ? {} : { finishReason: 'STOP' }) }] });
      }
      res.end();
    };
    if (model === 'check-tools' && !messages.some(message => message.role === 'tool')) return emit('', [{ id: 'probe', name: 'connection_probe', args: { value: 4 } }]);
    if (model.startsWith('multi-agent')) {
      if (model === 'multi-agent-auth' && (req.headers.authorization !== 'Bearer fictional-gateway-token' || req.headers['x-api-key'] !== 'fictional-gateway-token' || req.headers['x-route'] !== 'fixture' || new URL(req.url, 'http://127.0.0.1').searchParams.get('gateway_key') !== 'fictional-gateway-token')) {
        res.writeHead(401, { 'Content-Type': 'application/json' }); return res.end('{"error":"fictional-gateway-token auth mismatch"}');
      }
      if (model === 'multi-agent-auth') {
        const options = provider === 'ollama' ? body.options : body;
        const tokens = provider === 'ollama' ? options?.num_predict : (body.max_tokens ?? body.max_completion_tokens);
        if (tokens !== 2048 || options?.temperature !== 0.25 || options?.top_p !== 0.9) {
          res.writeHead(400); return res.end('{"error":"generation settings mismatch"}');
        }
      }
      const instructions = messages.find(message => message.role === 'system')?.content || '';
      if (instructions.includes('Compact the supplied')) {
        const ids = instructions.split('Supplied source IDs: ')[1]?.split('\n')[0].split(', ') || [];
        const pinned = instructions.split('Pinned moderator constraint source IDs: ')[1]?.split('. ')[0].split(', ') || [];
        return emit(JSON.stringify({ summary: 'Working memory of supplied evidence.', sections: [
          ['facts', 'No rollout outcome is established.'], ['contested_claims', 'Evidence remains contested.'],
          ['positions', 'No final participant agreement is established.'], ['evidence', 'Original evidence is retained in the source manifest.'],
          ['decisions', 'No ratified decision yet.'], ['questions', 'Which rollout limit is justified?'],
          ['constraints', 'Retain reversible rollout and moderator constraints.' + pinned.map(id => ` Pinned constraint (${id}).`).join('')]
        ].map(([kind, content]) => ({ kind, content })), sourceIds: ids }));
      }
      if (instructions.includes('question (required')) return emit(JSON.stringify({ question: 'Which evidence makes a reversible rollout safe?', rationale: 'Identify the constraint that distinguishes the alternatives.' }));
      if (instructions.includes('proposal (required')) return emit(JSON.stringify({ proposal: 'Adopt a reversible rollout with explicit success criteria and a rollback threshold.' }));
      if (instructions.includes('proposalId:')) return emit(JSON.stringify({ proposalId: instructions.match(/proposalId: "([^"]+)"/)?.[1], position: 'agree', rationale: 'The proposal addresses the shared constraints.' }));
      if (model === 'multi-agent-resources') {
        // Read the billing resource, append a note when writing is offered, then answer from what was read.
        const offered = name => body.tools?.some(tool => tool.function?.name === name);
        const results = messages.filter(message => message.role === 'tool').map(message => String(message.content));
        if (offered('read_resource') && results.length === 0) return emit('', [{ id: 'resource_read', name: 'read_resource', args: { name: 'billing' } }]);
        if (offered('append_resource') && results.length === 1) return emit('', [{ id: 'resource_note', name: 'append_resource', args: { name: 'debate-notes', text: 'REST note.' } }]);
        return emit(results.some(result => result.includes('Invoices go out on the 1st')) ? 'Billing context read: invoices go out on the 1st.' : 'Billing context unavailable.');
      }
      if (body.tools?.some(tool => tool.function?.name === 'get_debate_state') && !messages.some(message => message.role === 'tool')) return emit('', [{ id: 'context_state', name: 'get_debate_state', args: {} }]);
      return emit('Use a measured rollout with a clear rollback threshold. WE HAVE CONSENSUS is only prose here.');
    }
    if (model === 'tools-disabled') return emit(!body.tools?.length ? 'Tools disabled.' : 'Unexpected tools.');
    if (!model.startsWith('local-tools') && !model.startsWith('tool-')) return emit('Fixture answer.', [], model === 'broken-stream');
    const results = provider === 'anthropic'
      ? messages.flatMap(message => Array.isArray(message.content) ? message.content.filter(part => part.type === 'tool_result').map(part => part.content) : [])
      : provider === 'gemini'
        ? messages.flatMap(message => (message.parts || []).filter(part => part.functionResponse).map(part => part.functionResponse.response))
        : messages.filter(message => message.role === 'tool').map(message => message.content);
    const findConfig = value => {
      if (typeof value === 'string' && value.startsWith('{"fixtureExecutable"')) { try { return JSON.parse(value); } catch {} }
      if (value && typeof value === 'object') { for (const item of Object.values(value)) { const found = findConfig(item); if (found) return found; } }
      return null;
    };
    const config = findConfig(body) || {};
    const step = results.length;
    if (model === 'tool-limits') {
      if (step >= 2) return emit('Tool limits verified.');
      return emit('', [{ id: `limit_${step}`, name: step === 0 ? 'read_file' : 'run_process', args: step === 0 ? { path: 'large.txt' } : { executable: 'dotnet', arguments: [config.fixtureExecutable, 'stderr'] } }]);
    }
    if (model === 'tool-many') return emit('', Array.from({ length: 33 }, (_, index) => ({ id: `many_${index}`, name: 'write_file', args: { path: 'should-not-exist.txt', content: 'bad' } })));
    if (model === 'tool-duplicate') return emit('', [{ id: 'duplicate', name: 'write_file', args: { path: 'duplicate.txt', content: 'first' } }, { id: 'duplicate', name: 'write_file', args: { path: 'duplicate.txt', content: 'second' } }]);
    if (model === 'tool-failure' && step) return emit(JSON.stringify(results).includes('Unknown local tool') ? 'Tool error recovered.' : 'Missing error result.');
    if (model === 'tool-failure') return emit('', [{ id: 'invalid', name: 'unknown_local_tool', args: {} }]);
    if (model === 'tool-loop') return emit('', [{ id: `loop_${step}`, name: 'list_directory', args: { path: '.' } }]);
    if (model === 'tool-cancel') return emit('', [{ id: 'hang', name: 'run_process', args: { executable: 'dotnet', arguments: [config.fixtureExecutable, 'tool-hang', 'tool.pid'] } }]);
    if (model === 'tool-broken') return emit('', [{ id: 'broken', name: 'write_file', args: { path: 'should-not-exist.txt', content: 'bad' } }], true);
    const sequence = [
      { name: 'write_file', args: { path: 'tool fixture.txt', content: 'Local tool evidence 議論' } },
      { name: 'read_file', args: { path: 'tool fixture.txt' } },
      { name: 'list_directory', args: { path: '.' } },
      { name: 'run_process', args: { executable: 'dotnet', arguments: [config.fixtureExecutable, 'tool-report', 'two words', '$(literal)', ''] } },
    ];
    if (step < sequence.length) return emit('', [{ id: `call_${step}`, ...sequence[step] }]);
    const resultText = JSON.stringify(results);
    const success = !resultText.includes('"success":false') && resultText.includes('Local tool evidence') && resultText.includes('two words') && resultText.includes('$(literal)');
    if (provider === 'gemini' && !JSON.stringify(body).includes('fixture-thought-signature')) return emit('Lost thought signature.');
    return emit(success ? 'Local tools verified.' : 'Tool results did not match fixture.');
  });
});
server.listen(Number(process.env.PORT), '127.0.0.1', () => process.stdout.write(JSON.stringify({ port: server.address().port }) + '\n'));

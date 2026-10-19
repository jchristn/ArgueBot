// Contract source: PolyPrompt's documented Ollama /api/chat NDJSON protocol.
// Answers only the completion path exercised by ArgueBot; no tools execute.
const http = require('http');
http.createServer((req, res) => {
  const path = new URL(req.url, 'http://127.0.0.1').pathname;
  if (req.method !== 'POST' || path !== '/api/chat') {
    res.writeHead(501, { 'Content-Type': 'application/json' });
    return res.end(JSON.stringify({ error: 'Outside this simulation contract' }));
  }
  let raw = '';
  req.on('data', chunk => { raw += chunk; });
  req.on('end', () => {
    let body;
    try { body = req.body || JSON.parse(raw); } catch {
      res.writeHead(400); return res.end('{"error":"invalid JSON"}');
    }
    if (!body.model || !Array.isArray(body.messages) || !body.stream) {
      res.writeHead(400); return res.end('{"error":"model, messages and stream required"}');
    }
    if (body.model === 'rate-limit') {
      res.writeHead(429, { 'Content-Type': 'application/json', 'Retry-After': '1' });
      return res.end('{"error":"fixture rate limit"}');
    }
    res.writeHead(200, { 'Content-Type': 'application/x-ndjson' });
    res.write(JSON.stringify({ model: body.model, message: { role: 'assistant', content: 'Fixture ' }, done: false }) + '\n');
    if (body.model === 'broken-stream') return res.end();
    res.end(JSON.stringify({ model: body.model, message: { role: 'assistant', content: 'answer.' }, done: true, done_reason: 'stop', prompt_eval_count: 10, eval_count: 2 }) + '\n');
  });
}).listen(Number(process.env.PORT), '127.0.0.1');

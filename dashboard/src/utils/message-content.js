/** Remove one surrounding Markdown code fence (```json ... ```), including an unterminated one. */
export function unfence(content) {
  const trimmed = (content || "").trim();
  if (!trimmed.startsWith("```")) return content || "";
  const newline = trimmed.indexOf("\n");
  if (newline < 0) return content;
  const body = trimmed.slice(newline + 1);
  const close = body.lastIndexOf("```");
  return close >= 0 ? body.slice(0, close) : body;
}

/** The decoded text of a JSON string field in incomplete JSON, with whether the string was closed. */
export function partialField(json, field) {
  const match = new RegExp(`"${field}"\\s*:\\s*"`).exec(json);
  if (!match) return null;
  let text = "";
  let complete = false;
  for (let index = match.index + match[0].length; index < json.length; index++) {
    const character = json[index];
    if (character === '"') { complete = true; break; }
    if (character !== "\\") { text += character; continue; }
    if (++index >= json.length) break;
    const escape = json[index];
    if (escape === "n") text += "\n";
    else if (escape === "t") text += "\t";
    else if (escape === "u") { if (index + 4 >= json.length) break; text += String.fromCharCode(parseInt(json.slice(index + 1, index + 5), 16)); index += 4; }
    else if (!"rbf".includes(escape)) text += escape;
  }
  return text ? { text, complete } : null;
}

/**
 * Orchestration and vote replies are JSON objects, sometimes inside a code fence. Show their main field and
 * rationale; for previews and streaming replies that are cut off, show the readable part of the main field.
 */
export function messageContent(message) {
  const field = { question: "question", synthesis: "proposal", compaction: "summary", ratification: "position" }[message.kind];
  if (!field || ["failed", "interrupted"].includes(message.state)) return message.content;
  const text = unfence(message.content);
  if (!text.trimStart().startsWith("{")) return message.content;
  const shape = value => message.kind === "ratification" ? `**${value.replaceAll("_", " ")}**` : value;
  try {
    const value = JSON.parse(text);
    if (typeof value[field] !== "string") return message.content;
    return shape(value[field]) + (typeof value.rationale === "string" && value.rationale ? "\n\n" + value.rationale : "");
  } catch {
    const partial = partialField(text, field);
    return partial ? shape(partial.text) + (partial.complete ? "" : "\u2026") : message.content;
  }
}

const toolField = (content, name) => content.match(new RegExp(`^${name}: (\\S+)$`, "m"))?.[1] || "";
/** Hide a tool's "started" record when its finished record for the same attempt and call is in the list; storage keeps both. */
export function mergeToolActivity(messages) {
  const key = message => `${toolField(message.content, "Attempt")}|${toolField(message.content, "Call")}`;
  const finished = new Set(messages.filter(message => message.kind === "tool" && toolField(message.content, "Status") && toolField(message.content, "Status") !== "started").map(key));
  return messages.filter(message => !(message.kind === "tool" && toolField(message.content, "Status") === "started" && finished.has(key(message))));
}

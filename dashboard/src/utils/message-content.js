export function messageContent(message) {
  const field = { question: "question", synthesis: "proposal", compaction: "summary", ratification: "position" }[message.kind];
  if (!field || message.state !== "completed" || message.contentTruncated) return message.content;
  try {
    const value = JSON.parse(message.content);
    if (typeof value[field] !== "string") return message.content;
    const main = message.kind === "ratification" ? `**${value[field].replaceAll("_", " ")}**` : value[field];
    return main + (typeof value.rationale === "string" && value.rationale ? "\n\n" + value.rationale : "");
  } catch { return message.content; }
}

const toolField = (content, name) => content.match(new RegExp(`^${name}: (\\S+)$`, "m"))?.[1] || "";
/** Hide a tool's "started" record when its finished record for the same attempt and call is in the list; storage keeps both. */
export function mergeToolActivity(messages) {
  const key = message => `${toolField(message.content, "Attempt")}|${toolField(message.content, "Call")}`;
  const finished = new Set(messages.filter(message => message.kind === "tool" && toolField(message.content, "Status") && toolField(message.content, "Status") !== "started").map(key));
  return messages.filter(message => !(message.kind === "tool" && toolField(message.content, "Status") === "started" && finished.has(key(message))));
}

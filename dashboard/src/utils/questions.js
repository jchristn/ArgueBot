const askTool = /^Conversation tool: ask_user\n/;

/** For an ask_user tool activity record, the question request it created, so the transcript can open the question modal. */
export function questionToolRequest(content) {
  const text = content || "";
  if (!askTool.test(text)) return null;
  return /^ {2}requestId: (qrq_\S+)$/m.exec(text.split("\nResult:\n")[1] || "")?.[1] || null;
}

/** Question kinds shown as chips in lists. */
export const questionKinds = ["text", "single_choice", "multi_choice", "confirm"];

/** Split a comma-separated tag field into lowercase tags without duplicates. */
export function parseTags(text) {
  return [...new Set((text || "").split(",").map(tag => tag.trim().toLowerCase()).filter(Boolean))];
}

/** Whether a name follows the resource naming rules the service enforces. */
export function validResourceName(name) {
  const value = (name || "").trim().toLowerCase();
  return /^[a-z0-9][a-z0-9._/-]{0,127}$/.test(value) && value.split("/").every(segment => segment && segment !== "." && segment !== "..");
}

/** UTF-8 byte length, the unit of the 256 KiB body limit. */
export function byteLength(text) {
  return new TextEncoder().encode(text || "").length;
}

/** Human-readable size. */
export function formatSize(bytes) {
  return bytes < 1024 ? bytes + " B" : (bytes / 1024).toFixed(1).replace(/\.0$/, "") + " KiB";
}

/**
 * Line diff from a longest common subsequence. Returns rows of { kind: "same" | "added" | "removed", text }.
 * Inputs over 2000 lines fall back to whole-text replacement so the browser stays responsive.
 */
export function lineDiff(before, after) {
  const left = (before || "").split("\n");
  const right = (after || "").split("\n");
  if (before === after) return left.map(text => ({ kind: "same", text }));
  if (left.length > 2000 || right.length > 2000) return [...left.map(text => ({ kind: "removed", text })), ...right.map(text => ({ kind: "added", text }))];
  const table = Array.from({ length: left.length + 1 }, () => new Uint16Array(right.length + 1));
  for (let i = left.length - 1; i >= 0; i--) for (let j = right.length - 1; j >= 0; j--) table[i][j] = left[i] === right[j] ? table[i + 1][j + 1] + 1 : Math.max(table[i + 1][j], table[i][j + 1]);
  const rows = [];
  let i = 0, j = 0;
  while (i < left.length && j < right.length) {
    if (left[i] === right[j]) { rows.push({ kind: "same", text: left[i] }); i++; j++; }
    else if (table[i + 1][j] >= table[i][j + 1]) rows.push({ kind: "removed", text: left[i++] });
    else rows.push({ kind: "added", text: right[j++] });
  }
  while (i < left.length) rows.push({ kind: "removed", text: left[i++] });
  while (j < right.length) rows.push({ kind: "added", text: right[j++] });
  return rows;
}

const resourceTool = /^Conversation tool: (read_resource|create_resource|update_resource|edit_resource|append_resource)\n/;

/** For a completed, successful resource tool message, the resource name and revision it read or produced. */
export function resourceToolLink(content) {
  const text = content || "";
  const tool = resourceTool.exec(text);
  if (!tool || !/\nStatus: completed\n/.test(text)) return null;
  const result = text.split("\nResult:\n")[1];
  if (!result || !/^success: true$/m.test(result)) return null;
  const name = /^ {2}name: (.+)$/m.exec(result)?.[1];
  const revision = Number(/^ {2}revision: (\d+)$/m.exec(result)?.[1]);
  return name && revision ? { tool: tool[1], name, revision } : null;
}

/** Read browser File objects as import entries. */
export async function readImportFiles(files) {
  return Promise.all([...files].map(async file => ({ fileName: file.name, content: await file.text() })));
}

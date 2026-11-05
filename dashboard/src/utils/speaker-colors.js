// Distinct, well-separated hues. Agents get them in roster order, then in order of first appearance,
// so every agent in a conversation has its own color for up to this many agents.
export const speakerPalette = [215, 28, 150, 330, 265, 185, 48, 0, 95, 295, 240, 120];

/** Map each agent name to a hue; the moderator ("user") is not assigned one. */
export function speakerHues(rosterNames = [], messageSpeakers = []) {
  const hues = new Map();
  for (const name of [...rosterNames, ...messageSpeakers]) {
    if (!name || name === "user" || hues.has(name)) continue;
    const index = hues.size;
    hues.set(name, index < speakerPalette.length ? speakerPalette[index] : (speakerPalette[index % speakerPalette.length] + 17 * Math.floor(index / speakerPalette.length)) % 360);
  }
  return hues;
}

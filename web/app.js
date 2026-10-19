// ArgueBot web client. No build step: plain ES modules served by src/web/server.ts.

const LABELS = { claude: "Claude Code", codex: "Codex", gemini: "Gemini", user: "You" };
const SETTINGS_KEY = "arguebot.settings";

const $ = (id) => document.getElementById(id);

// Keep the theme preference separate from debate settings.
const themeMedia = window.matchMedia("(prefers-color-scheme: light)");
let savedTheme;
try { savedTheme = localStorage.getItem("arguebot.theme"); } catch { /* Storage may be unavailable. */ }
function applyTheme(theme) {
  document.documentElement.dataset.theme = theme;
  const toggle = $("theme-toggle");
  const light = theme === "light";
  toggle.title = toggle.ariaLabel = `Switch to ${light ? "dark" : "light"} mode`;
  toggle.innerHTML = light
    ? '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M20.9 13A9 9 0 0 1 11 3.1 9 9 0 1 0 20.9 13Z"/></svg>'
    : '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><circle cx="12" cy="12" r="4"/><path d="M12 2v2m0 16v2M2 12h2m16 0h2M5 5l1.5 1.5m11 11L19 19M5 19l1.5-1.5m11-11L19 5"/></svg>';
}
applyTheme(savedTheme === "light" || savedTheme === "dark" ? savedTheme : themeMedia.matches ? "light" : "dark");
$("theme-toggle").addEventListener("click", () => {
  savedTheme = document.documentElement.dataset.theme === "light" ? "dark" : "light";
  applyTheme(savedTheme);
  try { localStorage.setItem("arguebot.theme", savedTheme); } catch { /* Still works for this page. */ }
});
themeMedia.addEventListener("change", (event) => {
  if (savedTheme !== "light" && savedTheme !== "dark") applyTheme(event.matches ? "light" : "dark");
});

// Labels share their field's help, including when hovering over label text.
for (const label of document.querySelectorAll("label")) {
  label.title = label.querySelector("input, select, textarea")?.title || "";
}
// Cover links and informational labels inserted by streamed debate content.
function addContentHelp(root) {
  for (const el of root.querySelectorAll("a, .label, .turn-speaker, .turn-meta, .turn-tag, .round-divider, .chip, option")) {
    if (el.title) continue;
    el.title = el.matches("a") ? `Open ${el.textContent.trim() || "link"}`
      : el.matches("option") ? `Select ${el.textContent.trim()}`
      : el.matches(".turn-meta") ? "Time taken to generate this response"
      : el.matches(".turn-speaker") ? `Author of this message: ${el.textContent.trim()}`
      : el.matches(".turn-tag") ? `Message type: ${el.textContent.trim()}`
      : el.matches(".label") ? "The topic being debated"
      : el.matches(".chip") ? `Debate participant: ${el.textContent.trim()}`
      : `Messages in ${el.textContent.trim()}`;
  }
}
new MutationObserver((records) => {
  for (const record of records) {
    if (record.type === "childList") addContentHelp(record.target);
  }
}).observe(document.body, { childList: true, subtree: true });

// ---------------------------------------------------------------------------
// Utilities
// ---------------------------------------------------------------------------

function escapeHtml(text) {
  return text.replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
}

function agentColor(speaker) {
  return `var(--${speaker})`;
}

async function api(path, body) {
  const res = await fetch(path, {
    method: body === undefined ? "GET" : "POST",
    headers: body === undefined ? {} : { "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const data = await res.json().catch(() => ({}));
  if (!res.ok) {
    throw new Error(data.error || `${res.status} ${res.statusText}`);
  }
  return data;
}

let toastTimer = null;
function toast(message, level = "error") {
  const el = $("toast");
  el.textContent = message;
  el.className = `toast ${level}`;
  el.hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => (el.hidden = true), 4000);
}

function formatDuration(ms) {
  if (ms === undefined) return "";
  return ms < 60_000 ? `${(ms / 1000).toFixed(1)}s` : `${Math.floor(ms / 60_000)}m ${Math.round((ms % 60_000) / 1000)}s`;
}

// ---------------------------------------------------------------------------
// Markdown: everything is escaped first, then a small set of known constructs
// is turned into tags. Agent output can never inject raw HTML.
// ---------------------------------------------------------------------------

const LIST_RE = /^(\s*)([-*+]|\d+[.)])\s+(.*)$/;
const TABLE_SEPARATOR_RE = /^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$/;

function renderInline(text) {
  const codeSpans = [];
  let html = escapeHtml(text).replace(/`([^`\n]+)`/g, (_, code) => {
    codeSpans.push(code);
    return `\u0000${codeSpans.length - 1}\u0000`;
  });

  html = html
    .replace(/\[([^\]]+)\]\((https?:\/\/[^\s)]+)\)/g, '<a href="$2" target="_blank" rel="noopener noreferrer">$1</a>')
    .replace(/\*\*(?=\S)(.+?)\*\*/g, "<strong>$1</strong>")
    .replace(/__(?=\S)(.+?)__/g, "<strong>$1</strong>")
    .replace(/(^|[\s(])\*(?=\S)([^*\n]+?)\*(?=[\s).,;:!?]|$)/g, "$1<em>$2</em>")
    .replace(/(^|[\s(])_(?=\S)([^_\n]+?)_(?=[\s).,;:!?]|$)/g, "$1<em>$2</em>");

  return html.replace(/\u0000(\d+)\u0000/g, (_, index) => `<code>${codeSpans[Number(index)]}</code>`);
}

function splitTableRow(line) {
  return line.trim().replace(/^\|/, "").replace(/\|$/, "").split("|").map((cell) => cell.trim());
}

function buildList(items) {
  let html = "";
  const stack = [];
  for (const item of items) {
    while (stack.length && item.indent < stack[stack.length - 1].indent) {
      html += `</li></${stack.pop().tag}>`;
    }
    const top = stack[stack.length - 1];
    if (!top || item.indent > top.indent) {
      const tag = item.ordered ? "ol" : "ul";
      stack.push({ indent: item.indent, tag });
      html += `<${tag}>`;
    } else {
      html += "</li>";
    }
    html += `<li>${item.text.split("\n").map(renderInline).join("<br>")}`;
  }
  while (stack.length) {
    html += `</li></${stack.pop().tag}>`;
  }
  return html;
}

function renderMarkdown(source) {
  const lines = source.replace(/\r/g, "").split("\n");
  let html = "";
  let paragraph = [];
  let i = 0;

  const flush = () => {
    if (paragraph.length) {
      html += `<p>${paragraph.map(renderInline).join("<br>")}</p>`;
      paragraph = [];
    }
  };

  while (i < lines.length) {
    const line = lines[i];

    const fence = /^\s*(```|~~~)/.exec(line);
    if (fence) {
      flush();
      const body = [];
      i += 1;
      while (i < lines.length && !lines[i].trim().startsWith(fence[1])) {
        body.push(lines[i]);
        i += 1;
      }
      i += 1;
      html += `<pre><code>${escapeHtml(body.join("\n"))}</code></pre>`;
      continue;
    }

    if (!line.trim()) {
      flush();
      i += 1;
      continue;
    }

    const heading = /^(#{1,6})\s+(.*)$/.exec(line);
    if (heading) {
      flush();
      const level = Math.min(4, heading[1].length);
      html += `<h${level}>${renderInline(heading[2].replace(/\s*#+\s*$/, ""))}</h${level}>`;
      i += 1;
      continue;
    }

    if (/^\s*([-*_])(\s*\1){2,}\s*$/.test(line)) {
      flush();
      html += "<hr>";
      i += 1;
      continue;
    }

    if (line.includes("|") && i + 1 < lines.length && TABLE_SEPARATOR_RE.test(lines[i + 1])) {
      flush();
      const header = splitTableRow(line);
      i += 2;
      let rows = "";
      while (i < lines.length && lines[i].includes("|") && lines[i].trim()) {
        rows += `<tr>${splitTableRow(lines[i]).map((cell) => `<td>${renderInline(cell)}</td>`).join("")}</tr>`;
        i += 1;
      }
      html += `<table><thead><tr>${header.map((cell) => `<th>${renderInline(cell)}</th>`).join("")}</tr></thead><tbody>${rows}</tbody></table>`;
      continue;
    }

    if (/^\s*>/.test(line)) {
      flush();
      const quoted = [];
      while (i < lines.length && /^\s*>/.test(lines[i])) {
        quoted.push(lines[i].replace(/^\s*>\s?/, ""));
        i += 1;
      }
      html += `<blockquote>${renderMarkdown(quoted.join("\n"))}</blockquote>`;
      continue;
    }

    if (LIST_RE.test(line)) {
      flush();
      const items = [];
      while (i < lines.length) {
        const match = LIST_RE.exec(lines[i]);
        if (match) {
          items.push({ indent: match[1].length, ordered: /\d/.test(match[2]), text: match[3] });
          i += 1;
        } else if (lines[i].trim() && /^\s+/.test(lines[i])) {
          items[items.length - 1].text += `\n${lines[i].trim()}`;
          i += 1;
        } else if (!lines[i].trim() && i + 1 < lines.length && LIST_RE.test(lines[i + 1])) {
          i += 1;
        } else {
          break;
        }
      }
      html += buildList(items);
      continue;
    }

    paragraph.push(line);
    i += 1;
  }

  flush();
  return html;
}

// ---------------------------------------------------------------------------
// Setup view
// ---------------------------------------------------------------------------

const setup = {
  agents: [],
  selected: [],
  server: null,
};

function loadSettings() {
  try {
    return JSON.parse(localStorage.getItem(SETTINGS_KEY) || "{}");
  } catch {
    return {};
  }
}

function saveSettings(settings) {
  localStorage.setItem(SETTINGS_KEY, JSON.stringify(settings));
}

function isAgentReady(agent) {
  return agent.installed && !agent.setupError;
}

function renderAgentGrid() {
  const grid = $("agent-grid");
  grid.innerHTML = "";

  for (const agent of setup.agents) {
    const ready = isAgentReady(agent);
    const order = setup.selected.indexOf(agent.name);
    const card = document.createElement("button");
    card.type = "button";
    card.className = `agent-card${order >= 0 ? " selected" : ""}`;
    card.style.setProperty("--agent-color", agentColor(agent.name));
    card.disabled = !ready;
    card.title = !ready ? `${agent.label}: ${agent.setupError || "CLI is not installed"}`
      : order >= 0 ? `Remove ${agent.label} from the debate (speaking position ${order + 1})`
      : `Add ${agent.label} as the next speaker`;
    card.setAttribute("aria-pressed", String(order >= 0));
    card.innerHTML = `
      <span class="agent-name">${escapeHtml(agent.label)}</span>
      <span class="agent-state ${ready ? "" : "bad"}">${ready ? "Ready" : agent.installed ? "Needs setup" : "Not installed"}</span>
      ${ready ? "" : `<span class="agent-detail">${escapeHtml(agent.setupError || "")}</span>`}
      ${order >= 0 ? `<span class="agent-order" title="Speaking order">${order + 1}</span>` : ""}
    `;
    card.addEventListener("click", () => {
      setup.selected = order >= 0
        ? setup.selected.filter((name) => name !== agent.name)
        : [...setup.selected, agent.name];
      renderAgentGrid();
    });
    grid.appendChild(card);
  }

  const summary = $("summary-agent");
  const previous = summary.value;
  summary.innerHTML = setup.selected
    .map((name) => `<option value="${name}">${LABELS[name]}</option>`)
    .join("");
  if (setup.selected.includes(previous)) {
    summary.value = previous;
  }

  updateStartButton();
}

function updateStartButton() {
  $("start-button").disabled = setup.selected.length < 2 || !$("topic").value.trim();
}

async function renderRecent() {
  const { sessions } = await api("/api/sessions").catch(() => ({ sessions: [] }));
  $("recent").hidden = sessions.length === 0;
  $("recent-list").innerHTML = sessions
    .map((session) => {
      const state = session.phase === "ended" || session.phase === "followup"
        ? { consensus: "Consensus", max_rounds: "No consensus", stopped: "Stopped", failed: "Failed" }[session.endReason]
        : "In progress";
      const who = session.participants.map((name) => LABELS[name]).join(" vs ");
      return `<li><a href="/?session=${encodeURIComponent(session.id)}">
        <span class="recent-topic">${escapeHtml(session.topic)}</span>
        <span class="recent-meta">${escapeHtml(who)} · ${state}</span>
      </a></li>`;
    })
    .join("");
}

async function showSetup() {
  $("setup-view").hidden = false;
  $("debate-view").hidden = true;
  $("topbar-debate").hidden = true;
  $("topbar-actions").hidden = true;
  document.title = "ArgueBot";

  const settings = loadSettings();
  let data;
  try {
    data = await api("/api/agents");
  } catch (err) {
    $("setup-error").textContent = `Could not reach the ArgueBot server: ${err.message}`;
    $("setup-error").hidden = false;
    return;
  }

  setup.agents = data.agents;
  setup.server = data;
  applySettings(settings);

  renderRecent();
  $("topic").focus();
}

/**
 * Fill the settings form. Flags passed when the server was started win over
 * settings saved in this browser, which win over the built-in defaults.
 */
function applySettings(settings) {
  const { defaults, cliFlags, cwd } = setup.server;
  const readyNames = setup.agents.filter(isAgentReady).map((agent) => agent.name);
  const flagged = [cliFlags.firstAgent, cliFlags.secondAgent].filter(Boolean);
  const preferred = flagged.length > 0 ? flagged : settings.participants || [];
  setup.selected = [...new Set(preferred)].filter((name) => readyNames.includes(name));
  if (setup.selected.length < 2) {
    setup.selected = [...new Set([...setup.selected, ...readyNames])].slice(0, 2);
  }

  const seconds = (ms) => (ms === undefined ? undefined : ms / 1000);
  $("max-rounds").value = cliFlags.maxRounds ?? settings.maxRounds ?? defaults.maxRounds;
  $("intervention-seconds").value =
    seconds(cliFlags.interventionTimeoutMs) ?? settings.interventionSeconds ?? seconds(defaults.interventionTimeoutMs);
  $("agent-timeout-seconds").value =
    seconds(cliFlags.agentTimeoutMs) ?? settings.agentTimeoutSeconds ?? seconds(defaults.agentTimeoutMs);
  $("cwd").value = settings.cwd || cwd;
  $("yolo").checked = cliFlags.yoloMode ?? settings.yolo ?? defaults.yoloMode;
  $("verbose").checked = cliFlags.verbose ?? settings.verbose ?? defaults.verbose;

  renderAgentGrid();
  const summaryAgent = cliFlags.summaryAgent ?? settings.summaryAgent;
  if (summaryAgent && setup.selected.includes(summaryAgent)) {
    $("summary-agent").value = summaryAgent;
  }
}

$("reset-settings").addEventListener("click", () => {
  localStorage.removeItem(SETTINGS_KEY);
  applySettings({});
});

async function startDebate() {
  const button = $("start-button");
  if (button.disabled) return;

  const settings = {
    participants: setup.selected,
    maxRounds: Number($("max-rounds").value),
    interventionSeconds: Number($("intervention-seconds").value),
    agentTimeoutSeconds: Number($("agent-timeout-seconds").value),
    summaryAgent: $("summary-agent").value,
    cwd: $("cwd").value.trim(),
    yolo: $("yolo").checked,
    verbose: $("verbose").checked,
  };

  button.disabled = true;
  $("setup-error").hidden = true;
  try {
    const { id } = await api("/api/sessions", { ...settings, topic: $("topic").value.trim() });
    saveSettings(settings);
    history.pushState(null, "", `/?session=${encodeURIComponent(id)}`);
    showDebate(id);
  } catch (err) {
    $("setup-error").textContent = err.message;
    $("setup-error").hidden = false;
    button.disabled = false;
  }
}

$("topic").addEventListener("input", updateStartButton);
$("topic").addEventListener("keydown", (event) => {
  if (event.key === "Enter" && (event.metaKey || event.ctrlKey)) {
    event.preventDefault();
    startDebate();
  }
});
$("start-button").addEventListener("click", startDebate);

// ---------------------------------------------------------------------------
// Debate view
// ---------------------------------------------------------------------------

const debate = {
  id: null,
  source: null,
  snapshot: null,
  turnElements: new Map(),
  lastRound: null,
  followupDividerShown: false,
  renderQueued: new Set(),
  composerMode: "note",
  holdSent: false,
  countdownFrame: null,
};

function act(action, extra = {}) {
  return api(`/api/sessions/${encodeURIComponent(debate.id)}/actions`, { action, ...extra }).catch((err) => {
    toast(err.message);
    throw err;
  });
}

function isNearBottom() {
  const feed = $("feed");
  return feed.scrollHeight - feed.scrollTop - feed.clientHeight < 120;
}

function withStickyScroll(fn) {
  const stick = isNearBottom();
  fn();
  if (stick) {
    const feed = $("feed");
    feed.scrollTop = feed.scrollHeight;
  }
}

function appendToFeed(element) {
  $("feed").appendChild(element);
}

function makeElement(className, html) {
  const el = document.createElement("div");
  el.className = className;
  el.innerHTML = html;
  return el;
}

function turnTags(turn) {
  const tags = [];
  if (turn.speaker === "user") {
    if (turn.steer) tags.push(`<span class="turn-tag steer">Directive for next speaker only</span>`);
    else if (turn.type === "final") tags.push(`<span class="turn-tag">Follow-up question</span>`);
    else tags.push(`<span class="turn-tag">Moderator note</span>`);
    return tags.join("");
  }
  if (turn.type === "opening") tags.push(`<span class="turn-tag">Opening</span>`);
  if (turn.type === "consensus") tags.push(`<span class="turn-tag" style="color: var(--success)">Consensus</span>`);
  if (turn.type === "final") tags.push(`<span class="turn-tag">Follow-up answer</span>`);
  if (turn.steer) tags.push(`<span class="turn-tag steer">Steered</span>`);
  if (turn.interrupted) tags.push(`<span class="turn-tag steer">Interrupted</span>`);
  return tags.join("");
}

function renderTurnBody(el, turn) {
  const body = el.querySelector(".turn-body");
  if (turn.error) {
    body.className = "turn-body";
    body.textContent = turn.error;
  } else if (turn.streaming && !turn.content) {
    body.className = "turn-body waiting";
    body.textContent = `${LABELS[turn.speaker]} is thinking…`;
  } else {
    body.className = "turn-body md";
    body.innerHTML = renderMarkdown(turn.content);
  }
}

function renderTurn(turn) {
  let el = debate.turnElements.get(turn.id);
  if (!el) {
    el = document.createElement("article");
    debate.turnElements.set(turn.id, el);
    insertDividers(turn);
    appendToFeed(el);
  }

  const classes = ["turn"];
  if (turn.speaker === "user") classes.push(turn.type === "final" ? "question" : "moderator");
  if (turn.error) classes.push("error");
  if (turn.type === "consensus") classes.push("consensus");
  if (turn.streaming) classes.push("streaming");
  el.className = classes.join(" ");
  if (turn.speaker !== "user") el.style.setProperty("--agent-color", agentColor(turn.speaker));

  const meta = turn.speaker === "user" ? "" : formatDuration(turn.durationMs);
  el.innerHTML = `
    <div class="turn-header">
      <span class="turn-speaker">${LABELS[turn.speaker]}</span>
      ${turnTags(turn)}
      <span class="turn-meta">${meta}</span>
    </div>
    <div class="turn-body"></div>
  `;
  renderTurnBody(el, turn);
}

function insertDividers(turn) {
  if (turn.type === "final") {
    if (!debate.followupDividerShown) {
      debate.followupDividerShown = true;
      appendToFeed(makeElement("round-divider", "Follow-up"));
    }
    return;
  }
  if (turn.speaker !== "user" && turn.round !== debate.lastRound) {
    debate.lastRound = turn.round;
    appendToFeed(makeElement("round-divider", `Round ${turn.round}`));
  }
}

function findTurn(id) {
  return debate.snapshot.turns.find((turn) => turn.id === id);
}

function upsertTurn(turn) {
  const turns = debate.snapshot.turns;
  const index = turns.findIndex((existing) => existing.id === turn.id);
  if (index >= 0) turns[index] = turn;
  else turns.push(turn);
  withStickyScroll(() => renderTurn(turn));
}

function scheduleBodyRender(id) {
  if (debate.renderQueued.has(id)) return;
  debate.renderQueued.add(id);
  requestAnimationFrame(() => {
    debate.renderQueued.delete(id);
    const turn = findTurn(id);
    const el = debate.turnElements.get(id);
    if (turn && el) withStickyScroll(() => renderTurnBody(el, turn));
  });
}

function endBanner(reason) {
  const text = {
    consensus: ["consensus", "Consensus reached"],
    max_rounds: ["neutral", "Out of rounds without consensus"],
    stopped: ["neutral", "Debate stopped"],
    failed: ["neutral", "Debate ended after repeated agent errors"],
  }[reason];
  return text ? makeElement(`banner ${text[0]}`, text[1]) : null;
}

function renderFeed() {
  const feed = $("feed");
  feed.innerHTML = "";
  debate.turnElements.clear();
  debate.lastRound = null;
  debate.followupDividerShown = false;

  const { topic, turns, status } = debate.snapshot;
  const topicCard = makeElement("topic-card", `<div class="label">Topic</div><div class="md"></div>`);
  topicCard.querySelector(".md").innerHTML = renderMarkdown(topic);
  feed.appendChild(topicCard);

  let bannerShown = false;
  for (const turn of turns) {
    if (turn.type === "final" && !bannerShown && status.endReason) {
      bannerShown = true;
      const banner = endBanner(status.endReason);
      if (banner) feed.appendChild(banner);
    }
    renderTurn(turn);
  }
  if (!bannerShown && status.endReason) {
    const banner = endBanner(status.endReason);
    if (banner) feed.appendChild(banner);
  }

  feed.scrollTop = feed.scrollHeight;
}

function applyStatus(status) {
  const previous = debate.snapshot.status;
  debate.snapshot.status = status;
  debate.snapshot.config.maxRounds = status.maxRounds;

  if (status.phase === "ended" && previous.phase !== "ended" && previous.phase !== "followup") {
    const banner = endBanner(status.endReason);
    if (banner) withStickyScroll(() => appendToFeed(banner));
  }
  if (previous.phase !== status.phase) {
    debate.holdSent = false;
    if (status.phase === "window" && !status.windowHeld && $("composer-input").value.trim()) {
      debate.holdSent = true;
      act("hold").catch(() => {});
    }
  }

  renderChrome();
}

function phaseLabel(status) {
  switch (status.phase) {
    case "agent_turn":
      return ["Live", "live"];
    case "window":
      return [status.windowHeld ? "Holding" : "Live", "live"];
    case "paused":
      return ["Paused", "warn"];
    case "followup":
      return ["Follow-up", "live"];
    case "ended":
      return status.endReason === "consensus" ? ["Consensus", "consensus"] : ["Ended", ""];
  }
  return ["", ""];
}

function button(label, onClick, className = "") {
  const el = document.createElement("button");
  el.type = "button";
  el.className = `btn ${className}`;
  el.textContent = label;
  el.title = {
    "+2 rounds": "Add two rounds to the debate limit.",
    "End debate": "Stop the debate and allow follow-up questions.",
    "Cut in": "Interrupt the current speaker so you can step in now.",
    "Don't pause": "Cancel the requested pause after this turn.",
    "Pause after turn": "Let the current speaker finish, then pause the debate.",
    "Continue now": "Skip the remaining countdown and start the next turn.",
    "Finish now": "Skip the remaining countdown and finish the debate.",
    "Pause": "Hold the debate until you resume it.",
    "Resume": "Resume the debate from the next turn.",
    "Stop answer": "Interrupt the current follow-up answer.",
    "Keep debating (+2 rounds)": "Reopen the debate with two additional rounds.",
  }[label] || label;
  el.addEventListener("click", onClick);
  return el;
}

function renderChrome() {
  const { status, config } = debate.snapshot;
  const name = (agent) => `<strong>${LABELS[agent]}</strong>`;

  // Top bar
  $("topbar-participants").innerHTML = config.participants
    .map((agent) => `<span class="chip${status.activeSpeaker === agent ? " speaking" : ""}" style="--agent-color: ${agentColor(agent)}"><span class="dot"></span>${LABELS[agent]}</span>`)
    .join("");
  $("round-indicator").textContent = status.phase === "ended" || status.phase === "followup"
    ? ""
    : `Round ${Math.min(status.round, status.maxRounds)} of ${status.maxRounds}`;
  const [pillText, pillClass] = phaseLabel(status);
  $("phase-pill").textContent = pillText;
  $("phase-pill").className = `phase-pill ${pillClass}`;

  // Control bar
  const statusEl = $("control-status");
  const buttons = $("control-buttons");
  buttons.innerHTML = "";
  const extend = () => button("+2 rounds", () => act("extend", { rounds: 2 }));
  const stop = () => button("End debate", () => act("stop"), "btn-danger");

  switch (status.phase) {
    case "agent_turn":
      statusEl.innerHTML = status.pauseRequested
        ? `${name(status.activeSpeaker)} is arguing — will pause after this turn.`
        : `${name(status.activeSpeaker)} is arguing…`;
      buttons.append(
        button("Cut in", () => act("interrupt").then(() => $("composer-input").focus()).catch(() => {})),
        status.pauseRequested
          ? button("Don't pause", () => act("resume"))
          : button("Pause after turn", () => act("pause")),
        stop(),
      );
      break;

    case "window":
      if (status.windowHeld) {
        statusEl.innerHTML = status.nextSpeaker
          ? `Holding while you type. ${name(status.nextSpeaker)} is up next.`
          : "Holding while you type. That was the last turn.";
      } else {
        statusEl.innerHTML = status.nextSpeaker
          ? `${name(status.nextSpeaker)} speaks in <span id="countdown-seconds"></span> — start typing to hold.`
          : `Last turn done — wrapping up in <span id="countdown-seconds"></span>.`;
      }
      buttons.append(
        button(status.nextSpeaker ? "Continue now" : "Finish now", () => act("continue")),
        button("Pause", () => act("pause")),
        extend(),
        stop(),
      );
      break;

    case "paused":
      statusEl.innerHTML = status.nextSpeaker
        ? `Paused. Add a note to steer the debate, or resume when ready. ${name(status.nextSpeaker)} is up next.`
        : "Paused. Add a note, or resume when ready.";
      buttons.append(button("Resume", () => act("resume"), "btn-primary"), extend(), stop());
      break;

    case "followup":
      statusEl.innerHTML = `${name(status.activeSpeaker)} is answering…`;
      buttons.append(button("Stop answer", () => act("interrupt")));
      break;

    case "ended":
      statusEl.innerHTML = {
        consensus: "Consensus reached. Ask a follow-up question — e.g. “Summarize the agreed approach.”",
        max_rounds: "Out of rounds without consensus. Keep going, or ask for a summary of the trade-offs.",
        stopped: "Debate stopped. Ask a follow-up question to wrap up.",
        failed: "The agents kept failing. Check the errors above.",
      }[status.endReason] ?? "Debate ended.";
      if (status.endReason === "max_rounds") {
        buttons.append(button("Keep debating (+2 rounds)", () => act("extend", { rounds: 2 })));
      }
      break;
  }

  // Queued notes
  const queued = $("queued");
  queued.hidden = status.queuedNotes.length === 0;
  queued.innerHTML = status.queuedNotes
    .map((note) => `<span class="chip" title="${escapeHtml(note)}">Queued: ${escapeHtml(note.slice(0, 80))}</span>`)
    .join("");

  renderComposer();
  runCountdown();
}

function renderComposer() {
  const { status, config } = debate.snapshot;
  const input = $("composer-input");
  const debating = status.phase !== "ended" && status.phase !== "followup";

  $("composer-mode").hidden = !debating;
  $("followup-agent").hidden = debating;
  for (const modeButton of document.querySelectorAll(".composer-mode .mode")) {
    modeButton.classList.toggle("active", modeButton.dataset.mode === debate.composerMode);
  }

  if (!debating) {
    const select = $("followup-agent");
    if (select.options.length !== config.participants.length) {
      select.innerHTML = config.participants
        .map((agent) => `<option value="${agent}">${LABELS[agent]}</option>`)
        .join("");
      select.value = config.summaryAgent;
    }
  }

  const steer = debate.composerMode === "steer";
  const active = status.activeSpeaker ? LABELS[status.activeSpeaker] : "the current speaker";
  input.placeholder = {
    agent_turn: steer
      ? "Directive for the next speaker…"
      : `Queue a note for after ${active} finishes…`,
    window: steer ? "Directive for the next speaker…" : "Step in with a note — typing holds the countdown…",
    paused: steer ? "Directive for the next speaker…" : "Add a note — sending resumes the debate…",
    followup: `Waiting for ${active}…`,
    ended: status.endReason === "failed" ? "Ask a follow-up question…" : "Ask a follow-up question…",
  }[status.phase];

  const busy = status.phase === "followup";
  input.title = busy ? `Wait for ${active} to finish answering.`
    : `${input.placeholder} Enter to send; Shift + Enter for a newline.`;
  $("composer-send").title = busy ? `Wait for ${active} to finish answering.`
    : debating ? `Send a ${steer ? "private directive to the next speaker" : "note visible to every agent"}.`
    : "Send a follow-up question to the selected agent.";
  input.disabled = busy;
  $("composer-send").disabled = busy;

  $("composer-hint").textContent = debating
    ? "Enter to send · Shift+Enter for a newline · Note: everyone sees it · Steer: private directive to the next speaker · Commands: /pause /resume /stop /extend N"
    : "Enter to send · Shift+Enter for a newline";
}

function runCountdown() {
  cancelAnimationFrame(debate.countdownFrame);
  const { status, config } = debate.snapshot;
  const bar = $("countdown");
  const fill = $("countdown-fill");

  if (status.phase !== "window" || !status.windowDeadline) {
    bar.classList.remove("active");
    return;
  }

  bar.classList.add("active");
  const total = config.interventionTimeoutMs || 1;
  const tick = () => {
    const remaining = Math.max(0, status.windowDeadline - Date.now());
    fill.style.width = `${Math.min(100, (remaining / total) * 100)}%`;
    const label = $("countdown-seconds");
    if (label) label.textContent = `${Math.ceil(remaining / 1000)}s`;
    if (remaining > 0) debate.countdownFrame = requestAnimationFrame(tick);
  };
  tick();
}

function handleEvent(event) {
  switch (event.type) {
    case "snapshot":
      debate.snapshot = event.session;
      $("download-transcript").href = `/api/sessions/${encodeURIComponent(debate.id)}/transcript.md`;
      document.title = `${event.session.topic.slice(0, 60)} · ArgueBot`;
      renderFeed();
      renderChrome();
      break;

    case "status":
      applyStatus(event.status);
      break;

    case "turn_start":
    case "turn_end":
      upsertTurn(event.turn);
      break;

    case "delta": {
      const turn = findTurn(event.id);
      if (turn) {
        turn.content += event.text;
        scheduleBodyRender(event.id);
      }
      break;
    }

    case "notice":
      withStickyScroll(() => appendToFeed(makeElement(`notice ${event.level}`, escapeHtml(event.message))));
      break;
  }
}

function showDebate(id) {
  $("setup-view").hidden = true;
  $("debate-view").hidden = false;
  $("topbar-debate").hidden = false;
  $("topbar-actions").hidden = false;

  debate.source?.close();
  debate.id = id;
  debate.snapshot = null;
  $("feed").innerHTML = "";

  const source = new EventSource(`/api/sessions/${encodeURIComponent(id)}/events`);
  debate.source = source;
  source.onmessage = (message) => handleEvent(JSON.parse(message.data));
  source.onerror = () => {
    if (source.readyState === EventSource.CLOSED) {
      $("feed").innerHTML = "";
      appendToFeed(makeElement("banner neutral", `This debate isn't available — it may have been lost when the server restarted. <a href="/">Start a new one</a>.`));
      $("control-bar").hidden = true;
      $("composer").hidden = true;
    }
  };

  $("composer-input").focus();
}

// ---------------------------------------------------------------------------
// Composer
// ---------------------------------------------------------------------------

function parseCommand(text) {
  const match = /^\/(\w+)\s*([\s\S]*)$/.exec(text);
  if (!match) return null;
  const [, command, rest] = match;
  switch (command) {
    case "steer":
      return { action: "steer", text: rest };
    case "note":
      return { action: "note", text: rest };
    case "pause":
    case "resume":
    case "stop":
    case "continue":
    case "interrupt":
      return { action: command };
    case "extend":
      return { action: "extend", rounds: parseInt(rest, 10) || 2 };
    case "save":
      $("download-transcript").click();
      return { action: null };
    default:
      return undefined;
  }
}

async function submitComposer() {
  const input = $("composer-input");
  const text = input.value.trim();
  if (!text || !debate.snapshot) return;

  const { status } = debate.snapshot;
  let request;
  if (status.phase === "ended") {
    request = { action: "followup", text, agent: $("followup-agent").value };
  } else {
    const command = parseCommand(text);
    if (command === undefined) {
      toast(`Unknown command. Try /steer, /pause, /resume, /stop, /extend N.`);
      return;
    }
    request = command ?? { action: debate.composerMode, text };
  }

  if (!request.action) {
    input.value = "";
    autosize();
    return;
  }

  const { action, ...extra } = request;
  try {
    await act(action, extra);
    input.value = "";
    debate.holdSent = false;
    autosize();
    if (action === "note" && status.phase === "agent_turn") {
      toast("Note queued — it will be added after this turn.", "info");
    }
  } catch {
    // act() already reported the error
  }
}

function autosize() {
  const input = $("composer-input");
  input.style.height = "auto";
  input.style.height = `${Math.min(input.scrollHeight + 2, 220)}px`;
}

$("composer").addEventListener("submit", (event) => {
  event.preventDefault();
  submitComposer();
});

$("composer-input").addEventListener("keydown", (event) => {
  if (event.key === "Enter" && !event.shiftKey && !event.isComposing) {
    event.preventDefault();
    submitComposer();
  }
});

$("composer-input").addEventListener("input", () => {
  autosize();
  if (!debate.snapshot) return;
  const hasText = $("composer-input").value.trim().length > 0;
  const { status } = debate.snapshot;
  if (status.phase !== "window") return;
  if (hasText && !debate.holdSent) {
    debate.holdSent = true;
    act("hold").catch(() => {});
  } else if (!hasText && debate.holdSent) {
    debate.holdSent = false;
    act("release").catch(() => {});
  }
});

for (const modeButton of document.querySelectorAll(".composer-mode .mode")) {
  modeButton.addEventListener("click", () => {
    debate.composerMode = modeButton.dataset.mode;
    renderComposer();
    $("composer-input").focus();
  });
}

// ---------------------------------------------------------------------------
// Routing
// ---------------------------------------------------------------------------

function route() {
  const id = new URLSearchParams(location.search).get("session");
  if (id) {
    showDebate(id);
  } else {
    debate.source?.close();
    debate.source = null;
    showSetup();
  }
}

window.addEventListener("popstate", route);
route();

import chalk from "chalk";
import { StringDecoder } from "node:string_decoder";

type PromptResult = string | null;

export interface TextInputOptions {
  prompt: string;
  multiline?: boolean;
  timeoutMs?: number;
}

type SemanticEvent =
  | { type: "text"; text: string }
  | { type: "enter" }
  | { type: "backspace" }
  | { type: "delete" }
  | { type: "left" }
  | { type: "right" }
  | { type: "up" }
  | { type: "down" }
  | { type: "home" }
  | { type: "end" }
  | { type: "escape" }
  | { type: "pasteStart" }
  | { type: "pasteEnd" }
  | { type: "ctrlC" };

interface EditorState {
  lines: string[];
  cursorLine: number;
  cursorCol: number;
  isPasting: boolean;
  escPending: boolean;
}

interface RenderMetrics {
  rows: number;
  cursorRow: number;
}

function createInitialState(): EditorState {
  return {
    lines: [""],
    cursorLine: 0,
    cursorCol: 0,
    isPasting: false,
    escPending: false,
  };
}

function approximateWidth(text: string): number {
  return Array.from(text).length;
}

function splitForInsert(text: string): string[] {
  return text.split("\n");
}

function insertText(state: EditorState, text: string): EditorState {
  if (text.length === 0) {
    return state;
  }

  const currentLine = state.lines[state.cursorLine] ?? "";
  const before = currentLine.slice(0, state.cursorCol);
  const after = currentLine.slice(state.cursorCol);
  const segments = splitForInsert(text);
  const nextLines = [...state.lines];

  if (segments.length === 1) {
    nextLines[state.cursorLine] = before + segments[0] + after;
    return {
      ...state,
      lines: nextLines,
      cursorCol: state.cursorCol + segments[0].length,
      escPending: false,
    };
  }

  const replacement = [
    before + segments[0],
    ...segments.slice(1, -1),
    segments[segments.length - 1] + after,
  ];

  nextLines.splice(state.cursorLine, 1, ...replacement);

  return {
    ...state,
    lines: nextLines,
    cursorLine: state.cursorLine + segments.length - 1,
    cursorCol: segments[segments.length - 1].length,
    escPending: false,
  };
}

function insertNewline(state: EditorState): EditorState {
  return insertText(state, "\n");
}

function moveLeft(state: EditorState): EditorState {
  if (state.cursorCol > 0) {
    return { ...state, cursorCol: state.cursorCol - 1, escPending: false };
  }

  if (state.cursorLine === 0) {
    return { ...state, escPending: false };
  }

  return {
    ...state,
    cursorLine: state.cursorLine - 1,
    cursorCol: state.lines[state.cursorLine - 1].length,
    escPending: false,
  };
}

function moveRight(state: EditorState): EditorState {
  const line = state.lines[state.cursorLine];
  if (state.cursorCol < line.length) {
    return { ...state, cursorCol: state.cursorCol + 1, escPending: false };
  }

  if (state.cursorLine >= state.lines.length - 1) {
    return { ...state, escPending: false };
  }

  return {
    ...state,
    cursorLine: state.cursorLine + 1,
    cursorCol: 0,
    escPending: false,
  };
}

function moveUp(state: EditorState): EditorState {
  if (state.cursorLine === 0) {
    return { ...state, escPending: false };
  }

  return {
    ...state,
    cursorLine: state.cursorLine - 1,
    cursorCol: Math.min(state.cursorCol, state.lines[state.cursorLine - 1].length),
    escPending: false,
  };
}

function moveDown(state: EditorState): EditorState {
  if (state.cursorLine >= state.lines.length - 1) {
    return { ...state, escPending: false };
  }

  return {
    ...state,
    cursorLine: state.cursorLine + 1,
    cursorCol: Math.min(state.cursorCol, state.lines[state.cursorLine + 1].length),
    escPending: false,
  };
}

function moveHome(state: EditorState): EditorState {
  return { ...state, cursorCol: 0, escPending: false };
}

function moveEnd(state: EditorState): EditorState {
  return {
    ...state,
    cursorCol: state.lines[state.cursorLine].length,
    escPending: false,
  };
}

function backspace(state: EditorState): EditorState {
  if (state.cursorCol > 0) {
    const nextLines = [...state.lines];
    const line = nextLines[state.cursorLine];
    nextLines[state.cursorLine] = line.slice(0, state.cursorCol - 1) + line.slice(state.cursorCol);
    return {
      ...state,
      lines: nextLines,
      cursorCol: state.cursorCol - 1,
      escPending: false,
    };
  }

  if (state.cursorLine === 0) {
    return { ...state, escPending: false };
  }

  const nextLines = [...state.lines];
  const previous = nextLines[state.cursorLine - 1];
  const current = nextLines[state.cursorLine];
  nextLines.splice(state.cursorLine - 1, 2, previous + current);

  return {
    ...state,
    lines: nextLines,
    cursorLine: state.cursorLine - 1,
    cursorCol: previous.length,
    escPending: false,
  };
}

function deleteForward(state: EditorState): EditorState {
  const current = state.lines[state.cursorLine];
  if (state.cursorCol < current.length) {
    const nextLines = [...state.lines];
    nextLines[state.cursorLine] = current.slice(0, state.cursorCol) + current.slice(state.cursorCol + 1);
    return {
      ...state,
      lines: nextLines,
      escPending: false,
    };
  }

  if (state.cursorLine >= state.lines.length - 1) {
    return { ...state, escPending: false };
  }

  const nextLines = [...state.lines];
  nextLines.splice(state.cursorLine, 2, current + nextLines[state.cursorLine + 1]);

  return {
    ...state,
    lines: nextLines,
    escPending: false,
  };
}

function flattenState(state: EditorState): string {
  return state.lines.join("\n");
}

class InputDecoder {
  private readonly decoder = new StringDecoder("utf8");
  private ordinaryBytes: number[] = [];
  private escTimer: NodeJS.Timeout | null = null;
  private escapeBuffer = "";
  private awaitingEscape = false;
  private skipNextLineFeed = false;
  private sawBracketedPaste = false;

  constructor(
    private readonly onEvent: (event: SemanticEvent) => void,
    private readonly onFallbackPaste: () => void,
  ) {}

  consume(chunk: Buffer): void {
    if (!this.sawBracketedPaste && chunk.length > 1 && chunk.includes(0x0a)) {
      this.onFallbackPaste();
    }

    for (const byte of chunk) {
      this.consumeByte(byte);
    }

    this.flushOrdinary();
  }

  finish(): void {
    this.clearEscapeTimer();
    this.flushOrdinary();
    const tail = this.decoder.end();
    if (tail.length > 0) {
      this.onEvent({ type: "text", text: tail });
    }
  }

  private consumeByte(byte: number): void {
    if (this.awaitingEscape) {
      this.consumeEscapedByte(byte);
      return;
    }

    switch (byte) {
      case 0x03:
        this.flushOrdinary();
        this.onEvent({ type: "ctrlC" });
        return;
      case 0x08:
      case 0x7f:
        this.flushOrdinary();
        this.onEvent({ type: "backspace" });
        return;
      case 0x0d:
        this.flushOrdinary();
        this.skipNextLineFeed = true;
        this.onEvent({ type: "enter" });
        return;
      case 0x0a:
        if (this.skipNextLineFeed) {
          this.skipNextLineFeed = false;
          return;
        }
        this.flushOrdinary();
        this.onEvent({ type: "enter" });
        return;
      case 0x1b:
        this.flushOrdinary();
        this.awaitingEscape = true;
        this.escapeBuffer = "";
        this.startEscapeTimer();
        return;
      default:
        this.skipNextLineFeed = false;
        this.ordinaryBytes.push(byte);
    }
  }

  private consumeEscapedByte(byte: number): void {
    this.clearEscapeTimer();
    this.escapeBuffer += String.fromCharCode(byte);
    const parsed = this.tryParseEscape(this.escapeBuffer);

    if (parsed === "pending") {
      this.startEscapeTimer();
      return;
    }

    const sequence = this.escapeBuffer;
    this.awaitingEscape = false;
    this.escapeBuffer = "";

    if (parsed) {
      if (parsed.type === "pasteStart" || parsed.type === "pasteEnd") {
        this.sawBracketedPaste = true;
      }
      this.onEvent(parsed);
      return;
    }

    this.onEvent({ type: "escape" });

    const remainder = Buffer.from(sequence, "binary");
    for (const nextByte of remainder) {
      this.consumeByte(nextByte);
    }
  }

  private tryParseEscape(sequence: string): SemanticEvent | "pending" | null {
    if (sequence === "[" || sequence === "O") {
      return "pending";
    }

    if (sequence.startsWith("[")) {
      switch (sequence) {
        case "[A":
          return { type: "up" };
        case "[B":
          return { type: "down" };
        case "[C":
          return { type: "right" };
        case "[D":
          return { type: "left" };
        case "[H":
          return { type: "home" };
        case "[F":
          return { type: "end" };
        case "[3~":
          return { type: "delete" };
        case "[200~":
          return { type: "pasteStart" };
        case "[201~":
          return { type: "pasteEnd" };
        default:
          if (/^\[[0-9;]*$/.test(sequence)) {
            return "pending";
          }
          return null;
      }
    }

    if (sequence.startsWith("O")) {
      switch (sequence) {
        case "OH":
          return { type: "home" };
        case "OF":
          return { type: "end" };
        default:
          return null;
      }
    }

    return null;
  }

  private startEscapeTimer(): void {
    this.escTimer = setTimeout(() => {
      this.awaitingEscape = false;
      this.escapeBuffer = "";
      this.escTimer = null;
      this.onEvent({ type: "escape" });
    }, 30);
  }

  private clearEscapeTimer(): void {
    if (this.escTimer) {
      clearTimeout(this.escTimer);
      this.escTimer = null;
    }
  }

  private flushOrdinary(): void {
    if (this.ordinaryBytes.length === 0) {
      return;
    }

    const text = this.decoder.write(Buffer.from(this.ordinaryBytes));
    this.ordinaryBytes = [];

    if (text.length > 0) {
      this.onEvent({ type: "text", text });
    }
  }
}

class RawMultilineInput {
  private readonly stdin = process.stdin;
  private readonly stdout = process.stdout;
  private readonly decoder: InputDecoder;
  private readonly promptWidth: number;
  private readonly indent: string;
  private readonly hintText = "ENTER = newline, ESC then ENTER = submit.";
  private state = createInitialState();
  private touched = false;
  private resolved = false;
  private renderMetrics: RenderMetrics = { rows: 0, cursorRow: 0 };
  private countdownInterval: NodeJS.Timeout | null = null;
  private remainingMs: number | null;
  private rawModeEnabled = false;
  private bracketedPasteEnabled = false;

  constructor(
    private readonly options: TextInputOptions,
    private readonly resolveResult: (value: PromptResult) => void,
  ) {
    this.remainingMs = options.timeoutMs ?? null;
    this.promptWidth = approximateWidth(options.prompt);
    this.indent = " ".repeat(this.promptWidth);
    this.decoder = new InputDecoder(
      (event) => this.handleEvent(event),
      () => {
        if (!this.state.isPasting) {
          this.state = { ...this.state, isPasting: true };
        }
      },
    );
  }

  async run(): Promise<void> {
    this.prepareTerminal();
    this.render();

    await new Promise<void>((resolve, reject) => {
      const onData = (chunk: Buffer) => this.decoder.consume(chunk);
      const onError = (error: Error) => {
        cleanup();
        reject(error);
      };

      const cleanup = () => {
        this.stdin.off("data", onData);
        this.stdin.off("error", onError);
      };

      const complete = (value: PromptResult) => {
        if (this.resolved) {
          return;
        }

        this.resolved = true;
        cleanup();
        this.resolveResult(value);
        resolve();
      };

      this.complete = complete;
      this.stdin.on("data", onData);
      this.stdin.on("error", onError);
    }).finally(() => {
      this.decoder.finish();
      this.restoreTerminal();
    });
  }

  private complete: (value: PromptResult) => void = () => undefined;

  private prepareTerminal(): void {
    if (!this.stdin.isTTY || !this.stdout.isTTY) {
      throw new Error("Interactive multiline input requires a TTY.");
    }

    this.stdin.resume();
    this.stdin.setRawMode(true);
    this.rawModeEnabled = true;
    this.enableBracketedPaste();

    if (this.remainingMs !== null) {
      this.countdownInterval = setInterval(() => {
        if (this.touched || this.remainingMs === null) {
          return;
        }

        this.remainingMs = Math.max(0, this.remainingMs - 1000);
        if (this.remainingMs === 0) {
          this.complete(null);
          return;
        }

        this.render();
      }, 1000);
    }
  }

  private restoreTerminal(): void {
    if (this.countdownInterval) {
      clearInterval(this.countdownInterval);
      this.countdownInterval = null;
    }

    this.disableBracketedPaste();

    if (this.rawModeEnabled) {
      this.stdin.setRawMode(false);
      this.rawModeEnabled = false;
    }

    this.clearRender();
    this.stdout.write("\n");
  }

  private enableBracketedPaste(): void {
    this.stdout.write("\x1b[?2004h");
    this.bracketedPasteEnabled = true;
  }

  private disableBracketedPaste(): void {
    if (!this.bracketedPasteEnabled) {
      return;
    }

    this.stdout.write("\x1b[?2004l");
    this.bracketedPasteEnabled = false;
  }

  private handleEvent(event: SemanticEvent): void {
    if (this.resolved) {
      return;
    }

    if (!this.touched && event.type !== "pasteStart" && event.type !== "pasteEnd") {
      this.touched = true;
      this.remainingMs = null;
      if (this.countdownInterval) {
        clearInterval(this.countdownInterval);
        this.countdownInterval = null;
      }
    }

    switch (event.type) {
      case "ctrlC":
        this.complete(null);
        return;
      case "pasteStart":
        this.state = { ...this.state, isPasting: true, escPending: false };
        this.render();
        return;
      case "pasteEnd":
        this.state = { ...this.state, isPasting: false };
        this.render();
        return;
      case "escape":
        this.state = { ...this.state, escPending: true };
        this.render();
        return;
      case "enter":
        if (this.state.escPending) {
          this.complete(flattenState(this.state));
          return;
        }
        this.state = insertNewline(this.state);
        break;
      case "backspace":
        this.state = backspace(this.state);
        break;
      case "delete":
        this.state = deleteForward(this.state);
        break;
      case "left":
        this.state = moveLeft(this.state);
        break;
      case "right":
        this.state = moveRight(this.state);
        break;
      case "up":
        this.state = moveUp(this.state);
        break;
      case "down":
        this.state = moveDown(this.state);
        break;
      case "home":
        this.state = moveHome(this.state);
        break;
      case "end":
        this.state = moveEnd(this.state);
        break;
      case "text":
        this.state = insertText(this.state, event.text);
        break;
      default:
        break;
    }

    this.render();
  }

  private clearRender(): void {
    if (this.renderMetrics.rows === 0) {
      return;
    }

    this.stdout.write("\r");
    if (this.renderMetrics.cursorRow > 0) {
      this.stdout.write(`\x1b[${this.renderMetrics.cursorRow}F`);
    }

    for (let row = 0; row < this.renderMetrics.rows; row += 1) {
      this.stdout.write("\x1b[2K");
      if (row < this.renderMetrics.rows - 1) {
        this.stdout.write("\x1b[1E");
      }
    }

    if (this.renderMetrics.rows > 1) {
      this.stdout.write(`\x1b[${this.renderMetrics.rows - 1}F`);
    } else {
      this.stdout.write("\r");
    }

    this.renderMetrics = { rows: 0, cursorRow: 0 };
  }

  private render(): void {
    const columns = Math.max(this.stdout.columns ?? 80, 1);
    const displayLines: Array<{ rendered: string; plain: string }> = [];

    if (this.remainingMs !== null && !this.touched) {
      const seconds = Math.ceil(this.remainingMs / 1000);
      const plain = `Continue in ${seconds}s... (type to intervene)`;
      displayLines.push({
        rendered: chalk.gray(`Continue in ${seconds}s... `) + chalk.dim("(type to intervene)"),
        plain,
      });
    }

    const hint = this.state.escPending
      ? "ESC armed. Press ENTER to submit, or keep editing to continue."
      : this.hintText;
    displayLines.push({
      rendered: chalk.dim(hint),
      plain: hint,
    });

    for (let index = 0; index < this.state.lines.length; index += 1) {
      const prefix = index === 0 ? this.options.prompt : this.indent;
      displayLines.push({
        rendered: index === 0 ? chalk.green(prefix) + this.state.lines[index] : prefix + this.state.lines[index],
        plain: prefix + this.state.lines[index],
      });
    }

    this.clearRender();

    const rowsPerLine = displayLines.map(({ plain }) =>
      Math.max(1, Math.ceil(Math.max(approximateWidth(plain), 1) / columns)),
    );
    const renderedOutput = displayLines.map((line) => line.rendered).join("\n");
    this.stdout.write(renderedOutput);

    const cursorDisplayLine = (this.remainingMs !== null && !this.touched ? 2 : 1) + this.state.cursorLine;
    const cursorPlain =
      (this.state.cursorLine === 0 ? this.options.prompt : this.indent) +
      this.state.lines[this.state.cursorLine].slice(0, this.state.cursorCol);
    const cursorRowOffset = Math.floor(approximateWidth(cursorPlain) / columns);
    const cursorCol = approximateWidth(cursorPlain) % columns;
    const cursorRow =
      rowsPerLine.slice(0, cursorDisplayLine).reduce((sum, rowCount) => sum + rowCount, 0) + cursorRowOffset;
    const totalRows = rowsPerLine.reduce((sum, rowCount) => sum + rowCount, 0);

    this.stdout.write("\r");
    if (totalRows > 1) {
      this.stdout.write(`\x1b[${totalRows - 1}F`);
    }
    if (cursorRow > 0) {
      this.stdout.write(`\x1b[${cursorRow}E`);
    }
    this.stdout.write(`\x1b[${cursorCol + 1}G`);

    this.renderMetrics = { rows: totalRows, cursorRow };
  }
}

export async function readTextInput(options: TextInputOptions): Promise<PromptResult> {
  let result: PromptResult = null;
  const session = new RawMultilineInput(options, (value) => {
    result = value;
  });
  await session.run();
  return result;
}

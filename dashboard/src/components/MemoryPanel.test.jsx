import { render, screen, cleanup } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import i18n from "../i18n/index.js";
import MemoryPanel from "./MemoryPanel.jsx";
afterEach(cleanup);
HTMLDialogElement.prototype.showModal ||= function showModal() { this.setAttribute("open", ""); };
HTMLDialogElement.prototype.close ||= function close() { this.removeAttribute("open"); };

const run = { id: "run-test", revision: 4, state: "paused", participants: [{ id: "seat-a", name: "Agent A" }] };

it("shows working memory only; questions are answered in the question dialog, not here", async () => {
  const api = { summaries: vi.fn().mockResolvedValue([]) };
  render(<MemoryPanel api={api} id="conversation-test" run={run} notify={vi.fn()} onChange={vi.fn()} />);
  expect(await screen.findByText(i18n.t("memory.empty"))).toBeInTheDocument();
  expect(api.summaries).toHaveBeenCalledWith("conversation-test", "run-test", 1);
  expect(screen.queryByRole("textbox")).not.toBeInTheDocument();
});

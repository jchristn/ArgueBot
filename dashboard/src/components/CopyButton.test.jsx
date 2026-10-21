import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import "../i18n/index.js";
import CopyButton from "./CopyButton.jsx";
import { copyText } from "../utils/clipboard.js";
vi.mock("../utils/clipboard.js", () => ({ copyText: vi.fn() }));
afterEach(() => vi.restoreAllMocks());
describe("copy feedback", () => {
  it("loads full content before copying, not a preview", async () => {
    copyText.mockResolvedValue(true);
    render(<CopyButton label="messages.copy" loadText={async () => "Full message 議論"} />);
    fireEvent.click(screen.getByRole("button", { name: "Copy turn" }));
    await waitFor(() => expect(copyText).toHaveBeenCalledWith("Full message 議論"));
    expect(await screen.findByRole("button", { name: "Copied" })).toBeEnabled();
  });
  it("offers the complete selectable text when both clipboard mechanisms fail", async () => {
    HTMLDialogElement.prototype.showModal = vi.fn(function () { this.setAttribute("open", ""); }); HTMLDialogElement.prototype.close = vi.fn();
    copyText.mockResolvedValue(false);
    render(<CopyButton value="Entire transcript" />);
    fireEvent.click(screen.getByRole("button", { name: "Copy" }));
    expect(await screen.findByRole("textbox", { name: "Text to copy" })).toHaveValue("Entire transcript");
    expect(screen.queryByRole("button", { name: "Copied" })).not.toBeInTheDocument();
  });
  it("shows a load error and never copies an incomplete transcript", async () => {
    copyText.mockClear();
    render(<CopyButton loadText={async () => { throw new Error("History changed; retry."); }} />);
    fireEvent.click(screen.getByRole("button", { name: "Copy" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("History changed; retry."); expect(copyText).not.toHaveBeenCalled();
  });
});

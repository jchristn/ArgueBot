import { render, screen, fireEvent } from "@testing-library/react";
import { describe, it, expect, vi } from "vitest";
import "../i18n/index.js";
import { Button, Pagination } from "./ui.jsx";
// Import resolves the canonical catalog before component rendering.
describe("shared controls", () => {
  it("icon-only actions keep accessible names and hover help without text", () => {
    render(<Button label="conversations.export" icon="download" iconOnly />);
    const button = screen.getByRole("button", { name: "Download transcript" });
    expect(button).toHaveAttribute(
      "title",
      "Download the complete selected conversation as a Markdown transcript",
    );
    expect(button.textContent.trim()).toBe("");
  });
  it("pagination follows server totals and sends an explicit next page", () => {
    const change = vi.fn();
    render(
      <Pagination
        page={{ items: [], totalCount: 60, pageNumber: 1, pageSize: 25 }}
        onChange={change}
      />,
    );
    expect(screen.getByRole("button", { name: "Previous" })).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Next" }));
    expect(change).toHaveBeenCalledWith({ pageNumber: 2 });
  });
});

import { beforeEach, describe, expect, it } from "vitest";
import { savePageSize, savedPageSize } from "./page-size.js";

describe("page size preference", () => {
  beforeEach(() => localStorage.clear());
  it("reuses a saved choice per table and falls back otherwise", () => {
    expect(savedPageSize("audit", 50)).toBe(50);
    savePageSize("audit", 100);
    expect(savedPageSize("audit", 50)).toBe(100);
    expect(savedPageSize("conversations", 25)).toBe(25);
  });
  it("ignores sizes the tables do not offer", () => {
    savePageSize("audit", 7);
    localStorage.setItem("arguebot.pageSize.messages", "9999");
    expect(savedPageSize("audit", 50)).toBe(50);
    expect(savedPageSize("messages", 25)).toBe(25);
  });
});

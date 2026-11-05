import { describe, expect, it } from "vitest";
import { detectPlatform } from "../components/CatalogTab.jsx";

describe("detectPlatform", () => {
  it("recognizes Windows, macOS and Linux browsers", () => {
    expect(detectPlatform({ userAgentData: { platform: "Windows" } })).toBe("windows");
    expect(detectPlatform({ platform: "Win32", userAgent: "Mozilla/5.0 (Windows NT 10.0; Win64; x64)" })).toBe("windows");
    expect(detectPlatform({ platform: "MacIntel", userAgent: "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)" })).toBe("mac");
    expect(detectPlatform({ platform: "Linux x86_64", userAgent: "Mozilla/5.0 (X11; Linux x86_64)" })).toBe("linux");
    expect(detectPlatform({})).toBe("linux");
    expect(detectPlatform({ userAgent: "Mozilla/5.0 (darwin) jsdom" })).toBe("mac");
  });
});

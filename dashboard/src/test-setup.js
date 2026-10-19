import "@testing-library/jest-dom/vitest";
import { vi } from "vitest";
if (!window.matchMedia)
  window.matchMedia = vi.fn(() => ({
    matches: false,
    addEventListener() {},
    removeEventListener() {},
  }));

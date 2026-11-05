import { describe, expect, it } from "vitest";
import { placeMenu } from "./ActionMenu.jsx";

const viewport = { width: 1280, height: 800, rtl: false };
const menu = { width: 180, height: 240 };

describe("placeMenu", () => {
  it("opens below the trigger, aligned to its end, when there is room", () => {
    const place = placeMenu({ top: 100, bottom: 132, left: 1200, right: 1232 }, menu, viewport);
    expect(place.top).toBe(136);
    expect(place.left).toBe(1232 - 180);
  });
  it("opens above the trigger near the bottom of the viewport", () => {
    const place = placeMenu({ top: 740, bottom: 772, left: 1200, right: 1232 }, menu, viewport);
    expect(place.top).toBe(740 - 240 - 4);
  });
  it("clamps inside the viewport horizontally and vertically", () => {
    const place = placeMenu({ top: 10, bottom: 42, left: 2, right: 34 }, { width: 400, height: 900 }, { width: 390, height: 700, rtl: false });
    expect(place.left).toBe(8);
    expect(place.top).toBeGreaterThanOrEqual(8);
    expect(place.maxHeight).toBe(684);
  });
  it("aligns to the trigger's start in right-to-left layouts", () => {
    const place = placeMenu({ top: 100, bottom: 132, left: 40, right: 72 }, menu, { ...viewport, rtl: true });
    expect(place.left).toBe(40);
  });
});

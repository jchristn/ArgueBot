import { test, expect } from "./fixtures.js";

// Measures text contrast of every visible control at rest and while hovered, in both themes.
function measure(selector) {
  const canvas = document.createElement("canvas").getContext("2d");
  const rgba = value => {
    canvas.clearRect(0, 0, 1, 1); canvas.fillStyle = "#000"; canvas.fillStyle = value; canvas.fillRect(0, 0, 1, 1);
    const [r, g, b, a] = canvas.getImageData(0, 0, 1, 1).data; return [r, g, b, a / 255];
  };
  const background = element => {
    const layers = [];
    for (let node = element; node; node = node.parentElement) {
      const color = rgba(getComputedStyle(node).backgroundColor);
      if (color[3] > 0) { layers.push(color); if (color[3] >= 1) break; }
    }
    let result = [255, 255, 255];
    for (const [r, g, b, a] of layers.reverse()) result = [r * a + result[0] * (1 - a), g * a + result[1] * (1 - a), b * a + result[2] * (1 - a)];
    return result;
  };
  const luminance = ([r, g, b]) => { const c = [r, g, b].map(v => { v /= 255; return v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4; }); return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2]; };
  const element = document.querySelector(selector);
  const text = rgba(getComputedStyle(element).color);
  const back = background(element);
  const fore = [text[0] * text[3] + back[0] * (1 - text[3]), text[1] * text[3] + back[1] * (1 - text[3]), text[2] * text[3] + back[2] * (1 - text[3])];
  const [light, dark] = [luminance(fore), luminance(back)].sort((a, b) => b - a);
  return (light + 0.05) / (dark + 0.05);
}

for (const theme of ["light", "dark"]) {
  test(`buttons, links and tabs stay readable on hover in ${theme} mode`, async ({ page }) => {
    test.setTimeout(120000);
    await page.addInitScript(theme => localStorage.setItem("arguebot.theme", theme), theme);
    await page.setViewportSize({ width: 1280, height: 900 });
    const failures = [];
    for (const route of ["home", "conversations", "participants", "participants?tab=credentials", "participants?tab=endpoints", "history", "explorer", "settings", "account", "users", "tenants", "audit"]) {
      await page.goto("/dashboard/" + route);
      await expect(page.locator("main")).toBeVisible();
      await page.waitForTimeout(300);
      const count = await page.evaluate(() => {
        let index = 0;
        for (const element of document.querySelectorAll("button, a[href], [role=tab]")) {
          const box = element.getBoundingClientRect();
          if (!element.textContent.trim() || element.disabled || box.width === 0 || box.bottom < 0 || box.top > innerHeight) continue;
          element.dataset.hoverCheck = String(index++);
        }
        return index;
      });
      for (let index = 0; index < Math.min(count, 40); index++) {
        const selector = `[data-hover-check="${index}"]`;
        const label = (await page.locator(selector).textContent()).trim().slice(0, 40);
        const rest = await page.evaluate(measure, selector);
        await page.locator(selector).hover();
        await page.waitForTimeout(60);
        const hovered = await page.evaluate(measure, selector);
        if (rest < 4.5 || hovered < 4.5) failures.push(`${route} "${label}": rest ${rest.toFixed(2)}, hover ${hovered.toFixed(2)}`);
      }
      await page.mouse.move(0, 0);
    }
    expect(failures, failures.join("\n")).toEqual([]);
  });
}

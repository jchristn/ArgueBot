import { expect, it } from "vitest";
import { returnPath } from "./Login.jsx";
import { navigationGroups, displayName } from "../App.jsx";
import { expiryFor } from "../components/AuthCredentials.jsx";

it("returns to dashboard pages only", () => {
  expect(returnPath({ state: { from: "/dashboard/conversations/cnv_1" }, search: "" })).toBe("/dashboard/conversations/cnv_1");
  expect(returnPath({ state: null, search: "?return=https://evil.example.test" })).toBe("/dashboard/home");
  expect(returnPath({ state: { from: "/dashboard/login" }, search: "" })).toBe("/dashboard/home");
});
it("shows administration only to administrators", () => {
  const links = (user) => navigationGroups(user).flatMap((group) => group.links.map(([path]) => path));
  expect(links({ isAdmin: false, isTenantAdmin: false })).not.toEqual(expect.arrayContaining(["users"]));
  expect(links({ isAdmin: false, isTenantAdmin: false })).not.toContain("settings");
  expect(links({ isAdmin: false, isTenantAdmin: false })).not.toContain("history");
  expect(links({ isAdmin: false, isTenantAdmin: true })).toEqual(expect.arrayContaining(["users", "audit", "history"]));
  expect(links({ isAdmin: false, isTenantAdmin: true })).not.toContain("tenants");
  expect(links({ isAdmin: true, isTenantAdmin: true })).toEqual(expect.arrayContaining(["users", "audit", "tenants", "settings"]));
  expect(displayName({ firstName: "Default", lastName: "Admin", email: "admin@arguebot" })).toBe("Default Admin");
  expect(displayName({ firstName: "", lastName: "", email: "member@example.test" })).toBe("member@example.test");
});
it("computes credential expiry", () => {
  expect(expiryFor(0)).toBeNull();
  expect(expiryFor(30, Date.parse("2026-01-01T00:00:00Z"))).toBe("2026-01-31T00:00:00.000Z");
});

import { describe, expect, it } from "vitest";

import { readStoredTheme, storeTheme, THEME_STORAGE_KEY } from "./theme";

describe("theme storage", () => {
  it("defaults to system", () => {
    expect(readStoredTheme()).toBe("system");
  });

  it("round-trips a saved theme", () => {
    storeTheme("dark");
    expect(readStoredTheme()).toBe("dark");
  });

  it("ignores garbage", () => {
    localStorage.setItem(THEME_STORAGE_KEY, "purple");
    expect(readStoredTheme()).toBe("system");
  });
});

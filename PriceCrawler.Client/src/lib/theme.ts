import { createContext } from "react";

/** Theme the user picked; `system` follows the OS setting. */
export type Theme = "light" | "dark" | "system";

/** Value of the theme context. */
export interface ThemeContextValue {
  /** Picked theme. */
  theme: Theme;
  /** Theme actually applied after resolving `system`. */
  resolvedTheme: "light" | "dark";
  /** Changes and remembers the theme. */
  setTheme: (theme: Theme) => void;
}

/** localStorage key; `index.html` reads it before the first paint. */
export const THEME_STORAGE_KEY = "pricecrawler.theme";

/** Theme context; `null` outside ThemeProvider. */
export const ThemeContext = createContext<ThemeContextValue | null>(null);

/**
 * Reads the saved theme.
 * @returns Saved theme, or `system` when nothing (or garbage) is stored or storage is unavailable.
 */
export function readStoredTheme(): Theme {
  try {
    const value = localStorage.getItem(THEME_STORAGE_KEY);
    return value === "light" || value === "dark" || value === "system" ? value : "system";
  } catch {
    return "system";
  }
}

/**
 * Saves the theme; storage errors (private mode) are ignored.
 * @param theme Theme to save.
 */
export function storeTheme(theme: Theme): void {
  try {
    localStorage.setItem(THEME_STORAGE_KEY, theme);
  } catch {
    // The theme then lasts only for this page view.
  }
}

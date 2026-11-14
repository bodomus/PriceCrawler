import { useContext } from "react";

import { ThemeContext, type ThemeContextValue } from "@/lib/theme";

/**
 * Returns the current theme and its setter.
 * @returns Theme context value.
 * @throws Error when used outside ThemeProvider.
 */
export function useTheme(): ThemeContextValue {
  const context = useContext(ThemeContext);
  if (context === null) {
    throw new Error("useTheme must be used inside ThemeProvider.");
  }
  return context;
}

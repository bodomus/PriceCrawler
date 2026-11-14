import { useEffect, useMemo, useState, type ReactNode } from "react";

import { useMediaQuery } from "@/hooks/use-media-query";
import { readStoredTheme, storeTheme, ThemeContext, type Theme, type ThemeContextValue } from "@/lib/theme";

/**
 * Keeps the light/dark theme: applies the `dark` class to `<html>` and remembers the choice.
 * @param props.children App content.
 * @returns The context provider.
 */
export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setThemeState] = useState<Theme>(readStoredTheme);
  const systemDark = useMediaQuery("(prefers-color-scheme: dark)");
  const resolvedTheme = theme === "system" ? (systemDark ? "dark" : "light") : theme;

  useEffect(() => {
    document.documentElement.classList.toggle("dark", resolvedTheme === "dark");
    document.documentElement.style.colorScheme = resolvedTheme;
  }, [resolvedTheme]);

  const value = useMemo<ThemeContextValue>(
    () => ({
      theme,
      resolvedTheme,
      setTheme: (next) => {
        storeTheme(next);
        setThemeState(next);
      },
    }),
    [theme, resolvedTheme],
  );

  return <ThemeContext value={value}>{children}</ThemeContext>;
}

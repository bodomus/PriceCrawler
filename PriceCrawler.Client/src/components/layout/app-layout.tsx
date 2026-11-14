import { MenuIcon, PanelLeftIcon, XIcon } from "lucide-react";
import { useState } from "react";
import { Outlet, useLocation } from "react-router";

import { Button } from "@/components/ui/button";
import { Separator } from "@/components/ui/separator";
import { useMediaQuery } from "@/hooks/use-media-query";
import { t } from "@/i18n";
import { cn } from "@/lib/utils";

import { ApiStatus } from "./api-status";
import { AppSidebar } from "./app-sidebar";
import { KyivClock } from "./kyiv-clock";
import { navItems } from "./nav-items";
import { ThemeToggle } from "./theme-toggle";

const COLLAPSED_STORAGE_KEY = "pricecrawler.sidebar-collapsed";

/**
 * Reads whether the desktop sidebar was left collapsed.
 * @returns Saved state, `false` when unknown.
 */
function readCollapsed(): boolean {
  try {
    return localStorage.getItem(COLLAPSED_STORAGE_KEY) === "1";
  } catch {
    return false;
  }
}

/**
 * Page frame: side menu, fixed header and a content panel that scrolls on its own,
 * so the page itself never grows past the window. On narrow screens the menu is a drawer.
 * @returns The layout with the current route inside.
 */
export function AppLayout() {
  const isDesktop = useMediaQuery("(min-width: 768px)");
  const [collapsed, setCollapsed] = useState(readCollapsed);
  const [mobileOpen, setMobileOpen] = useState(false);
  const { pathname } = useLocation();
  const current = navItems.find((item) => pathname.startsWith(item.path));

  /** Collapses or expands the desktop sidebar and remembers the state. */
  const toggleCollapsed = () => {
    const next = !collapsed;
    setCollapsed(next);
    try {
      localStorage.setItem(COLLAPSED_STORAGE_KEY, next ? "1" : "0");
    } catch {
      // Not remembered in private mode.
    }
  };

  return (
    <div className="flex h-svh overflow-hidden">
      {isDesktop ? (
        <aside
          className={cn(
            "shrink-0 border-r border-sidebar-border transition-[width] duration-200",
            collapsed ? "w-14" : "w-60",
          )}
        >
          <AppSidebar collapsed={collapsed} />
        </aside>
      ) : (
        mobileOpen && (
          <div className="fixed inset-0 z-40">
            <div
              className="absolute inset-0 bg-black/50"
              aria-hidden="true"
              onClick={() => {
                setMobileOpen(false);
              }}
            />
            <aside className="absolute inset-y-0 left-0 w-64 border-r border-sidebar-border shadow-lg">
              <Button
                variant="ghost"
                size="icon-sm"
                className="absolute top-3 right-2 z-10"
                aria-label={t.header.closeMenu}
                onClick={() => {
                  setMobileOpen(false);
                }}
              >
                <XIcon />
              </Button>
              <AppSidebar
                collapsed={false}
                onNavigate={() => {
                  setMobileOpen(false);
                }}
              />
            </aside>
          </div>
        )
      )}

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-14 shrink-0 items-center gap-2 border-b bg-background px-4">
          <Button
            variant="ghost"
            size="icon-sm"
            className="-ml-1"
            aria-label={isDesktop ? t.header.toggleSidebar : t.header.openMenu}
            onClick={
              isDesktop
                ? toggleCollapsed
                : () => {
                    setMobileOpen(true);
                  }
            }
          >
            {isDesktop ? <PanelLeftIcon /> : <MenuIcon />}
          </Button>
          <Separator orientation="vertical" className="mx-1 h-4!" />
          <h1 className="truncate text-sm font-medium">{current?.title ?? t.app.name}</h1>
          <div className="ml-auto flex items-center gap-1">
            <KyivClock />
            <ApiStatus />
            <ThemeToggle />
          </div>
        </header>

        <main className="min-h-0 flex-1 overflow-y-auto p-4 md:p-6">
          <Outlet />
        </main>
      </div>
    </div>
  );
}

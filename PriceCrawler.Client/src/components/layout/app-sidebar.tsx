import { TrendingUpIcon } from "lucide-react";
import { NavLink } from "react-router";

import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { t } from "@/i18n";
import { cn } from "@/lib/utils";

import { navItems } from "./nav-items";

/** Props of {@link AppSidebar}. */
export interface AppSidebarProps {
  /** Icons only (desktop "collapsed" state). */
  collapsed: boolean;
  /** Called after a menu link is chosen (closes the mobile drawer). */
  onNavigate?: () => void;
}

/**
 * Side menu with the app logo and the main sections.
 * @param props Collapsed state and navigation callback.
 * @returns The sidebar element.
 */
export function AppSidebar({ collapsed, onNavigate }: AppSidebarProps) {
  return (
    <div className="flex h-full flex-col bg-sidebar text-sidebar-foreground">
      <div className={cn("flex h-14 shrink-0 items-center gap-2 px-4", collapsed && "justify-center px-0")}>
        <div className="flex size-8 shrink-0 items-center justify-center rounded-lg bg-sidebar-primary text-sidebar-primary-foreground">
          <TrendingUpIcon className="size-4" />
        </div>
        {!collapsed && (
          <div className="grid leading-tight">
            <span className="truncate text-sm font-semibold">{t.app.name}</span>
            <span className="truncate text-xs text-muted-foreground">{t.app.subtitle}</span>
          </div>
        )}
      </div>

      <nav aria-label={t.nav.sectionMain} className="flex-1 overflow-y-auto px-2 py-2">
        {!collapsed && <div className="px-2 pb-1 text-xs font-medium text-muted-foreground">{t.nav.sectionMain}</div>}
        <ul className="grid gap-1">
          {navItems.map(({ path, title, icon: Icon }) => {
            const link = (
              <NavLink
                to={path}
                onClick={onNavigate}
                aria-label={collapsed ? title : undefined}
                className={({ isActive }) =>
                  cn(
                    "flex h-8 items-center gap-2 rounded-md px-2 text-sm outline-none hover:bg-sidebar-accent hover:text-sidebar-accent-foreground focus-visible:ring-2 focus-visible:ring-sidebar-ring",
                    collapsed && "justify-center px-0",
                    isActive && "bg-sidebar-accent font-medium text-sidebar-accent-foreground",
                  )
                }
              >
                <Icon className="size-4 shrink-0" />
                {!collapsed && <span className="truncate">{title}</span>}
              </NavLink>
            );
            return (
              <li key={path}>
                {collapsed ? (
                  <Tooltip>
                    <TooltipTrigger asChild>{link}</TooltipTrigger>
                    <TooltipContent side="right">{title}</TooltipContent>
                  </Tooltip>
                ) : (
                  link
                )}
              </li>
            );
          })}
        </ul>
      </nav>
    </div>
  );
}

import { ChartLineIcon, PackageIcon, UsersIcon, WrenchIcon, type LucideIcon } from "lucide-react";

import { t } from "@/i18n";

/** One entry of the side menu. */
export interface NavItem {
  /** Route path. */
  path: string;
  /** Menu text. */
  title: string;
  /** Menu icon. */
  icon: LucideIcon;
}

/** Side menu entries in display order. */
export const navItems: readonly NavItem[] = [
  { path: "/products", title: t.nav.products, icon: PackageIcon },
  { path: "/analytics", title: t.nav.analytics, icon: ChartLineIcon },
  { path: "/development", title: t.nav.development, icon: WrenchIcon },
  { path: "/users", title: t.nav.users, icon: UsersIcon },
];

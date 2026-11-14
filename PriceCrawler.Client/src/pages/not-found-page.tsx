import { Link } from "react-router";

import { Button } from "@/components/ui/button";
import { t } from "@/i18n";

/**
 * Shown for unknown addresses.
 * @returns The page.
 */
export function NotFoundPage() {
  return (
    <div className="flex h-full flex-col items-center justify-center gap-4 text-center">
      <p className="text-5xl font-semibold text-muted-foreground">404</p>
      <h2 className="text-lg font-medium">{t.notFound.title}</h2>
      <Button asChild variant="outline">
        <Link to="/products">{t.notFound.backHome}</Link>
      </Button>
    </div>
  );
}

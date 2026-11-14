import { useQuery } from "@tanstack/react-query";
import { FolderIcon } from "lucide-react";

import { categoryChildrenQuery } from "@/api/queries";
import { PageHeader } from "@/components/page-header";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { t } from "@/i18n";
import { DEFAULT_STORE } from "@/lib/config";

/**
 * Products section. For now it lists the store's top-level categories (proves the typed API
 * client end to end); the category tree and the product table come in MPC-102.
 * @returns The page.
 */
export function ProductsPage() {
  const { data, isPending, isError, error, refetch } = useQuery(categoryChildrenQuery(DEFAULT_STORE));

  return (
    <>
      <PageHeader title={t.products.title} description={t.products.description} />
      <h3 className="mb-3 text-sm font-medium text-muted-foreground">{t.products.categoriesTitle}</h3>

      {isPending ? (
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
          {Array.from({ length: 6 }, (_, i) => (
            <Skeleton key={i} className="h-24" />
          ))}
        </div>
      ) : isError ? (
        <Card role="alert" className="border-destructive/50">
          <CardHeader>
            <CardTitle>{t.common.loadError}</CardTitle>
            <CardDescription>{error.message}</CardDescription>
          </CardHeader>
          <CardContent>
            <Button variant="outline" size="sm" onClick={() => void refetch()}>
              {t.common.retry}
            </Button>
          </CardContent>
        </Card>
      ) : data.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t.products.empty}</p>
      ) : (
        <ul className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
          {data.map((category) => (
            <li key={category.id}>
              <Card className="h-full gap-2 py-4">
                <CardHeader className="flex items-center gap-2 px-4">
                  <FolderIcon className="size-4 shrink-0 text-muted-foreground" />
                  <CardTitle className="truncate text-sm">{category.name}</CardTitle>
                </CardHeader>
                <CardContent className="px-4 text-sm text-muted-foreground tabular-nums">
                  {t.products.productCount(category.productCount)}
                </CardContent>
              </Card>
            </li>
          ))}
        </ul>
      )}
    </>
  );
}

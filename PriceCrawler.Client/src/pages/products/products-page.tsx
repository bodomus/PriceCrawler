import { useQuery, useQueryClient } from "@tanstack/react-query";
import { SearchIcon } from "lucide-react";
import { useEffect, useState } from "react";

import type { CategoryNode } from "@/api/client";
import { productsQuery, type ProductGroupBy } from "@/api/queries";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { useDebouncedValue } from "@/hooks/use-debounced-value";
import { t } from "@/i18n";
import { DEFAULT_STORE } from "@/lib/config";

import { CategoryTree } from "./category-tree";
import { useProductListState } from "./list-state";
import { PaginationBar } from "./pagination-bar";
import { ProductsTable } from "./products-table";

/**
 * Finds a category's name among the tree levels already in the query cache.
 * @param queryClient Query client.
 * @param id Category id.
 * @returns The name, or `undefined` when that level was never loaded (e.g. after a reload).
 */
function findCachedCategoryName(queryClient: ReturnType<typeof useQueryClient>, id: number): string | undefined {
  for (const [, nodes] of queryClient.getQueriesData<CategoryNode[]>({ queryKey: ["categories", DEFAULT_STORE] })) {
    const node = nodes?.find((category) => category.id === id);
    if (node !== undefined) return node.name;
  }
  return undefined;
}

/**
 * Main screen: category tree on the left, product table on the right with search, grouping and a pager.
 * Every parameter lives in the address, so links and reloads keep the view.
 * @returns The page.
 */
export function ProductsPage() {
  const queryClient = useQueryClient();
  const [list, update] = useProductListState();
  const [searchText, setSearchText] = useState(list.search);
  const debouncedSearch = useDebouncedValue(searchText);

  useEffect(() => {
    if (debouncedSearch !== list.search) update({ search: debouncedSearch });
    // Only the debounced text drives the address; list.search changes come from it.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [debouncedSearch]);

  const products = useQuery(productsQuery(DEFAULT_STORE, list));
  const categoryName =
    list.categoryId === null
      ? t.products.allProducts
      : (findCachedCategoryName(queryClient, list.categoryId) ?? t.products.unknownCategory);

  return (
    <div className="flex h-full min-h-0 flex-col gap-4 lg:flex-row">
      <aside
        aria-label={t.products.categories}
        className="flex h-64 shrink-0 flex-col rounded-xl border bg-card lg:h-full lg:w-80"
      >
        <h2 className="border-b px-4 py-2.5 text-sm font-medium">{t.products.categories}</h2>
        <div className="min-h-0 flex-1">
          <CategoryTree
            store={DEFAULT_STORE}
            selectedId={list.categoryId}
            onSelect={(category) => {
              update({ categoryId: category?.id ?? null });
            }}
          />
        </div>
      </aside>

      <section className="flex min-h-0 min-w-0 flex-1 flex-col rounded-xl border bg-card">
        <div className="flex flex-wrap items-center gap-2 border-b px-3 py-2.5">
          <h2 className="mr-auto truncate text-sm font-medium" data-testid="list-title">
            {categoryName}
            {products.data !== undefined && (
              <span className="ml-2 font-normal text-muted-foreground">
                {t.products.productCount(products.data.totalCount)}
              </span>
            )}
          </h2>
          <div className="relative w-full sm:w-64">
            <SearchIcon className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground" />
            <Input
              type="search"
              value={searchText}
              placeholder={t.products.searchPlaceholder}
              aria-label={t.products.searchPlaceholder}
              className="h-8 pl-8"
              onChange={(event) => {
                setSearchText(event.target.value);
              }}
            />
          </div>
          <Select
            value={list.groupBy ?? "none"}
            onValueChange={(value) => {
              update({ groupBy: value === "none" ? null : (value as ProductGroupBy) });
            }}
          >
            <SelectTrigger size="sm" aria-label={t.products.groupBy}>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {(Object.keys(t.products.groupByOptions) as (keyof typeof t.products.groupByOptions)[]).map((key) => (
                <SelectItem key={key} value={key}>
                  {t.products.groupByOptions[key]}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        <div className="min-h-0 flex-1 overflow-auto">
          {products.isPending ? (
            <div className="grid gap-2 p-3">
              {Array.from({ length: 10 }, (_, i) => (
                <Skeleton key={i} className="h-10" />
              ))}
            </div>
          ) : products.isError ? (
            <div role="alert" className="grid justify-items-start gap-2 p-4 text-sm">
              <p className="font-medium">{t.common.loadError}</p>
              <p className="text-muted-foreground">{products.error.message}</p>
              <Button variant="outline" size="sm" onClick={() => void products.refetch()}>
                {t.common.retry}
              </Button>
            </div>
          ) : products.data.items.length === 0 ? (
            <p className="p-4 text-sm text-muted-foreground">{t.products.empty}</p>
          ) : (
            <ProductsTable
              items={products.data.items}
              groups={products.data.groups}
              sort={list.sort}
              isFetching={products.isPlaceholderData}
              onSortChange={(sort) => {
                update({ sort });
              }}
            />
          )}
        </div>

        {products.data !== undefined && (
          <PaginationBar
            page={list.page}
            pageSize={list.pageSize}
            totalCount={products.data.totalCount}
            onPageChange={(page) => {
              update({ page });
            }}
            onPageSizeChange={(pageSize) => {
              update({ pageSize });
            }}
          />
        )}
      </section>
    </div>
  );
}

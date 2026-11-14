import { createColumnHelper, flexRender, rowSortingFeature, tableFeatures, useTable } from "@tanstack/react-table";
import { ArrowDownIcon, ArrowUpDownIcon, ArrowUpIcon, ExternalLinkIcon } from "lucide-react";
import { Fragment, useMemo } from "react";

import type { ProductGroup, ProductItem } from "@/api/client";
import { Badge } from "@/components/ui/badge";
import { t } from "@/i18n";
import { formatPack, formatPrice } from "@/lib/format";
import { formatKyivDateTime } from "@/lib/kyiv-time";
import { cn } from "@/lib/utils";

import { PriceChange } from "./price-change";
import { groupName, parseSort, formatSort } from "./table-helpers";
import { Sparkline } from "./sparkline";

// Only sorting is a table feature here; paging and grouping come from the server.
const features = tableFeatures({ rowSortingFeature });
const column = createColumnHelper<typeof features, ProductItem>();

/** Columns whose header and cells are right-aligned (numbers and dates). */
const RIGHT_ALIGNED = new Set(["price", "priceChange", "capturedAt"]);

// Column ids match the API sort fields, so sorting state maps 1:1 to `sort=`.
const columns = column.columns([
  column.accessor("name", {
    header: t.products.columns.name,
    cell: ({ row: { original: item } }) => (
      <div className="flex min-w-0 items-center gap-2">
        <div className="min-w-0">
          <div className="truncate font-medium" title={item.name}>
            {item.name}
          </div>
          <div className="flex items-center gap-2 text-xs text-muted-foreground">
            {formatPack(item.packValue, item.packUnit)}
            {!item.inStock && <span className="text-destructive">{t.products.outOfStock}</span>}
          </div>
        </div>
        {item.isPromo && <Badge className="shrink-0 bg-chart-1 text-white">{t.products.promo}</Badge>}
        <a
          href={item.url}
          target="_blank"
          rel="noreferrer"
          className="ml-auto shrink-0 text-muted-foreground hover:text-foreground"
          aria-label={t.products.openOnSite}
          title={t.products.openOnSite}
        >
          <ExternalLinkIcon className="size-3.5" />
        </a>
      </div>
    ),
  }),
  column.accessor("price", {
    header: t.products.columns.price,
    cell: ({ row: { original: item } }) =>
      item.price === null ? (
        <span className="text-muted-foreground">{t.products.noPrice}</span>
      ) : (
        <div className="tabular-nums">
          <div className="font-medium">{formatPrice(item.price)}</div>
          {item.isPromo && item.oldPrice !== null && (
            <div className="text-xs text-muted-foreground line-through">{formatPrice(item.oldPrice)}</div>
          )}
        </div>
      ),
  }),
  column.accessor("priceChange", {
    header: t.products.columns.change,
    cell: ({ row: { original: item } }) => <PriceChange change={item.priceChange} percent={item.priceChangePercent} />,
  }),
  column.display({
    id: "trend",
    header: t.products.columns.trend,
    cell: ({ row: { original: item } }) => <Sparkline values={item.trend} label={t.products.columns.trend} />,
  }),
  column.accessor("capturedAt", {
    header: t.products.columns.capturedAt,
    cell: ({ getValue }) => {
      const value = getValue();
      return (
        <span className="text-xs whitespace-nowrap text-muted-foreground tabular-nums">
          {value === null ? "—" : formatKyivDateTime(value)}
        </span>
      );
    },
  }),
]);

/** Props of {@link ProductsTable}. */
export interface ProductsTableProps {
  /** Rows of the current page. */
  items: readonly ProductItem[];
  /** Groups of a grouped list (empty when not grouped). */
  groups: readonly ProductGroup[];
  /** API sort expression. */
  sort: string;
  /** Called with the new sort expression when a header is clicked. */
  onSortChange: (sort: string) => void;
  /** Dims the rows while the next page loads. */
  isFetching: boolean;
}

/**
 * Product table (TanStack Table) with server-side sorting: the table only renders the page the API returned.
 * When grouped, a header row with the group name and size starts each group.
 * @param props Rows, groups, sort and callbacks.
 * @returns The table.
 */
export function ProductsTable({ items, groups, sort, onSortChange, isFetching }: ProductsTableProps) {
  const sorting = useMemo(() => parseSort(sort), [sort]);
  const groupByKey = useMemo(() => new Map(groups.map((group) => [group.key, group])), [groups]);

  const table = useTable({
    features,
    data: items,
    columns,
    getRowId: (item) => String(item.productId),
    manualSorting: true,
    enableMultiSort: true,
    isMultiSortEvent: (event) => (event as MouseEvent).shiftKey,
    sortDescFirst: false,
    state: { sorting },
    onSortingChange: (updater) => {
      const next = typeof updater === "function" ? updater(sorting) : updater;
      onSortChange(formatSort(next));
    },
  });

  return (
    <table className={cn("w-full caption-bottom text-sm transition-opacity", isFetching && "opacity-60")}>
      <thead className="sticky top-0 z-10 bg-background shadow-[inset_0_-1px_0_var(--color-border)]">
        {table.getHeaderGroups().map((headerGroup) => (
          <tr key={headerGroup.id}>
            {headerGroup.headers.map((header) => {
              const right = RIGHT_ALIGNED.has(header.column.id);
              const sorted = header.column.getIsSorted();
              const SortIcon = sorted === "asc" ? ArrowUpIcon : sorted === "desc" ? ArrowDownIcon : ArrowUpDownIcon;
              return (
                <th
                  key={header.id}
                  scope="col"
                  aria-sort={sorted === "asc" ? "ascending" : sorted === "desc" ? "descending" : undefined}
                  className={cn(
                    "h-10 px-3 font-medium whitespace-nowrap text-muted-foreground",
                    right ? "text-right" : "text-left",
                    header.id === "name" && "w-full min-w-64",
                  )}
                >
                  {header.column.getCanSort() ? (
                    <button
                      type="button"
                      className={cn(
                        "inline-flex items-center gap-1 hover:text-foreground",
                        sorted !== false && "text-foreground",
                      )}
                      onClick={header.column.getToggleSortingHandler()}
                    >
                      {flexRender(header.column.columnDef.header, header.getContext())}
                      <SortIcon className={cn("size-3.5", sorted === false && "opacity-40")} />
                    </button>
                  ) : (
                    flexRender(header.column.columnDef.header, header.getContext())
                  )}
                </th>
              );
            })}
          </tr>
        ))}
      </thead>
      <tbody>
        {table.getRowModel().rows.map((row, index, rows) => {
          const key = row.original.groupKey;
          const previousKey = index === 0 ? undefined : rows[index - 1]?.original.groupKey;
          const group = key === null ? undefined : groupByKey.get(key);
          return (
            <Fragment key={row.id}>
              {group !== undefined && key !== previousKey && (
                <tr className="bg-muted/60" data-testid="group-row">
                  <th colSpan={columns.length} scope="rowgroup" className="px-3 py-1.5 text-left text-xs font-medium">
                    {groupName(group)}
                    <span className="ml-2 font-normal text-muted-foreground">
                      {t.products.productCount(group.count)}
                    </span>
                  </th>
                </tr>
              )}
              <tr className="border-b hover:bg-muted/40">
                {row.getAllCells().map((cell) => {
                  return (
                    <td
                      key={cell.id}
                      className={cn(
                        "px-3 py-2 align-middle",
                        RIGHT_ALIGNED.has(cell.column.id) && "text-right",
                        cell.column.id === "name" && "max-w-0",
                      )}
                    >
                      {flexRender(cell.column.columnDef.cell, cell.getContext())}
                    </td>
                  );
                })}
              </tr>
            </Fragment>
          );
        })}
      </tbody>
    </table>
  );
}

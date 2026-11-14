import { ChevronLeftIcon, ChevronRightIcon, ChevronsLeftIcon, ChevronsRightIcon } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { t } from "@/i18n";

import { PAGE_SIZES } from "./list-state";

/** Props of {@link PaginationBar}. */
export interface PaginationBarProps {
  /** Current 1-based page. */
  page: number;
  /** Rows per page. */
  pageSize: number;
  /** Rows on all pages. */
  totalCount: number;
  /** Called with the page to show. */
  onPageChange: (page: number) => void;
  /** Called with a new page size. */
  onPageSizeChange: (pageSize: number) => void;
}

/**
 * Pager under the table: row range, page size and first/previous/next/last buttons.
 * @param props Paging state and callbacks.
 * @returns The bar.
 */
export function PaginationBar({ page, pageSize, totalCount, onPageChange, onPageSizeChange }: PaginationBarProps) {
  const pages = Math.max(1, Math.ceil(totalCount / pageSize));
  const from = totalCount === 0 ? 0 : (page - 1) * pageSize + 1;
  const to = Math.min(totalCount, page * pageSize);
  return (
    <div className="flex flex-wrap items-center justify-between gap-2 border-t px-3 py-2 text-sm">
      <span className="text-muted-foreground tabular-nums">{t.pagination.range(from, to, totalCount)}</span>
      <div className="flex items-center gap-2">
        <span className="hidden text-muted-foreground sm:inline">{t.pagination.pageSize}</span>
        <Select
          value={String(pageSize)}
          onValueChange={(value) => {
            onPageSizeChange(Number(value));
          }}
        >
          <SelectTrigger size="sm" aria-label={t.pagination.pageSize}>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {PAGE_SIZES.map((size) => (
              <SelectItem key={size} value={String(size)}>
                {size}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <span className="px-1 whitespace-nowrap tabular-nums">{t.pagination.page(page, pages)}</span>
        <Button
          variant="outline"
          size="icon-sm"
          aria-label={t.pagination.first}
          disabled={page <= 1}
          onClick={() => {
            onPageChange(1);
          }}
        >
          <ChevronsLeftIcon />
        </Button>
        <Button
          variant="outline"
          size="icon-sm"
          aria-label={t.pagination.previous}
          disabled={page <= 1}
          onClick={() => {
            onPageChange(page - 1);
          }}
        >
          <ChevronLeftIcon />
        </Button>
        <Button
          variant="outline"
          size="icon-sm"
          aria-label={t.pagination.next}
          disabled={page >= pages}
          onClick={() => {
            onPageChange(page + 1);
          }}
        >
          <ChevronRightIcon />
        </Button>
        <Button
          variant="outline"
          size="icon-sm"
          aria-label={t.pagination.last}
          disabled={page >= pages}
          onClick={() => {
            onPageChange(pages);
          }}
        >
          <ChevronsRightIcon />
        </Button>
      </div>
    </div>
  );
}

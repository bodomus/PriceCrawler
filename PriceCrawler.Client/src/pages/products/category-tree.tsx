import { useQuery, useQueryClient } from "@tanstack/react-query";
import { ChevronRightIcon, FolderIcon, FolderOpenIcon, LayersIcon, LoaderIcon } from "lucide-react";
import { useCallback, useMemo, useRef, useState } from "react";
import { Tree, type NodeRendererProps } from "react-arborist";

import type { CategoryNode } from "@/api/client";
import { categoryChildrenQuery } from "@/api/queries";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { useElementSize } from "@/hooks/use-element-size";
import { t } from "@/i18n";
import { cn } from "@/lib/utils";

import { buildTreeRows, type TreeRow } from "./tree-rows";

/** Props of {@link CategoryTree}. */
export interface CategoryTreeProps {
  /** Store key. */
  store: string;
  /** Selected category id, `null` for all products. */
  selectedId: number | null;
  /** Called with the chosen category, or `null` for "all products". */
  onSelect: (category: CategoryNode | null) => void;
}

/**
 * Store category tree (react-arborist). Only the top level loads at first; a node's children are fetched
 * from the API the first time it is opened and then come from the TanStack Query cache.
 * @param props Store, selection and selection callback.
 * @returns The tree panel.
 */
export function CategoryTree({ store, selectedId, onSelect }: CategoryTreeProps) {
  const queryClient = useQueryClient();
  const roots = useQuery(categoryChildrenQuery(store));
  const [loaded, setLoaded] = useState<ReadonlyMap<number, readonly CategoryNode[]>>(new Map());
  const [loading, setLoading] = useState<ReadonlySet<number>>(new Set());
  const containerRef = useRef<HTMLDivElement>(null);
  const { width, height } = useElementSize(containerRef);

  const rows = useMemo(() => buildTreeRows(roots.data ?? [], loaded, loading), [roots.data, loaded, loading]);

  /** Loads the children of a node the first time it is opened. */
  const handleToggle = useCallback(
    (id: string) => {
      const parentId = Number(id);
      if (!Number.isSafeInteger(parentId) || loaded.has(parentId) || loading.has(parentId)) {
        return;
      }
      setLoading((current) => new Set(current).add(parentId));
      void queryClient
        .query(categoryChildrenQuery(store, parentId))
        .then(
          (children) => {
            setLoaded((current) => new Map(current).set(parentId, children));
          },
          () => {
            // Leave the node unloaded; opening it again retries.
          },
        )
        .finally(() => {
          setLoading((current) => {
            const next = new Set(current);
            next.delete(parentId);
            return next;
          });
        });
    },
    [loaded, loading, queryClient, store],
  );

  return (
    <div className="flex h-full min-h-0 flex-col">
      <Button
        variant="ghost"
        size="sm"
        className={cn("mx-2 mt-2 justify-start", selectedId === null && "bg-accent font-medium")}
        onClick={() => {
          onSelect(null);
        }}
      >
        <LayersIcon />
        {t.products.allProducts}
      </Button>
      <div ref={containerRef} className="min-h-0 flex-1 px-2 py-1">
        {roots.isPending ? (
          <div className="grid gap-2 p-1">
            {Array.from({ length: 8 }, (_, i) => (
              <Skeleton key={i} className="h-6" />
            ))}
          </div>
        ) : roots.isError ? (
          <p role="alert" className="p-2 text-sm text-destructive">
            {t.common.loadError}
          </p>
        ) : rows.length === 0 ? (
          <p className="p-2 text-sm text-muted-foreground">{t.products.categoriesEmpty}</p>
        ) : (
          height > 0 && (
            <Tree<TreeRow>
              data={rows}
              width={width}
              height={height}
              rowHeight={30}
              indent={16}
              openByDefault={false}
              disableDrag
              disableDrop
              disableEdit
              disableMultiSelection
              aria-label={t.products.categories}
              // Always pass `selection` (empty for none): arborist memoizes on the props list, which must not change length.
              selection={selectedId === null ? "" : String(selectedId)}
              onToggle={handleToggle}
              onActivate={(node) => {
                if (node.data.category !== null) onSelect(node.data.category);
              }}
            >
              {CategoryTreeNode}
            </Tree>
          )
        )}
      </div>
    </div>
  );
}

/**
 * One row of the tree: arrow, folder icon, name and product count.
 * @param props Arborist node renderer props.
 * @returns The row content.
 */
function CategoryTreeNode({ node, style }: NodeRendererProps<TreeRow>) {
  const { category } = node.data;
  if (category === null) {
    return (
      <div style={style} className="flex h-full items-center gap-2 pl-6 text-xs text-muted-foreground">
        <LoaderIcon className="size-3.5 animate-spin" />
        {node.data.name}
      </div>
    );
  }
  const Folder = node.isOpen ? FolderOpenIcon : FolderIcon;
  return (
    <div
      style={style}
      className={cn(
        "flex h-full cursor-pointer items-center gap-1.5 rounded-md pr-2 text-sm hover:bg-accent",
        node.isSelected && "bg-accent font-medium text-accent-foreground",
      )}
      onClick={() => {
        if (node.isInternal) node.toggle();
      }}
    >
      <span className="flex size-4 shrink-0 items-center justify-center">
        {node.isInternal && (
          <ChevronRightIcon className={cn("size-3.5 transition-transform", node.isOpen && "rotate-90")} />
        )}
      </span>
      <Folder className="size-4 shrink-0 text-muted-foreground" />
      <span className="min-w-0 flex-1 truncate" title={category.name}>
        {category.name}
      </span>
      <span className="shrink-0 text-xs text-muted-foreground tabular-nums">
        {category.productCount.toLocaleString("ru-RU")}
      </span>
    </div>
  );
}

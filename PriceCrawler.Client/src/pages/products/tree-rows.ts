import type { CategoryNode } from "@/api/client";
import { t } from "@/i18n";

/** Row of the arborist tree: a category, or a "loading…" placeholder under a node whose children are loading. */
export interface TreeRow {
  /** Category id as a string (arborist ids are strings); placeholders use `loading-<parentId>`. */
  id: string;
  /** Display name. */
  name: string;
  /** The category, `null` for a placeholder. */
  category: CategoryNode | null;
  /** Children; `null` for a leaf, an empty array for a node not loaded yet. */
  children: TreeRow[] | null;
}

/**
 * Builds arborist rows from the loaded tree levels. A node with children that are not loaded yet gets an
 * empty array (so it shows as expandable), or a placeholder row while its children load.
 * @param nodes Categories of one level.
 * @param loaded Children already loaded, by parent id.
 * @param loading Parent ids whose children are loading.
 * @returns Rows of that level.
 */
export function buildTreeRows(
  nodes: readonly CategoryNode[],
  loaded: ReadonlyMap<number, readonly CategoryNode[]>,
  loading: ReadonlySet<number>,
): TreeRow[] {
  return nodes.map((category) => {
    const children = loaded.get(category.id);
    let rows: TreeRow[] | null = null;
    if (children !== undefined) {
      rows = buildTreeRows(children, loaded, loading);
    } else if (category.hasChildren) {
      rows = loading.has(category.id)
        ? [{ id: `loading-${category.id}`, name: t.products.loadingChildren, category: null, children: null }]
        : [];
    }
    return { id: String(category.id), name: category.name, category, children: rows };
  });
}

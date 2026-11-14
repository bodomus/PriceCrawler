import { describe, expect, it } from "vitest";

import { category } from "@/test/fixtures";

import { formatListParams, parseListParams } from "./list-state";
import { sparklinePoints } from "./sparkline-points";
import { formatSort, groupName, parseSort } from "./table-helpers";
import { buildTreeRows } from "./tree-rows";

describe("list state in the address", () => {
  it("falls back to defaults for garbage", () => {
    expect(parseListParams(new URLSearchParams("category=-3&page=abc&size=7&group=brand"))).toEqual({
      categoryId: null,
      search: "",
      sort: "",
      page: 1,
      pageSize: 50,
      groupBy: null,
    });
  });

  it("round-trips and leaves defaults out", () => {
    const list = { categoryId: 5, search: "сир", sort: "-price", page: 3, pageSize: 100, groupBy: "promo" as const };
    const params = formatListParams(list);
    expect(parseListParams(params)).toEqual(list);
    expect(formatListParams({ ...list, page: 1, pageSize: 50, search: "", sort: "" }).toString()).toBe(
      "category=5&group=promo",
    );
  });
});

describe("sort expression", () => {
  it("parses and formats several terms", () => {
    const sorting = parseSort("-priceChange, name");
    expect(sorting).toEqual([
      { id: "priceChange", desc: true },
      { id: "name", desc: false },
    ]);
    expect(formatSort(sorting)).toBe("-priceChange,name");
    expect(parseSort("")).toEqual([]);
  });
});

describe("groupName", () => {
  it("prefers the category name and falls back to the key", () => {
    expect(groupName({ key: "7", name: "Сири", count: 1 })).toBe("Сири");
    expect(groupName({ key: "promo", name: null, count: 1 })).toBe("Акция");
    expect(groupName({ key: "mystery", name: null, count: 1 })).toBe("mystery");
  });
});

describe("sparklinePoints", () => {
  it("needs two values", () => {
    expect(sparklinePoints([5], 100, 20)).toBe("");
  });

  it("puts the highest value at the top and a flat line in the middle", () => {
    expect(sparklinePoints([1, 3], 100, 20)).toBe("2.0,18.0 98.0,2.0");
    expect(sparklinePoints([4, 4], 100, 20)).toBe("2.0,10.0 98.0,10.0");
  });
});

describe("buildTreeRows", () => {
  const roots = [category(1, "A", { hasChildren: true }), category(2, "B")];

  it("marks unloaded parents as expandable and leaves as leaves", () => {
    const rows = buildTreeRows(roots, new Map(), new Set());
    expect(rows.map((row) => row.children)).toEqual([[], null]);
  });

  it("shows a placeholder while loading and the children once loaded", () => {
    const loading = buildTreeRows(roots, new Map(), new Set([1]));
    expect(loading[0]?.children?.[0]?.category).toBeNull();

    const loaded = buildTreeRows(roots, new Map([[1, [category(3, "C", { parentId: 1 })]]]), new Set());
    expect(loaded[0]?.children?.map((row) => row.name)).toEqual(["C"]);
  });
});

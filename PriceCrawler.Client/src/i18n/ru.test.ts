import { describe, expect, it } from "vitest";

import { ru } from "./ru";

describe("ru.products.productCount", () => {
  it.each([
    [1, "1 товар"],
    [3, "3 товара"],
    [5, "5 товаров"],
    [21, "21 товар"],
    [1234, "1\u00a0234 товара"], // ru-RU groups digits with a no-break space,
  ])("formats %i", (count, expected) => {
    expect(ru.products.productCount(count)).toBe(expected);
  });
});

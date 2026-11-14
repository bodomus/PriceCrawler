import { describe, expect, it } from "vitest";

import { formatPack, formatPercentChange, formatPrice, formatPriceChange } from "./format";

describe("format", () => {
  it("formats prices with two decimals and a comma", () => {
    expect(formatPrice(25.5)).toBe("25,50");
    expect(formatPrice(1234.5)).toBe("1 234,50");
  });

  it("signs changes", () => {
    expect(formatPriceChange(1.9)).toBe("+1,90");
    expect(formatPriceChange(-4.5)).toBe("-4,50");
    expect(formatPercentChange(-15)).toBe("-15,0 %");
  });

  it("formats packs and hides unknown ones", () => {
    expect(formatPack(0.9, "л")).toBe("0,9 л");
    expect(formatPack(null, "л")).toBe("");
  });
});

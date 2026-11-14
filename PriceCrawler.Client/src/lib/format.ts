const priceFormat = new Intl.NumberFormat("ru-RU", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const signedPriceFormat = new Intl.NumberFormat("ru-RU", {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
  signDisplay: "exceptZero",
});
const signedPercentFormat = new Intl.NumberFormat("ru-RU", {
  minimumFractionDigits: 1,
  maximumFractionDigits: 1,
  signDisplay: "exceptZero",
});
const packFormat = new Intl.NumberFormat("ru-RU", { maximumFractionDigits: 3 });

/**
 * Formats a price in hryvnias without the currency sign, e.g. `25,50`.
 * @param value Price.
 * @returns Formatted text.
 */
export function formatPrice(value: number): string {
  return priceFormat.format(value);
}

/**
 * Formats a price change with its sign, e.g. `+1,90` or `−4,50`.
 * @param value Change.
 * @returns Formatted text.
 */
export function formatPriceChange(value: number): string {
  return signedPriceFormat.format(value);
}

/**
 * Formats a percent change with its sign and one decimal, e.g. `−15,0 %`.
 * @param value Change in percent.
 * @returns Formatted text.
 */
export function formatPercentChange(value: number): string {
  return `${signedPercentFormat.format(value)} %`;
}

/**
 * Formats a pack size, e.g. `0,9 л`.
 * @param value Pack value, may be missing.
 * @param unit Pack unit, may be missing.
 * @returns Formatted text, or an empty string when the size is unknown.
 */
export function formatPack(value: number | null, unit: string | null): string {
  if (value === null || unit === null) {
    return "";
  }
  return `${packFormat.format(value)} ${unit}`;
}

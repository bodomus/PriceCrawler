/**
 * Builds the SVG polyline points of a series scaled into a box.
 * @param values Series, oldest first.
 * @param width Box width.
 * @param height Box height.
 * @param pad Inner padding so the stroke is not clipped.
 * @returns `points` attribute value; empty for fewer than two values.
 */
export function sparklinePoints(values: readonly number[], width: number, height: number, pad = 2): string {
  if (values.length < 2) {
    return "";
  }
  const min = Math.min(...values);
  const max = Math.max(...values);
  const span = max - min;
  const step = (width - 2 * pad) / (values.length - 1);
  return values
    .map((value, i) => {
      const x = pad + i * step;
      const y = span === 0 ? height / 2 : pad + (1 - (value - min) / span) * (height - 2 * pad);
      return `${x.toFixed(1)},${y.toFixed(1)}`;
    })
    .join(" ");
}

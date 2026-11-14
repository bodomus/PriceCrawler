import { cn } from "@/lib/utils";

import { sparklinePoints } from "./sparkline-points";

/** Props of {@link Sparkline}. */
export interface SparklineProps {
  /** Values oldest first. */
  values: readonly number[];
  /** Accessible description. */
  label?: string;
  /** Width in pixels. */
  width?: number;
  /** Height in pixels. */
  height?: number;
}

/**
 * Tiny line chart of daily prices for a table cell; green when the period ended lower, red when higher.
 * A full chart (ECharts) lives on the product card (MPC-103).
 * @param props Values and size.
 * @returns The SVG, or a dash when there is too little data.
 */
export function Sparkline({ values, label, width = 96, height = 28 }: SparklineProps) {
  const points = sparklinePoints(values, width, height);
  if (points === "") {
    return <span className="text-muted-foreground">—</span>;
  }
  const first = values[0] ?? 0;
  const last = values[values.length - 1] ?? 0;
  return (
    <svg
      width={width}
      height={height}
      viewBox={`0 0 ${width} ${height}`}
      role="img"
      aria-label={label}
      className={cn(
        "overflow-visible",
        last < first ? "text-success" : last > first ? "text-destructive" : "text-muted-foreground",
      )}
    >
      <polyline points={points} fill="none" stroke="currentColor" strokeWidth={1.5} strokeLinejoin="round" />
    </svg>
  );
}

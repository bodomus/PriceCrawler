import { ArrowDownIcon, ArrowUpIcon, MinusIcon } from "lucide-react";

import { t } from "@/i18n";
import { formatPercentChange, formatPriceChange } from "@/lib/format";
import { cn } from "@/lib/utils";

/** Props of {@link PriceChange}. */
export interface PriceChangeProps {
  /** Absolute change since the previous day, or `null` when unknown. */
  change: number | null;
  /** Change in percent, or `null` when unknown. */
  percent: number | null;
}

/**
 * Day's price change with an arrow: green down (cheaper), red up (dearer), grey unchanged.
 * @param props Change and percent.
 * @returns The change cell content.
 */
export function PriceChange({ change, percent }: PriceChangeProps) {
  if (change === null) {
    return (
      <span className="text-muted-foreground" title={t.products.changeUnknown}>
        —
      </span>
    );
  }
  const direction = change < 0 ? "down" : change > 0 ? "up" : "same";
  const Icon = direction === "down" ? ArrowDownIcon : direction === "up" ? ArrowUpIcon : MinusIcon;
  const label =
    direction === "down" ? t.products.changeDown : direction === "up" ? t.products.changeUp : t.products.changeSame;
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1 whitespace-nowrap tabular-nums",
        direction === "down" && "text-success",
        direction === "up" && "text-destructive",
        direction === "same" && "text-muted-foreground",
      )}
      title={label}
      data-direction={direction}
    >
      <Icon className="size-3.5 shrink-0" aria-label={label} />
      {direction !== "same" && (
        <span>
          {formatPriceChange(change)}
          {percent !== null && <span className="ml-1 text-xs opacity-80">({formatPercentChange(percent)})</span>}
        </span>
      )}
    </span>
  );
}

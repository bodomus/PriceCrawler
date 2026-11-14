import { ClockIcon } from "lucide-react";

import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { useNow } from "@/hooks/use-now";
import { t } from "@/i18n";
import { formatKyivTime } from "@/lib/kyiv-time";

/**
 * Current Kyiv time in the header; the whole site shows dates in this zone.
 * @returns The clock element.
 */
export function KyivClock() {
  const now = useNow();
  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <span
          className="hidden items-center gap-1.5 text-sm text-muted-foreground tabular-nums sm:inline-flex"
          data-testid="kyiv-clock"
        >
          <ClockIcon className="size-4" />
          {t.header.kyivTime} {formatKyivTime(now)}
        </span>
      </TooltipTrigger>
      <TooltipContent>{t.header.kyivTimeHint}</TooltipContent>
    </Tooltip>
  );
}

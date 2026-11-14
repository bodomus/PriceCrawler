import { useQuery } from "@tanstack/react-query";

import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { healthQuery } from "@/api/queries";
import { t } from "@/i18n";
import { cn } from "@/lib/utils";

/**
 * Coloured dot that shows whether PriceCrawler.Api answers `/health`.
 * @returns The status element.
 */
export function ApiStatus() {
  const { data, isPending } = useQuery(healthQuery());
  const label = isPending ? t.apiStatus.checking : data ? t.apiStatus.online : t.apiStatus.offline;
  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <span role="status" aria-label={label} className="inline-flex size-8 items-center justify-center">
          <span
            className={cn(
              "size-2.5 rounded-full",
              isPending ? "bg-muted-foreground/50" : data ? "bg-success" : "bg-destructive",
            )}
          />
        </span>
      </TooltipTrigger>
      <TooltipContent>{label}</TooltipContent>
    </Tooltip>
  );
}

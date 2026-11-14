import type { ComponentProps } from "react";

import { cn } from "@/lib/utils";

/**
 * Pulsing placeholder shown while data loads.
 * @param props Div props.
 * @returns The skeleton element.
 */
export function Skeleton({ className, ...props }: ComponentProps<"div">) {
  return <div data-slot="skeleton" className={cn("animate-pulse rounded-md bg-accent", className)} {...props} />;
}

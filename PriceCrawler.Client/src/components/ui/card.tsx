import type { ComponentProps } from "react";

import { cn } from "@/lib/utils";

/**
 * Card container (shadcn/ui).
 * @param props Div props.
 * @returns The card element.
 */
export function Card({ className, ...props }: ComponentProps<"div">) {
  return (
    <div
      data-slot="card"
      className={cn("flex flex-col gap-6 rounded-xl border bg-card py-6 text-card-foreground shadow-xs", className)}
      {...props}
    />
  );
}

/**
 * Card header with title and description.
 * @param props Div props.
 * @returns The header element.
 */
export function CardHeader({ className, ...props }: ComponentProps<"div">) {
  return <div data-slot="card-header" className={cn("grid auto-rows-min gap-1.5 px-6", className)} {...props} />;
}

/**
 * Card title.
 * @param props Div props.
 * @returns The title element.
 */
export function CardTitle({ className, ...props }: ComponentProps<"div">) {
  return <div data-slot="card-title" className={cn("leading-none font-semibold", className)} {...props} />;
}

/**
 * Secondary text under the card title.
 * @param props Div props.
 * @returns The description element.
 */
export function CardDescription({ className, ...props }: ComponentProps<"div">) {
  return <div data-slot="card-description" className={cn("text-sm text-muted-foreground", className)} {...props} />;
}

/**
 * Card body.
 * @param props Div props.
 * @returns The content element.
 */
export function CardContent({ className, ...props }: ComponentProps<"div">) {
  return <div data-slot="card-content" className={cn("px-6", className)} {...props} />;
}

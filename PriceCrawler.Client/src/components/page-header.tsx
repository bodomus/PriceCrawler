/** Props of {@link PageHeader}. */
export interface PageHeaderProps {
  /** Page title. */
  title: string;
  /** One line about the page. */
  description?: string;
}

/**
 * Title block at the top of a page.
 * @param props Title and description.
 * @returns The header element.
 */
export function PageHeader({ title, description }: PageHeaderProps) {
  return (
    <div className="mb-6 grid gap-1">
      <h2 className="text-2xl font-semibold tracking-tight">{title}</h2>
      {description && <p className="text-sm text-muted-foreground">{description}</p>}
    </div>
  );
}

import { ConstructionIcon } from "lucide-react";

import { PageHeader } from "@/components/page-header";
import { Card, CardContent } from "@/components/ui/card";
import { t } from "@/i18n";

/** Props of {@link ComingSoonPage}. */
export interface ComingSoonPageProps {
  /** Section title. */
  title: string;
  /** What the section will hold (and its ticket). */
  description: string;
}

/**
 * Placeholder for a section that a later ticket fills.
 * @param props Title and description.
 * @returns The page.
 */
export function ComingSoonPage({ title, description }: ComingSoonPageProps) {
  return (
    <>
      <PageHeader title={title} description={description} />
      <Card className="border-dashed">
        <CardContent className="flex items-center gap-3 text-sm text-muted-foreground">
          <ConstructionIcon className="size-5" />
          {t.common.comingSoon}
        </CardContent>
      </Card>
    </>
  );
}

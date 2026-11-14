import { createBrowserRouter, Navigate } from "react-router";

import { AppLayout } from "@/components/layout/app-layout";
import { t } from "@/i18n";
import { ComingSoonPage } from "@/pages/coming-soon-page";
import { NotFoundPage } from "@/pages/not-found-page";
import { ProductsPage } from "@/pages/products-page";

/** Route table of the site; every page renders inside {@link AppLayout}. */
export const routes = [
  {
    element: <AppLayout />,
    children: [
      { index: true, element: <Navigate to="/products" replace /> },
      { path: "products", element: <ProductsPage /> },
      {
        path: "analytics",
        element: <ComingSoonPage title={t.analytics.title} description={t.analytics.description} />,
      },
      {
        path: "development",
        element: <ComingSoonPage title={t.development.title} description={t.development.description} />,
      },
      { path: "users", element: <ComingSoonPage title={t.users.title} description={t.users.description} /> },
      { path: "*", element: <NotFoundPage /> },
    ],
  },
];

/**
 * Creates the browser router.
 * @returns Router for `RouterProvider`.
 */
export function createAppRouter() {
  return createBrowserRouter(routes);
}

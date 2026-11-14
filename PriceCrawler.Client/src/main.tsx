import "./index.css";

import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { RouterProvider } from "react-router/dom";

import { Providers } from "./app/providers";
import { createAppRouter } from "./app/router";

const router = createAppRouter();
const root = document.getElementById("root");
if (root === null) {
  throw new Error("Element #root is missing in index.html.");
}

createRoot(root).render(
  <StrictMode>
    <Providers>
      <RouterProvider router={router} />
    </Providers>
  </StrictMode>,
);

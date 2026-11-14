/// <reference types="vitest/config" />
import path from "node:path";

import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

/** Address of PriceCrawler.Api that the dev server proxies `/api` and `/health` to. */
const apiTarget = process.env.VITE_API_PROXY_TARGET ?? "http://localhost:5280";

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { "@": path.resolve(import.meta.dirname, "src") },
  },
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      "/api": apiTarget,
      "/health": apiTarget,
    },
  },
  build: {
    rolldownOptions: {
      output: {
        // Long-lived vendor chunks: app changes do not invalidate the browser cache of libraries.
        codeSplitting: {
          groups: [
            { name: "react", test: /node_modules[\\/](react|react-dom|scheduler)[\\/]/ },
            { name: "router", test: /node_modules[\\/]react-router/ },
            { name: "radix", test: /node_modules[\\/]@radix-ui/ },
            { name: "vendor", test: /node_modules/ },
          ],
        },
      },
    },
  },
  preview: {
    port: 4173,
    strictPort: true,
  },
  test: {
    environment: "jsdom",
    setupFiles: ["./src/test/setup.ts"],
    include: ["src/**/*.test.{ts,tsx}"],
    css: false,
  },
});

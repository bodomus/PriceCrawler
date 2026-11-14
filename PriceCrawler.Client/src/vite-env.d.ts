/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Base address of PriceCrawler.Api; empty means same origin (dev server proxy or reverse proxy). */
  readonly VITE_API_BASE_URL?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}

/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Origin of the API. Empty string = same origin (the compose/nginx case). */
  readonly VITE_API_BASE_URL?: string
  /** Dev-server proxy target. Build-time only, never read by app code. */
  readonly VITE_DEV_API_PROXY?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}

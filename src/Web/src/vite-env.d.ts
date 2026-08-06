interface ImportMetaEnv {
  readonly VITE_API_BASE_URL?: string
  readonly VITE_DEV_API_PROXY?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}

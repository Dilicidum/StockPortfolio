/// <reference types="vitest/config" />
import { copyFileSync, existsSync } from 'node:fs'
import { resolve } from 'node:path'
import { defineConfig, type Plugin } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { tanstackRouter } from '@tanstack/router-plugin/vite'

const base = process.env.VITE_BASE ?? '/'

/**
 * GitHub Pages has no rewrite rules, so a deep link like /<repo>/dashboard is a
 * 404 from the static host before the SPA ever loads. Pages serves 404.html for
 * any unmatched path, so shipping a byte-identical copy of index.html under that
 * name turns the 404 into the app, which then routes the URL itself.
 *
 * Only emitted when a non-root base is configured — that is exactly the Pages
 * build. nginx handles this with try_files and needs no such file.
 */
function githubPagesFallback(): Plugin {
  let outDir = 'dist'

  return {
    name: 'tz:github-pages-404',
    apply: 'build',
    configResolved(config) {
      outDir = resolve(config.root, config.build.outDir)
    },
    // writeBundle, not generateBundle: the copy has to be of the FINAL
    // index.html, the one Vite has already rewritten to point at the hashed
    // asset filenames. Emitting from the source index.html instead produces a
    // 404.html that requests /src/main.tsx and renders blank in production —
    // and only on deep links, so it survives every smoke test of "/".
    writeBundle() {
      if (base === '/') return
      const index = resolve(outDir, 'index.html')
      if (existsSync(index)) copyFileSync(index, resolve(outDir, '404.html'))
    },
  }
}

// `base` MUST come from the environment.
//
// Three deployment targets, three different bases:
//   docker compose  -> nginx serves the SPA at "/"          -> VITE_BASE unset  -> "/"
//   GitHub Pages    -> served at "/<repo>/"                 -> VITE_BASE="/<repo>/"
//   vite dev        -> "/"
//
// Hardcoding "/<repo>/" makes the compose SPA request /<repo>/assets/*.js, which
// nginx 404s, and the page renders blank with no console error worth the name.
// The router's `basepath` is derived from import.meta.env.BASE_URL (see main.tsx)
// so the two can never drift apart.
export default defineConfig({
  base,
  plugins: [
    // Must run before the react plugin: it generates src/routeTree.gen.ts.
    tanstackRouter({ target: 'react', autoCodeSplitting: false }),
    react(),
    tailwindcss(),
    githubPagesFallback(),
  ],
  server: {
    port: 5173,
    // Dev-only convenience so `npm run dev` talks to a locally running API
    // without CORS. Under compose, nginx does this instead and
    // VITE_API_BASE_URL stays empty.
    proxy: {
      '/api': {
        target: process.env.VITE_DEV_API_PROXY ?? 'http://localhost:8080',
        changeOrigin: true,
      },
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./tests/setup.ts'],
    include: ['tests/**/*.test.{ts,tsx}'],
    restoreMocks: true,
  },
})

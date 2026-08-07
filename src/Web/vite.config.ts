/// <reference types="vitest/config" />
import { copyFileSync, existsSync } from 'node:fs'
import { resolve } from 'node:path'
import { defineConfig, type Plugin } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { tanstackRouter } from '@tanstack/router-plugin/vite'

const base = process.env.VITE_BASE ?? '/'

function githubPagesFallback(): Plugin {
  let outDir = 'dist'

  return {
    name: 'tz:github-pages-404',
    apply: 'build',
    configResolved(config) {
      outDir = resolve(config.root, config.build.outDir)
    },
    writeBundle() {
      if (base === '/') return
      const index = resolve(outDir, 'index.html')
      if (existsSync(index)) copyFileSync(index, resolve(outDir, '404.html'))
    },
  }
}

export default defineConfig({
  base,
  plugins: [
    tanstackRouter({ target: 'react', autoCodeSplitting: false }),
    react(),
    tailwindcss(),
    githubPagesFallback(),
  ],
  server: {
    port: 5173,
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

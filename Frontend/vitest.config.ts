// Apply Windows Node v24 EPERM realpath workaround
try {
  // @ts-ignore
  require('./patch-fs.cjs')
} catch (_) {}

import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import path from 'path'

export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./vitest.setup.ts'],
    alias: {
      '@': path.resolve(__dirname, './'),
    },
    include: ['**/*.test.{ts,tsx}', '**/*.spec.{ts,tsx}'],
  },
})

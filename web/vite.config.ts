import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { execSync } from 'child_process'
import path from 'path'
import fs from 'fs'

function codegenWatchPlugin() {
  const openApiSpec = path.resolve(import.meta.dirname, '../openapi/Skoleoverblikket.Api.json')

  return {
    name: 'codegen-watch',
    configureServer(server: import('vite').ViteDevServer) {
      const watcher = fs.watch(path.dirname(openApiSpec), (_event, filename) => {
        if (filename !== 'Skoleoverblikket.Api.json') return
        console.log('\n[codegen] OpenAPI spec changed — regenerating...')
        try {
          execSync('npm run api:generate --silent', { cwd: import.meta.dirname, stdio: 'inherit' })
          console.log('[codegen] Done.')
          server.ws.send({ type: 'full-reload' })
        } catch (e) {
          console.error('[codegen] Failed:', e)
        }
      })
      server.httpServer?.on('close', () => watcher.close())
    },
  }
}

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss(), codegenWatchPlugin()],
  resolve: {
    alias: {
      '@': path.resolve(import.meta.dirname, './src'),
    },
  },
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': {
        target: 'http://127.0.0.1:5000',
        changeOrigin: true,
      },
    },
  },
})

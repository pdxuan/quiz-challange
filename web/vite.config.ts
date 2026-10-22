/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// The server (dotnet run, "http" profile) listens on 5080. In development Vite proxies the
// API and the hub so the browser sees one origin (no CORS, the auth cookie just works).
const server = 'http://localhost:5080'

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': server,
      '/hubs': { target: server, ws: true },
    },
  },
  build: {
    // Served by the .NET server from wwwroot (one process for the demo).
    outDir: '../src/Quiz.Server/wwwroot',
    emptyOutDir: true,
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
  },
})

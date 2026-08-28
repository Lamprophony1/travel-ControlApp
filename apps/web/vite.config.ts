import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { VitePWA } from 'vite-plugin-pwa'

export default defineConfig({
  plugins: [
    react(),
    VitePWA({
      registerType: 'prompt',
      includeAssets: ['icon.svg', 'offline.html'],
      manifest: {
        name: 'Control de Viaje — Boda Cielito & Ronaldo',
        short_name: 'Control de Viaje',
        description: 'Control privado de pasajeros, alojamiento, vuelos, equipaje y transfers.',
        theme_color: '#12304a',
        background_color: '#f4f8fb',
        display: 'standalone',
        start_url: '/',
        icons: [
          { src: '/icon.svg', sizes: 'any', type: 'image/svg+xml', purpose: 'any' },
          { src: '/icon-maskable.svg', sizes: 'any', type: 'image/svg+xml', purpose: 'maskable' }
        ]
      },
      workbox: {
        globPatterns: ['**/*.{js,css,html,svg,woff2}'],
        navigateFallback: '/offline.html',
        navigateFallbackDenylist: [/^\/api\//, /^\/health\//],
        runtimeCaching: []
      }
    })
  ],
  server: { proxy: { '/api': 'http://localhost:5090', '/health': 'http://localhost:5090' } }
})


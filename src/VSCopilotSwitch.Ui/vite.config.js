import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';

export default defineConfig({
  plugins: [vue()],
  server: {
    proxy: {
      '/api': 'http://127.0.0.1:11434',
      '/internal': 'http://127.0.0.1:11434',
      '/health': 'http://127.0.0.1:11434'
    }
  },
  build: {
    outDir: 'dist',
    emptyOutDir: true
  }
});

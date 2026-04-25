import { fileURLToPath, URL } from 'node:url';

import { defineConfig } from 'vite';
import plugin from '@vitejs/plugin-vue';
import { env } from 'process';

const target = env.ASPNETCORE_URLS ? env.ASPNETCORE_URLS.split(';')[0] : 'http://localhost:5124';

// Vite 调试服务固定使用 HTTP，避免本地证书和 HTTPS 端口干扰。
export default defineConfig({
  plugins: [plugin()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url))
    }
  },
  server: {
    host: '127.0.0.1',
    port: parseInt(env.DEV_SERVER_PORT || '5173'),
    proxy: {
      '/api': {
        target,
        secure: false
      },
      '/internal': {
        target,
        secure: false
      },
      '/health': {
        target,
        secure: false
      }
    }
  }
});

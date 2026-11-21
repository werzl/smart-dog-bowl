import { defineConfig } from "vite";

export default defineConfig({
  server: {
    port: Number(process.env.PORT ?? 5173),
    strictPort: true,
    proxy: {
      "/api": {
        target: process.env.API_BASE_URL ?? "http://localhost:5200",
        changeOrigin: true,
      },
    },
  },
});

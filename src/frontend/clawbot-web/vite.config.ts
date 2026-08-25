import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import path from "node:path";

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "src"),
    },
  },
  build: {
    rolldownOptions: {
      checks: {
        invalidAnnotation: false,
      },
    },
  },
  server: {
    port: 15876,
    proxy: {
      "/api": {
        target: "http://127.0.0.1:15873",
        changeOrigin: true,
      },
      "/auth": {
        target: "http://127.0.0.1:15873",
        changeOrigin: true,
      },
      "/hubs": { target: "http://127.0.0.1:15873", ws: true },
    },
  },
});

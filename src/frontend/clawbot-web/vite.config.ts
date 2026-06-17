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
      "/api": "http://localhost:15873",
      "/auth": "http://localhost:15873",
      "/hubs": { target: "http://localhost:15873", ws: true },
    },
  },
});

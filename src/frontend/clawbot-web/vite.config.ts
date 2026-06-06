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
  server: {
    port: 5173,
    proxy: {
      // API serves routes at root (/auth, /roles, ...) with no /api prefix,
      // so strip the prefix the frontend adds via its axios baseURL.
      "/api": {
        target: "http://localhost:5051",
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/api/, ""),
      },
      "/hubs": { target: "http://localhost:5051", ws: true },
    },
  },
});

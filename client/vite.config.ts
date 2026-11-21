import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

const proxy = {
  "/api": { target: "https://localhost:7090", changeOrigin: true, secure: false },
  "/.well-known": { target: "https://localhost:7090", changeOrigin: true, secure: false },
  "/health": { target: "https://localhost:7090", changeOrigin: true, secure: false },
};

export default defineConfig({
  plugins: [react()],
  server: { port: 3010, strictPort: true, proxy },
  preview: { port: 3010, strictPort: true, proxy },
});

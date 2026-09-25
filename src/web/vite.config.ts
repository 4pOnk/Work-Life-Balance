import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

const port = Number(process.env.WLB_DEV_API_PORT ?? 47831);
if (!Number.isInteger(port) || port < 1024 || port > 65535)
  throw new Error("Invalid WLB_DEV_API_PORT");
const backend = `http://127.0.0.1:${port}`;

export default defineConfig({
  plugins: [react()],
  build: { outDir: "../WorkLifeBalance.Host/wwwroot", emptyOutDir: true },
  server: {
    proxy: {
      "/api": {
        target: backend,
        changeOrigin: true,
        configure: (proxy) =>
          proxy.on("proxyReq", (request) =>
            request.setHeader("Origin", backend),
          ),
      },
    },
  },
});

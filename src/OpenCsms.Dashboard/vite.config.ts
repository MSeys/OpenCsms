import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";

// The API serves the built dashboard at the site root, so the SPA is built for "/". The dev server
// proxies the API so a running src/OpenCsms.Api instance is the only backend; point VITE_API_TARGET
// elsewhere to move it.
const target = process.env.VITE_API_TARGET ?? "http://localhost:5126";

export default defineConfig({
  base: "/",
  plugins: [vue()],
  server: {
    port: 5181,
    strictPort: true,
    proxy: {
      "/api": { target },
      "/healthz": { target },
      "/ocpp": { target, ws: true }
    }
  },
  build: {
    outDir: "dist",
    emptyOutDir: true,
    sourcemap: false
  }
});

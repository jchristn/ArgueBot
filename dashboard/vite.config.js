import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
export default defineConfig({
  plugins: [react()],
  build: { target: "es2022" },
  base: "/",
  server: {
    host: "127.0.0.1",
    proxy: {
      "/v1.0": "http://127.0.0.1:4417",
      "/health": "http://127.0.0.1:4417",
      "/openapi.json": "http://127.0.0.1:4417",
    },
    fs: { allow: [".."] },
  },
  test: {
    include: ["src/**/*.test.{js,jsx}"],
    environment: "jsdom",
    globals: true,
    setupFiles: ["./src/test-setup.js"],
  },
});

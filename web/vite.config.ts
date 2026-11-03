/// <reference types="vitest/config" />
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      "/api": "http://localhost:5110",
      "/swagger": "http://localhost:5110",
      "/openapi": "http://localhost:5110",
    },
  },
  test: {
    environment: "jsdom",
    setupFiles: ["./src/test/setup.ts"],
    // Not UTC, so a date computed in UTC instead of local time fails the tests (CI runners are UTC).
    env: { TZ: "Europe/Kyiv" },
  },
});

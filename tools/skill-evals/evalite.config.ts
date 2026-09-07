import { defineConfig } from "evalite/config";

export default defineConfig({
  // Conservative: future routing evals may call paid models.
  maxConcurrency: 2,
  testTimeout: 60_000,
  hideTable: false,
});

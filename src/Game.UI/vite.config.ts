import { defineConfig } from 'vite';
import { resolve } from 'path';

export default defineConfig({
  build: {
    lib: {
      entry: resolve(__dirname, 'Frontend/game.ts'),
      name: 'GameViewport',
      fileName: 'game-bundle',
      // ES module: Rapier's wasm-bindgen wrapper uses top-level await, which the
      // IIFE output format cannot express. Loaded via <script type="module">.
      formats: ['es']
    },
    outDir: resolve(__dirname, 'wwwroot/dist'), // Exports directly to Blazor assets
    emptyOutDir: true,
    sourcemap: true
  }
});

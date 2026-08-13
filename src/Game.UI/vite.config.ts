import { defineConfig } from 'vite';
import { resolve } from 'path';

export default defineConfig({
  build: {
    lib: {
      entry: resolve(__dirname, 'Frontend/game.ts'),
      name: 'GameViewport',
      fileName: 'game-bundle',
      formats: ['iife'] // Immediately Invoked Function Expression for direct browser execution
    },
    outDir: resolve(__dirname, 'wwwroot/dist'), // Exports directly to Blazor assets
    emptyOutDir: true,
    sourcemap: true
  }
});

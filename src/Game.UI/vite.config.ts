import { defineConfig } from 'vite';
import { resolve } from 'path';

// Compile-time render-source flag (ADR-007): 'sse' (default — Game.Web
// static-SSR bridge) or 'local-buffer' (`vite build --mode local` —
// co-located Game.Wasm host, Phase 2/3). `define` replaces the identifier
// before bundling so the unused transport branch is tree-shaken away.
const renderSource = (mode: string) => (mode === 'wasm' ? 'local-buffer' : 'sse');

export default defineConfig(({ mode }) => ({
    define: {
        __RENDER_SOURCE__: JSON.stringify(renderSource(mode))
    },
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
}));

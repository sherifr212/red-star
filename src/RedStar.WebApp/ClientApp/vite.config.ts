import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vite';
import tailwindcss from '@tailwindcss/vite';

// ESM-safe path resolution (no __dirname under "type": "module") -- see
// RedStar.WebApp/GETTING_STARTED.md's "Core concepts" if this looks unfamiliar.
const resolvePath = (relativePath: string) => fileURLToPath(new URL(relativePath, import.meta.url));

// https://vite.dev/config/
export default defineConfig({
  plugins: [tailwindcss()],
  build: {
    outDir: '../wwwroot/dist',
    emptyOutDir: true,
    manifest: true,
    rollupOptions: {
      input: {
        // The one shared Tailwind stylesheet, its own entry so Views/Shared/_Layout.cshtml can
        // link it once via vite-href instead of every page importing/duplicating it. See
        // RedStar.WebApp/CLAUDE.md's "ClientApp/ conventions" (styling rule).
        app: resolvePath('./styles/app.css'),
        // One entry per page: Views/<Controller>/<Action>.cshtml references this via vite-src.
        // Add one line here per new page -- see RedStar.WebApp/CLAUDE.md's "steps to add a new
        // page" for the full convention.
        home: resolvePath('./pages/home/main.ts'),
      },
    },
  },
  server: {
    port: 5173,
    strictPort: true,
  },
});

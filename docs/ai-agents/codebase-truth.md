# Codebase Truth — Verified Facts

Every fact below was verified against `.csproj`, `package.json`, `vite.config.ts`, `tsconfig.json`, and source files on 2026-08-13. When code and prose disagree, this file wins. Trust these files over `README.md` setup prose:

- `bonoboWebGame.slnx` (XML solution format — not `.sln`)
- `src/*/*.csproj`
- `src/Game.UI/package.json`

## Projects

| Project | SDK | Target(s) | References | Notable flags |
| --- | --- | --- | --- | --- |
| `src/Game.Engine` | `Microsoft.NET.Sdk` | `net10.0` | — | nullable + implicit usings; `GameSimulation.cs` + `RenderMessageEvent(string)` — `PublishHello()` raises `OnRenderMessage` with "Hello world to PixiJs Gaming!" |
| `src/Game.UI` | `Microsoft.NET.Sdk.Razor` | `net10.0` | `Game.Engine` | `<SupportedPlatform Include="browser" />`; `Microsoft.AspNetCore.Components.Web` 10.0.10; MSB4018 workaround target |
| `src/Game.Web` | `Microsoft.NET.Sdk.Web` | `net10.0` | `Game.UI` | `BlazorDisableThrowNavigationException=true` |
| `src/Game.Maui` | `Microsoft.NET.Sdk.Razor` + `UseMaui` | `net10.0-android`; adds `net10.0-ios`, `net10.0-maccatalyst` when not Linux; adds `net10.0-windows10.0.19041.0` on Windows | `Game.UI` | `OutputType=Exe`, `MauiXamlInflator=SourceGen`, `WindowsPackageType=None` (unpackaged), `<SingleProject>true` |

## Game.UI frontend pipeline

- Entry: `src/Game.UI/Frontend/game.ts` — exports `initGame(containerId)` and `renderText(message)`; also binds `(window as any).initGame` / `.renderText`.
  - Logs every pipeline step under the `[pixi-debug]` prefix (bundle load, container lookup/size, `app.init`, canvas append, text set, centering). `initGame` waits 50 ms for layout, then forces `100vw`/`100vh` if container measures 0×0; creates `Application` (`resizeTo`, `backgroundAlpha: 0`, `antialias`, `hello`). `renderText` creates/updates a centered PixiJS `Text` from an engine-driven payload and re-centers on window resize.
- CSS entry: `src/Game.UI/Frontend/app.css` — `@import "tailwindcss";` + `@theme` block (`--color-hud-bg`, `--color-mana`, `--font-game`).
- `vite.config.ts`: lib mode, IIFE format, entry `Frontend/game.ts`, name `GameViewport`, `fileName: 'game-bundle'`, `outDir: wwwroot/dist`, `emptyOutDir: true`, `sourcemap: true`.
- `tsconfig.json`: `strict`, `target ES2022`, `noEmit: true`, includes `Frontend/**/*.ts` **and** `vite.config.ts`.
  - **Known failure:** `npx tsc --noEmit` fails because `vite.config.ts` imports `path` without Node type definitions (`@types/node` missing).
- `package.json` deps: `pixi.js ^8.19.0`. Dev deps: `vite ^8.2.1`, `typescript ^7.0.2`, `tailwindcss ^4.3.3`, `@tailwindcss/cli ^4.3.3`, `postcss ^8.5.26`, `autoprefixer ^10.5.4`.
- Scripts: `build:js` = `vite build`; `build:css` = `npx @tailwindcss/cli -i ./Frontend/app.css -o ./wwwroot/dist/app.css --minify`; `build` = js then css; `watch:js` / `watch:css` are separate long-running watchers.

## MSB4018 workaround (`Game.UI.csproj`)

`.NET 10` static-web-asset precompression breaks on the Vite IIFE bundle. The csproj hooks `DiscoverPrecompressedAssetsDependsOn` and the `ExcludeViteBundleFromCompression` target removes any static web asset named `game-bundle.iife.js` before compression. Do not remove this target.

## Hosts

- `Game.Web/Program.cs`: **static SSR only** — no Interactive Server, no SignalR circuit, no reconnect modal. `AddRazorComponents()`; `UseStatusCodePagesWithReExecute("/not-found")`; `UseHttpsRedirection`; `UseAntiforgery()` (**required** — Razor Components endpoints carry antiforgery metadata even for static SSR); `MapStaticAssets()`; `MapRazorComponents<App>().AddAdditionalAssemblies(typeof(GameView).Assembly)`.
  - **Required:** RCL routes under static SSR only resolve with `AddAdditionalAssemblies` on `MapRazorComponents`; the `Router` `AdditionalAssemblies` parameter alone is not enough for direct HTTP requests.
- `Game.Web/Components/App.razor`: renders `<Routes />` + `game-bundle.iife.js`, then an inline `load`-event script that reads `#pixi-viewport[data-message]`, calls `initGame("pixi-viewport")`, then `renderText(message)` — all with `[pixi-debug]` console logs. **No `ReconnectModal`, no `<ImportMap />`, no `blazor.web.js`** — the served HTML contains zero `_framework` references. (Interactive Server leftovers `Counter.razor`/`Weather.razor` deleted; `Home.razor` deleted earlier — it shadowed GameView's `/` route.)
- `Game.Web/Components/Routes.razor`: `<Router AppAssembly="typeof(Program).Assembly" AdditionalAssemblies="new[] { typeof(GameView).Assembly }">` — discovers shared RCL routes.
- `Game.Maui/MainPage.xaml`: `BlazorWebView` with `RootComponent` → `Game.Maui.Components.Routes`.

## Key UI components

- `src/Game.UI/GameView.razor`: `@page "/"`, full-viewport fixed container (`position: fixed; inset 0; background #020617`), top HUD bar, `#pixi-viewport` div carrying the engine payload in `data-message`. Static SSR flow: `OnInitialized` subscribes `_simulation.OnRenderMessage` and calls `PublishHello()`; `HandleRenderMessage` stores `ev.Message` into `Message` (rendered into `data-message`); the client-side `load` script picks it up and pushes it to PixiJS. `Dispose` unsubscribes. **No `IJSRuntime` calls** (no interactivity on the web host). Uses inline styles — Tailwind classes not used yet.
- Template leftovers still present (not production code):
  - `Game.UI`: `Component1.razor`, `Component1.razor.css`, `ExampleJsInterop.cs`, `wwwroot/exampleJsInterop.js`.
  - `Game.Web`: Bootstrap lib assets.
  - `Game.Maui`: `Components/Pages/Counter.razor`, `Weather.razor`, `Home.razor`.

## Build commands

From repo root:

```powershell
dotnet build bonoboWebGame.slnx
dotnet test          # no test projects yet; validates solution only
dotnet watch --project src/Game.Web
```

From `src/Game.UI` (build frontend first; never run two `dotnet` commands concurrently — static-web-asset compression races):

```powershell
npm ci
npm run build
npx tsc --noEmit    # KNOWN FAILURE: vite.config.ts lacks Node type definitions
```

## Environment constraints

- MAUI builds require .NET MAUI workloads; platform TFMs vary by host OS (Linux excludes iOS/MacCatalyst; Windows adds `net10.0-windows10.0.19041.0`).
- `bin/`, `obj/`, `node_modules/`, and `wwwroot/dist` output are git-ignored — never commit.

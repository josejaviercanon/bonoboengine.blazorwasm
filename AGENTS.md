# Agent Guide

## Repository Shape

- `bonoboWebGame.slnx` is the solution; projects target .NET 10.
- `src/Game.Engine` is a plain C# class library. Keep engine logic independent of UI and platform code.
- `src/Game.UI` is the shared Razor Class Library. It references `Game.Engine` and owns shared Razor components plus PixiJS assets.
- `src/Game.Web` is the Blazor Web App host. It uses **static SSR only** (no Interactive Server, no SignalR circuit, no reconnect modal) and discovers shared RCL routes via `AddAdditionalAssemblies` in `Program.cs` plus the `Router` `AdditionalAssemblies` in `Components/Routes.razor`. PixiJS is bootstrapped client-side from an inline `load`-event script in `Components/App.razor`, which reads the engine payload from `#pixi-viewport[data-message]`.
- `src/Game.Maui` is the .NET MAUI Blazor Hybrid host. It targets Android by default, plus iOS, Mac Catalyst, and Windows when supported by the OS/workloads.

## Agent References

- `docs/pixijs-documentation-for-llms/pixyjs.md` — complete vendored PixiJS v8 API reference (llm.txt format). Consult it when writing or verifying any PixiJS code; prefer its API facts over memory or generic web knowledge.
- `net-microsoft-documentation` MCP server — official, up-to-date Microsoft Learn docs for .NET, ASP.NET Core, Blazor, and MAUI. Use it for framework/API verification.
- `docs/2d-games` and `docs/game-development` — game architecture and gamedev workflow references (see `docs/index.md`).

## Commands

Build frontend assets before .NET commands. Do not run multiple `dotnet` commands concurrently; static-web-asset compression can race.

Run from repository root:

```powershell
dotnet build bonoboWebGame.slnx
dotnet test
```

Run from `src/Game.UI`:

```powershell
npm ci
npm run build
npx tsc --noEmit # currently fails: vite.config.ts lacks Node type definitions
```

Run web host from repository root:

```powershell
dotnet watch --project src/Game.Web
```

`Game.UI` scripts build Vite JavaScript first, then Tailwind CSS. Vite reads `Frontend/game.ts` and writes generated files to `src/Game.UI/wwwroot/dist`; do not hand-edit generated output. `npm run watch:js` and `npm run watch:css` are separate long-running watchers.

MAUI builds require .NET MAUI workloads. Platform-specific target frameworks may make full-solution builds depend on host OS and installed workloads.

## Verification Notes

- No test projects currently exist; `dotnet test` may report no tests while still validating the solution.
- `bin/`, `obj/`, `node_modules/`, and other build output are ignored. Do not commit them.
- Trust `.csproj`, `.slnx`, `package.json`, and executable build output over setup prose in `README.md`.
- `docs/index.md` describe architecture; `docs/ai-agents/codebase-truth.md` holds verified API facts; record significant decisions in `docs/adr/`.

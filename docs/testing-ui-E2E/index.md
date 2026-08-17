# playwright

# Testing in this repo (agentic guide)

Three test projects cover the simulation/presentation split. All commands verified working.

| Project | Stack | Scope | Run |
|---|---|---|---|
| `src/Game.Tests` | C#, **xUnit v3** (MTP runner) | Determinism self-checks, ECS simulation unit tests, snapshot shape | `dotnet test src/Game.Tests` |
| `src/Game.Tests.Aot` | C#, **TUnit** (MTP runner) | AOT/trim pattern checks over the `Game.Engine` closure (no ReflectionEmit, no runtime package refs, DependencyContext behavior) | `dotnet test src/Game.Tests.Aot` |
| `src/Game.Tests.UI` | **Node/TypeScript, Playwright** | E2E against the real `Game.Web` host (static SSR shell, `data-message` payload, PixiJS bootstrap, asset serving) | `cd src/Game.Tests.UI && npx playwright test` |

`Game.Tests` and `Game.Tests.Aot` are in `bonoboWebGame.slnx`; `Game.Tests.UI` is a Node project (no `.csproj`) and runs via npm.

## .NET 10 / MTP requirements

- `global.json` at repo root contains `{"test":{"runner":"Microsoft.Testing.Platform"}}`. **Required**: without it `dotnet test` silently misbehaves on .NET 10. Do not delete.
- Plain `dotnet test` from repo root runs both .NET test projects.
- xUnit v3 rules enforced by analyzers: async tests must use `TestContext.Current.CancellationToken`, not `CancellationToken.None`/class fixtures.
- TUnit assertion API note: collection counts use `HasCount(n)`, not `HasCount().EqualTo(n)`.

## Playwright (Game.Tests.UI) — rules and caveats

- **Chrome channel only**: config uses `channel: 'chrome'` (installed Chrome, no bundled browser download). For manual agent driving use `playwright-cli open --browser=chrome`.
- Config (`playwright.config.ts`): port **5902**, `webServer` boots `dotnet run --project ../../src/Game.Web` (Development env, HTTPS redirect suppressed). Set `GAME_WEB_EXTERNAL_URL` to reuse an already-running host.
- `workers: 1`, `fullyParallel: false` — the host holds singleton simulations.
- npm scripts: `test`, `test:headed`, `test:ui`, `report`, `typecheck`. Run `npm run typecheck` after spec edits.
- **Static-asset 500s after touching `Game.UI` RCL assets**: `_content/Game.UI/dist/*` (game-bundle.js, app.css) returning 500 means stale/partially-copied `bin`/`obj` static-web-asset output — usually from a build racing a server process. Kill all `Game.Web.exe` (`taskkill //F //IM Game.Web.exe`), then rebuild. If 500s persist: delete `src/Game.Web/bin` + `src/Game.Web/obj` (+ `src/Game.UI/obj` compressed cache) and rebuild. Never build while a host is running (static-web-asset compression race).
- **Bootstrap timing is a product contract**: `game-bundle.js` is an ES module with dynamic imports; its execution can finish *after* the window `load` event. The inline script in `Game.Web/Components/App.razor` polls up to 15 s for `window.initGame`. Tests assert canvas visibility with a 20 s timeout — do not shrink these without understanding cold-load module fetches.
- `/hello` `data-message` payload is plain text, not JSON. Home heading text is `PixiJS Examples` (there is no `<title>`).

## Standalone Screenshot Scripts (ESM Context)

When writing a standalone Node.js script (`.js` extension) that runs inside `src/Game.Tests.UI/`, you MUST use ESM syntax because `src/Game.Tests.UI/package.json` declares `"type": "module"`. Using `require()` in a `.js` file fails with `ReferenceError: require is not defined`.

### Root Cause

`package.json` `"type": "module"` instructs Node.js to parse all `.js` files in its scope as ECMAScript Modules, where the CommonJS `require()` function is not available. Use `import` syntax instead, or name the file `.cjs` to force CommonJS.

| Strategy | Extension | Syntax | Notes |
| --- | --- | --- | --- |
| **ESM Native** | `.js` | `import` | Aligns with `"type": "module"`; dynamic imports required inside closures |
| **CommonJS Override** | `.cjs` | `require()` | Zero refactoring; explicit extension in ESM-first codebase |
| **Synthetic Bridge** | `.js` | `createRequire` | Boilerplate; `import { createRequire } from 'module'` |

### Process Lifecycle — Avoid Orphaned .NET Processes

Never pass `{ shell: true }` to `child_process.spawn` on Windows. It wraps the command in `cmd.exe`; calling `host.kill()` terminates only the shell wrapper, leaving the child `.NET` host process alive and locking TCP port 5902.

**Correct Windows cleanup:**
```ts
if (process.platform === 'win32') {
  spawn('taskkill', ['/pid', host.pid.toString(), '/f', '/t']);
} else {
  host.kill('SIGKILL');
}
```

### Path Resolution

Do NOT specify both `cwd` and `--project` with relative paths simultaneously — `dotnet` resolves `--project` relative to `cwd`, doubling the traversal and breaking project resolution. Use `spawn('dotnet', ['run', '--project', '../../src/Game.Web'])` from the repo root without overriding `cwd`.

### Dynamic Readiness Polling — Replace Static Sleep

Use HTTP readiness polling (`http.get` loop) instead of `await wait(20000)` or `setTimeout`. Polling minimizes test latency and eliminates timing race conditions from arbitrary sleep windows.

### Corrected Standalone Screenshot Command

Run from the repo root (`x:/PROJECTS/BonoboEngine/Repos/bonoboengine.blazorwasm`):

```powershell
$screenshot = @'
import { chromium } from '@playwright/test';
import { spawn } from 'child_process';
import http from 'http';
const pollServer = (url, timeoutMs = 30000) => {
  const start = Date.now();
  return new Promise((resolve, reject) => {
    const check = () => {
      http.get(url, (res) => resolve())
        .on('error', () => {
          if (Date.now() - start > timeoutMs) reject(new Error('Server startup timeout'));
          else setTimeout(check, 500);
        });
    };
    check();
  });
};
(async () => {
  const host = spawn('dotnet', ['run', '--project', '../../src/Game.Web']);
  try {
    await pollServer('http://localhost:5902');
    const browser = await chromium.launch({ channel: 'chrome' });
    const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
    const errors = [];
    page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
    page.on('pageerror', e => errors.push(String(e)));
    await page.goto('http://localhost:5902/examples/games/asteroids', { waitUntil: 'networkidle' });
    await page.keyboard.press('Enter');
    await page.keyboard.press('ArrowUp');
    await page.keyboard.press('Space');
    await page.screenshot({ path: 'asteroids-visual.png' });
    console.log('ERRORS:', JSON.stringify(errors));
    console.log('title:', await page.title());
    await browser.close();
  } finally {
    if (process.platform === 'win32') {
      spawn('taskkill', ['/pid', host.pid.toString(), '/f', '/t']);
    } else {
      host.kill('SIGKILL');
    }
  }
})().catch(e => { console.error('FAILED', e); process.exit(1); });
'@
Set-Content -Path "shot.js" -Value $screenshot
node shot.js 2>&1 | Select-Object -Last 10
Remove-Item shot.js
```

Key points in the corrected script:
- ESM `import` syntax (compatible with `"type": "module"`)
- No `{ shell: true }` — `spawn` manages the `dotnet` PID directly
- Windows cleanup: `taskkill /pid /f /t` terminates the entire process tree
- No `cwd` override — `--project` path resolves correctly from the default working directory
- HTTP polling replaces static `sleep()` — script proceeds as soon as the server is ready

## Agent workflow for UI testing (skills + playwright-cli)

For exploratory/agentic browser work (not the checked-in suite), use the `playwright-cli` skill (`.agents/skills/playwright-cli/`) — interactive session commands (`open`, `snapshot`, `click`, `eval`, …), run against the dev host:
```bash
# terminal 1: host
dotnet watch --project src/Game.Web
# terminal 2: agent drives installed Chrome
playwright-cli open http://localhost:5902/hello --browser=chrome
playwright-cli snapshot
```

For assertions meant to live on, write them as specs in `src/Game.Tests.UI/tests/*.spec.ts` and run `npx playwright test` — deterministic, replayable, CI-able. Use playwright-cli for exploration and debugging only.

## Verdict: Playwright MCP server — NOT needed

Reviewed against the alternative of running the `playwright` MCP server. **Skills + playwright-cli is sufficient for this repo.** Rationale:

- CLI + skill invocations are token-efficient (no large tool schemas / verbose accessibility trees resident in context); fits coding-agent loops that must also carry a large codebase.
- Our needs are: (a) replayable assertions → covered by the Playwright test suite; (b) exploratory debugging → covered by playwright-cli sessions with `--browser=chrome`.
- MCP remains relevant for long-running autonomous loops needing persistent browser context and rich page introspection (self-healing tests, exploratory automation). Revisit only if agent-driven, stateful multi-page workflows become routine.

---

UI testing capabilities in Playwright, there are a few primary names depending on which tool or mode is being used:
Playwright UI Mode: The primary built-in graphical user interface used to run, explore, and debug tests interactively. It features time-travel debugging, DOM inspection, live action tracing, and watch mode. You launch it via npx playwright test --ui.Trace Viewer: The visual GUI dedicated to inspecting generated test traces after a test run. It provides a full timeline view, network logs, console logs, and action-by-action DOM snapshots.Codegen (Playwright Test Generator): The interactive recorder tool used to generate test scripts automatically by recording user clicks and typing inside a browser window.Component Testing: Playwright Component Testing (@playwright/experimental-ct-*) is used specifically for isolated UI component testing for frameworks like React, Vue, Svelte, and Solid.Visual Comparisons: Snapshot / Visual Regression Testing is the technique used within Playwright to compare UI screenshots against baseline reference images (expect(page).toHaveScreenshot()).

## playwright-cli

Playwright CLI with SKILLS

### Playwright CLI vs Playwright MCP

- **CLI**: Modern **coding agents** increasingly favor CLI–based workflows exposed as SKILLs over MCP because CLI invocations are more token-efficient: they avoid loading large tool schemas and verbose accessibility trees into the model context, allowing agents to act through concise, purpose-built commands. This makes CLI + SKILLs better suited for high-throughput coding agents that must balance browser automation with large codebases, tests, and reasoning within limited context windows.

- **MCP**: MCP remains relevant for specialized agentic loops that benefit from persistent state, rich introspection, and iterative reasoning over page structure, such as exploratory automation, self-healing tests, or long-running autonomous workflows where maintaining continuous browser context outweighs token cost concerns. Learn more about [Playwright MCP](https://github.com/microsoft/playwright-mcp).

## Getting Started

## Commands Help

```bash
playwright-cli --help
```

### Installing skills

```bash
playwright-cli install --skills=agents
```

### Skills-less operation

Point your agent at the CLI and let it cook. It'll read the skill off `playwright-cli --help` on its own:

```
Test the "add todo" flow on https://demo.playwright.dev/todomvc using playwright-cli.
Check playwright-cli --help for available commands.
```

## Demo

```
> Use playwright skills to test https://demo.playwright.dev/todomvc/.
  Take screenshots for all successful and failing scenarios.
```

Your agent will be running commands, but it does not mean you can't play with it manually:

```
playwright-cli open https://demo.playwright.dev/todomvc/ --headed
playwright-cli type "Buy groceries"
playwright-cli press Enter
playwright-cli type "Water flowers"
playwright-cli press Enter
playwright-cli check e21
playwright-cli check e35
playwright-cli screenshot
```

## Headed operation

Playwright CLI is headless by default. If you'd like to see the browser, pass `--headed` to `open`:

```bash
playwright-cli open https://playwright.dev --headed
```

## Sessions

Playwright CLI keeps the browser profile in memory by default. Your cookies and storage state
are preserved between CLI calls within the session, but lost when the browser closes. Use
`--persistent` to save the profile to disk for persistence across browser restarts.

You can use different instances of the browser for different projects with sessions. Pass `-s=` to
the invocation to talk to a specific browser.

```bash
playwright-cli open https://playwright.dev
playwright-cli -s=example open https://example.com --persistent
playwright-cli list
```

You can run your coding agent with the `PLAYWRIGHT_CLI_SESSION` environment variable:

```bash
PLAYWRIGHT_CLI_SESSION=todo-app claude .
```

Or instruct it to prepend `-s=` to the calls.

Manage your sessions as follows:

```bash
playwright-cli list                     # list all sessions
playwright-cli close-all                # close all browsers
playwright-cli kill-all                 # forcefully kill all browser processes
```

## Monitoring

Use `playwright-cli show` to open a visual dashboard that lets you see and control all running
browser sessions. This is useful when your coding agents are running browser automation in the
background and you want to observe their progress or step in to help.

```bash
playwright-cli show
```

<img width="1107" height="729" alt="Image" src="https://github.com/user-attachments/assets/99df739d-106a-4520-b004-bb315db41da7" />

The dashboard opens a window with two views:

- **Session grid** — shows all active sessions grouped by workspace, each with a live screencast
  preview, session name, current URL, and page title. Click any session to zoom in.
- **Session detail** — shows a live view of the selected session with a tab bar, navigation
  controls (back, forward, reload, address bar), and full remote control. Click into the viewport
  to take over mouse and keyboard input; press Escape to release.

From the grid you can also close running sessions or delete data for inactive ones.

## Commands

### Core

```bash
playwright-cli open [url]               # open browser, optionally navigate to url
playwright-cli goto <url>               # navigate to a url
playwright-cli close                    # close the page
playwright-cli type <text>              # type text into editable element
playwright-cli click <ref> [button]     # perform click on a web page
playwright-cli dblclick <ref> [button]  # perform double click on a web page
playwright-cli fill <ref> <text>        # fill text into editable element
playwright-cli fill <ref> <text> --submit # fill and press Enter
playwright-cli drag <startRef> <endRef> # perform drag and drop between two elements
playwright-cli drop <ref> --path=<file> # drop files onto an element (from outside the page)
playwright-cli drop <ref> --data="k=v"  # drop data onto an element
playwright-cli hover <ref>              # hover over element on page
playwright-cli select <ref> <val>       # select an option in a dropdown
playwright-cli upload <file>            # upload one or multiple files
playwright-cli check <ref>              # check a checkbox or radio button
playwright-cli uncheck <ref>            # uncheck a checkbox or radio button
playwright-cli snapshot                 # capture page snapshot to obtain element ref
playwright-cli snapshot --filename=f    # save snapshot to specific file
playwright-cli snapshot <ref>           # snapshot a specific element
playwright-cli snapshot --depth=N       # limit snapshot depth for efficiency
playwright-cli find <text>              # search the snapshot for text, returns matching nodes
playwright-cli find --regex <pattern>   # search the snapshot with a regexp
playwright-cli eval <func> [ref]        # evaluate javascript expression on page or element
playwright-cli dialog-accept [prompt]   # accept a dialog
playwright-cli dialog-dismiss           # dismiss a dialog
playwright-cli resize <w> <h>           # resize the browser window
```

### Navigation

```bash
playwright-cli go-back                  # go back to the previous page
playwright-cli go-forward               # go forward to the next page
playwright-cli reload                   # reload the current page
```

### Keyboard

```bash
playwright-cli press <key>              # press a key on the keyboard, `a`, `arrowleft`
playwright-cli keydown <key>            # press a key down on the keyboard
playwright-cli keyup <key>              # press a key up on the keyboard
```

### Mouse

```bash
playwright-cli mousemove <x> <y>        # move mouse to a given position
playwright-cli mousedown [button]       # press mouse down
playwright-cli mouseup [button]         # press mouse up
playwright-cli mousewheel <dx> <dy>     # scroll mouse wheel
```

### Save as

```bash
playwright-cli screenshot [ref]         # screenshot of the current page or element
playwright-cli screenshot --filename=f  # save screenshot with specific filename
playwright-cli screenshot --hires       # capture at full device pixel ratio
playwright-cli pdf                      # save page as pdf
playwright-cli pdf --filename=page.pdf  # save pdf with specific filename
```

### Tabs

```bash
playwright-cli tab-list                 # list all tabs
playwright-cli tab-new [url]            # create a new tab
playwright-cli tab-close [index]        # close a browser tab
playwright-cli tab-select <index>       # select a browser tab
```

### Storage

```bash
playwright-cli state-save [filename]    # save storage state
playwright-cli state-load <filename>    # load storage state

# Cookies
playwright-cli cookie-list [--domain]   # list cookies
playwright-cli cookie-get <name>        # get a cookie
playwright-cli cookie-set <name> <val>  # set a cookie
playwright-cli cookie-delete <name>     # delete a cookie
playwright-cli cookie-clear             # clear all cookies

# LocalStorage
playwright-cli localstorage-list        # list localStorage entries
playwright-cli localstorage-get <key>   # get localStorage value
playwright-cli localstorage-set <k> <v> # set localStorage value
playwright-cli localstorage-delete <k>  # delete localStorage entry
playwright-cli localstorage-clear       # clear all localStorage

# SessionStorage
playwright-cli sessionstorage-list      # list sessionStorage entries
playwright-cli sessionstorage-get <k>   # get sessionStorage value
playwright-cli sessionstorage-set <k> <v> # set sessionStorage value
playwright-cli sessionstorage-delete <k>  # delete sessionStorage entry
playwright-cli sessionstorage-clear     # clear all sessionStorage
```

### Network

```bash
playwright-cli route <pattern> [opts]   # mock network requests
playwright-cli route-list               # list active routes
playwright-cli unroute [pattern]        # remove route(s)
```

### DevTools

```bash
playwright-cli console [min-level]      # list console messages
playwright-cli requests                 # list all network requests since loading the page
playwright-cli request <index>          # show details for a specific request
playwright-cli run-code <code>          # run playwright code snippet
playwright-cli run-code --filename=f    # run playwright code from a file
playwright-cli tracing-start            # start trace recording
playwright-cli tracing-stop             # stop trace recording
playwright-cli video-start [filename]   # start video recording
playwright-cli video-chapter <title>    # add a chapter marker to the video
playwright-cli video-show-actions       # annotate each action with a callout in the video
playwright-cli video-hide-actions       # stop annotating actions in the video
playwright-cli video-stop               # stop video recording
playwright-cli show                     # open the visual dashboard
playwright-cli show --annotate          # launch dashboard for UI review / design feedback
playwright-cli generate-locator <ref>   # generate a playwright locator for an element
playwright-cli highlight <ref>          # show a persistent highlight overlay
playwright-cli highlight <ref> --style= # highlight with a custom CSS style
playwright-cli highlight <ref> --hide   # hide highlight on a specific element
playwright-cli highlight --hide         # hide all page highlights
```

### Open parameters

```bash
playwright-cli open --browser=chrome    # use specific browser
playwright-cli open --mobile            # emulate a generic mobile device
playwright-cli open --device="iPhone 15" # emulate a specific device
playwright-cli attach --extension=chrome # connect via Playwright Extension
playwright-cli attach --cdp=chrome      # attach to running Chrome/Edge by channel
playwright-cli attach --cdp=<url>       # attach via CDP endpoint
playwright-cli detach                   # detach an attached session, leaves the external browser running
playwright-cli open --persistent        # use persistent profile
playwright-cli open --profile=<path>    # use custom profile directory
playwright-cli open --config=file.json  # use config file
playwright-cli close                    # close the browser
playwright-cli delete-data              # delete user data for default session
```

### Snapshots

After each command, playwright-cli provides a snapshot of the current browser state.

```bash
> playwright-cli goto https://example.com
### Page
- Page URL: https://example.com/
- Page Title: Example Domain
### Snapshot
[Snapshot](.playwright-cli/page-2026-02-14T19-22-42-679Z.yml)
```

You can also take a snapshot on demand using `playwright-cli snapshot` command. All the options below can be combined as needed.

```bash
# default - save to a file with timestamp-based name
playwright-cli snapshot

# save to file, use when snapshot is a part of the workflow result
playwright-cli snapshot --filename=after-click.yaml

# snapshot an element instead of the whole page
playwright-cli snapshot "#main"

# limit snapshot depth for efficiency, take a partial snapshot afterwards
playwright-cli snapshot --depth=4
playwright-cli snapshot e34

# include each element's bounding box as [box=x,y,width,height]
playwright-cli snapshot --boxes

# search a large snapshot instead of capturing it all — returns matching nodes
# with 3 lines of context around each match (like grep -C)
playwright-cli find "Add to cart"
playwright-cli find --regex "\\$[0-9]+\\.[0-9]{2}"
# wrap the regexp in slashes to add flags, e.g. /i for case-insensitive
playwright-cli find --regex "/sign (in|up)/i"
```

### Targeting elements

By default, use refs from the snapshot to interact with page elements.

```bash
# get snapshot with refs
playwright-cli snapshot

# interact using a ref
playwright-cli click e15
```

You can also use css selectors or Playwright locators.

```bash
# css selector
playwright-cli click "#main > button.submit"

# role locator
playwright-cli click "getByRole('button', { name: 'Submit' })"

# test id
playwright-cli click "getByTestId('submit-button')"
```

### Sessions

```bash
playwright-cli -s=name <cmd>            # run command in named session
playwright-cli -s=name close            # stop a named browser
playwright-cli -s=name delete-data      # delete user data for named browser
playwright-cli list                     # list all sessions
playwright-cli close-all                # close all browsers
playwright-cli kill-all                 # forcefully kill all browser processes
```

### Local installation

If global `playwright-cli` command is not available, try a local version via `npx playwright cli`:

```bash
npx --no-install playwright --version
```

When local version is available, use `npx playwright cli` in all commands. Otherwise, install `playwright-cli` as a global command:

```bash
npm install -g @playwright/cli@latest
```

## Configuration file

The Playwright CLI can be configured using a JSON configuration file. You can specify the configuration file using the `--config` command line option:

```bash
playwright-cli --config path/to/config.json open example.com
```

Playwright CLI will load config from `.playwright/cli.config.json` by default so that you did not need to specify it every time.

<details>
<summary>Configuration file schema</summary>

```typescript
{
  /**
   * The browser to use.
   */
  browser?: {
    /**
     * The type of browser to use.
     */
    browserName?: 'chromium' | 'firefox' | 'webkit';

    /**
     * Keep the browser profile in memory, do not save it to disk.
     */
    isolated?: boolean;

    /**
     * Path to a user data directory for browser profile persistence.
     * Temporary directory is created by default.
     */
    userDataDir?: string;

    /**
     * Launch options passed to
     * @see https://playwright.dev/docs/api/class-browsertype#browser-type-launch-persistent-context
     *
     * This is useful for settings options like `channel`, `headless`, `executablePath`, etc.
     */
    launchOptions?: playwright.LaunchOptions;

    /**
     * Context options for the browser context.
     *
     * This is useful for settings options like `viewport`.
     */
    contextOptions?: playwright.BrowserContextOptions;

    /**
     * Chrome DevTools Protocol endpoint to connect to an existing browser instance in case of Chromium family browsers.
     */
    cdpEndpoint?: string;

    /**
     * CDP headers to send with the connect request.
     */
    cdpHeaders?: Record<string, string>;

    /**
     * Timeout in milliseconds for connecting to CDP endpoint. Defaults to 30000 (30 seconds). Pass 0 to disable timeout.
     */
    cdpTimeout?: number;

    /**
     * Remote endpoint to connect to an existing Playwright server.
     */
    remoteEndpoint?: string;

    /**
     * Paths to TypeScript files to add as initialization scripts for Playwright page.
     */
    initPage?: string[];

    /**
     * Paths to JavaScript files to add as initialization scripts.
     * The scripts will be evaluated in every page before any of the page's scripts.
     */
    initScript?: string[];
  },

  /**
   * If specified, saves the Playwright video of the session into the output directory.
   */
  saveVideo?: {
    width: number;
    height: number;
  };

  /**
   * The directory to save output files.
   */
  outputDir?: string;

  /**
   * Whether to save snapshots, console messages, network logs and other session logs to a file or to the standard output. Defaults to "stdout".
   */
  outputMode?: 'file' | 'stdout';

  console?: {
    /**
     * The level of console messages to return. Each level includes the messages of more severe levels. Defaults to "info".
     */
    level?: 'error' | 'warning' | 'info' | 'debug';
  },

  network?: {
    /**
     * List of origins to allow the browser to request. Default is to allow all. Origins matching both `allowedOrigins` and `blockedOrigins` will be blocked.
     */
    allowedOrigins?: string[];

    /**
     * List of origins to block the browser to request. Origins matching both `allowedOrigins` and `blockedOrigins` will be blocked.
     */
    blockedOrigins?: string[];
  };

  /**
   * Specify the attribute to use for test ids, defaults to "data-testid".
   */
  testIdAttribute?: string;

  timeouts?: {
    /*
     * Configures default action timeout: https://playwright.dev/docs/api/class-page#page-set-default-timeout. Defaults to 5000ms.
     */
    action?: number;

    /*
     * Configures default navigation timeout: https://playwright.dev/docs/api/class-page#page-set-default-navigation-timeout. Defaults to 60000ms.
     */
    navigation?: number;
  };

  /**
   * Whether to allow file uploads from anywhere on the file system.
   * By default (false), file uploads are restricted to paths within the MCP roots only.
   */
  allowUnrestrictedFileAccess?: boolean;

  /**
   * Specify the language to use for code generation.
   */
  codegen?: 'typescript' | 'none';
}
```

</details>

<details>
<summary>Configuration via env</summary>

| Environment |
|-------------|
| `PLAYWRIGHT_MCP_ALLOWED_HOSTS` comma-separated list of hosts this server is allowed to serve from. Defaults to the host the server is bound to. Pass '*' to disable the host check. |
| `PLAYWRIGHT_MCP_ALLOWED_ORIGINS` semicolon-separated list of TRUSTED origins to allow the browser to request. Default is to allow all. Important: *does not* serve as a security boundary and *does not* affect redirects. |
| `PLAYWRIGHT_MCP_ALLOW_UNRESTRICTED_FILE_ACCESS` allow access to files outside of the workspace roots. Also allows unrestricted access to file:// URLs. By default access to file system is restricted to workspace root directories (or cwd if no roots are configured) only, and navigation to file:// URLs is blocked. |
| `PLAYWRIGHT_MCP_BLOCKED_ORIGINS` semicolon-separated list of origins to block the browser from requesting. Blocklist is evaluated before allowlist. If used without the allowlist, requests not matching the blocklist are still allowed. Important: *does not* serve as a security boundary and *does not* affect redirects. |
| `PLAYWRIGHT_MCP_BLOCK_SERVICE_WORKERS` block service workers |
| `PLAYWRIGHT_MCP_BROWSER` browser or chrome channel to use, possible values: chrome, firefox, webkit, msedge. |
| `PLAYWRIGHT_MCP_CAPS` comma-separated list of additional capabilities to enable, possible values: vision, pdf. |
| `PLAYWRIGHT_MCP_CDP_ENDPOINT` CDP endpoint to connect to. |
| `PLAYWRIGHT_MCP_CDP_HEADERS` CDP headers to send with the connect request, multiple can be specified. |
| `PLAYWRIGHT_MCP_CDP_TIMEOUT` timeout for the CDP connection. |
| `PLAYWRIGHT_MCP_CONFIG` path to the configuration file. |
| `PLAYWRIGHT_MCP_CONSOLE_LEVEL` level of console messages to return: "error", "warning", "info", "debug". Each level includes the messages of more severe levels. |
| `PLAYWRIGHT_MCP_DEVICE` device to emulate, for example: "iPhone 15" |
| `PLAYWRIGHT_MCP_EXECUTABLE_PATH` path to the browser executable. |
| `PLAYWRIGHT_MCP_EXTENSION` Connect to a running browser instance (Edge/Chrome only). Requires the "Playwright MCP Bridge" browser extension to be installed. |
| `PLAYWRIGHT_MCP_GRANT_PERMISSIONS` List of permissions to grant to the browser context, for example "geolocation", "clipboard-read", "clipboard-write". |
| `PLAYWRIGHT_MCP_HEADLESS` whether to run browser in headless mode, headless by default. |
| `PLAYWRIGHT_MCP_IGNORE_HTTPS_ERRORS` ignore https errors |
| `PLAYWRIGHT_MCP_INIT_PAGE` path to TypeScript file to evaluate on Playwright page object |
| `PLAYWRIGHT_MCP_INIT_SCRIPT` path to JavaScript file to add as an initialization script. The script will be evaluated in every page before any of the page's scripts. Can be specified multiple times. |
| `PLAYWRIGHT_MCP_ISOLATED` keep the browser profile in memory, do not save it to disk. |
| `PLAYWRIGHT_MCP_SANDBOX` whether to enable the browser sandbox. |
| `PLAYWRIGHT_MCP_OUTPUT_DIR` path to the directory for output files. |
| `PLAYWRIGHT_MCP_PROXY_BYPASS` comma-separated domains to bypass proxy, for example ".com,chromium.org,.domain.com" |
| `PLAYWRIGHT_MCP_PROXY_SERVER` specify proxy server, for example "http://myproxy:3128" or "socks5://myproxy:8080" |
| `PLAYWRIGHT_MCP_SAVE_TRACE` Whether to save the Playwright Trace of the session into the output directory. |
| `PLAYWRIGHT_MCP_SAVE_VIDEO` Whether to save the video of the session into the output directory. For example "--save-video=800x600" |
| `PLAYWRIGHT_MCP_SECRETS_FILE` path to a file containing secrets in the dotenv format |
| `PLAYWRIGHT_MCP_STORAGE_STATE` path to the storage state file for isolated sessions. |
| `PLAYWRIGHT_MCP_TEST_ID_ATTRIBUTE` specify the attribute to use for test ids, defaults to "data-testid" |
| `PLAYWRIGHT_MCP_TIMEOUT_ACTION` specify action timeout in milliseconds, defaults to 5000ms |
| `PLAYWRIGHT_MCP_TIMEOUT_NAVIGATION` specify navigation timeout in milliseconds, defaults to 60000ms |
| `PLAYWRIGHT_MCP_USER_AGENT` specify user agent string |
| `PLAYWRIGHT_MCP_USER_DATA_DIR` path to the user data directory. If not specified, a temporary directory will be created. |
| `PLAYWRIGHT_MCP_VIEWPORT_SIZE` specify browser viewport size in pixels, for example "1280x720" |
</details>

## Specific tasks

The installed skill includes detailed reference guides for common tasks:

* **Running and Debugging Playwright tests** — run, debug and manage Playwright test suites
* **Request mocking** — intercept and mock network requests
* **Running Playwright code** — execute arbitrary Playwright scripts
* **Browser session management** — manage multiple browser sessions
* **Storage state (cookies, localStorage)** — persist and restore browser state
* **Test generation (plan / generate / heal)** — generate Playwright tests from a spec or interactions
* **Tracing** — record and inspect execution traces
* **Video recording** — capture browser session videos
* **Inspecting element attributes** — get element id, class, or any attribute not visible in the snapshot

# Running Custom Playwright Code

Use `run-code` to execute arbitrary Playwright code for advanced scenarios not covered by CLI commands.

## Syntax

```bash
playwright-cli run-code "async page => {
  // Your Playwright code here
  // Access page.context() for browser context operations
}"
```

You can also load the function from a file:

```bash
playwright-cli run-code --filename=./my-script.js
```


The code must be a single function expression, it is wrapped in `(...)` and evaluated.
import/export/require syntax is not supported.

## Geolocation

```bash
# Grant geolocation permission and set location
playwright-cli run-code "async page => {
  await page.context().grantPermissions(['geolocation']);
  await page.context().setGeolocation({ latitude: 37.7749, longitude: -122.4194 });
}"

# Set location to London
playwright-cli run-code "async page => {
  await page.context().grantPermissions(['geolocation']);
  await page.context().setGeolocation({ latitude: 51.5074, longitude: -0.1278 });
}"

# Clear geolocation override
playwright-cli run-code "async page => {
  await page.context().clearPermissions();
}"
```

## Permissions

```bash
# Grant multiple permissions
playwright-cli run-code "async page => {
  await page.context().grantPermissions([
    'geolocation',
    'notifications',
    'camera',
    'microphone'
  ]);
}"

# Grant permissions for specific origin
playwright-cli run-code "async page => {
  await page.context().grantPermissions(['clipboard-read'], {
    origin: 'https://example.com'
  });
}"
```

## Media Emulation

```bash
# Emulate dark color scheme
playwright-cli run-code "async page => {
  await page.emulateMedia({ colorScheme: 'dark' });
}"

# Emulate light color scheme
playwright-cli run-code "async page => {
  await page.emulateMedia({ colorScheme: 'light' });
}"

# Emulate reduced motion
playwright-cli run-code "async page => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
}"

# Emulate print media
playwright-cli run-code "async page => {
  await page.emulateMedia({ media: 'print' });
}"
```

## Wait Strategies

```bash
# Wait for network idle
playwright-cli run-code "async page => {
  await page.waitForLoadState('networkidle');
}"

# Wait for specific element
playwright-cli run-code "async page => {
  await page.locator('.loading').waitFor({ state: 'hidden' });
}"

# Wait for function to return true
playwright-cli run-code "async page => {
  await page.waitForFunction(() => window.appReady === true);
}"

# Wait with timeout
playwright-cli run-code "async page => {
  await page.locator('.result').waitFor({ timeout: 10000 });
}"
```

## Frames and Iframes

```bash
# Work with iframe
playwright-cli run-code "async page => {
  const frame = page.locator('iframe#my-iframe').contentFrame();
  await frame.locator('button').click();
}"

# Get all frames
playwright-cli run-code "async page => {
  const frames = page.frames();
  return frames.map(f => f.url());
}"
```

## File Downloads

```bash
# Handle file download
playwright-cli run-code "async page => {
  const downloadPromise = page.waitForEvent('download');
  await page.getByRole('link', { name: 'Download' }).click();
  const download = await downloadPromise;
  await download.saveAs('./downloaded-file.pdf');
  return download.suggestedFilename();
}"
```

## Clipboard

```bash
# Read clipboard (requires permission)
playwright-cli run-code "async page => {
  await page.context().grantPermissions(['clipboard-read']);
  return await page.evaluate(() => navigator.clipboard.readText());
}"

# Write to clipboard
playwright-cli run-code "async page => {
  await page.evaluate(text => navigator.clipboard.writeText(text), 'Hello clipboard!');
}"
```

## Page Information

```bash
# Get page title
playwright-cli run-code "async page => {
  return await page.title();
}"

# Get current URL
playwright-cli run-code "async page => {
  return page.url();
}"

# Get page content
playwright-cli run-code "async page => {
  return await page.content();
}"

# Get viewport size
playwright-cli run-code "async page => {
  return page.viewportSize();
}"
```

## JavaScript Execution

```bash
# Execute JavaScript and return result
playwright-cli run-code "async page => {
  return await page.evaluate(() => {
    return {
      userAgent: navigator.userAgent,
      language: navigator.language,
      cookiesEnabled: navigator.cookieEnabled
    };
  });
}"

# Pass arguments to evaluate
playwright-cli run-code "async page => {
  const multiplier = 5;
  return await page.evaluate(m => document.querySelectorAll('li').length * m, multiplier);
}"
```

## Error Handling

```bash
# Try-catch in run-code
playwright-cli run-code "async page => {
  try {
    await page.getByRole('button', { name: 'Submit' }).click({ timeout: 1000 });
    return 'clicked';
  } catch (e) {
    return 'element not found';
  }
}"
```

## Complex Workflows

```bash
# Login and save state
playwright-cli run-code "async page => {
  await page.goto('https://example.com/login');
  await page.getByRole('textbox', { name: 'Email' }).fill('user@example.com');
  await page.getByRole('textbox', { name: 'Password' }).fill('secret');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await page.waitForURL('**/dashboard');
  await page.context().storageState({ path: 'auth.json' });
  return 'Login successful';
}"

# Scrape data from multiple pages
playwright-cli run-code "async page => {
  const results = [];
  for (let i = 1; i <= 3; i++) {
    await page.goto(\`https://example.com/page/\${i}\`);
    const items = await page.locator('.item').allTextContents();
    results.push(...items);
  }
  return results;
}"
```

## Standalone Scripts in ESM Context (Bonobo Repo)

When writing a standalone Node.js `.js` script inside `src/Game.Tests.UI/`, you MUST use ESM `import` syntax. The directory's `package.json` declares `"type": "module"`, so `.js` files parse as ECMAScript Modules where `require()` is not defined.

**Resolution options:**
| Strategy | Extension | Syntax | Notes |
| --- | --- | --- | --- |
| **ESM Native** | `.js` | `import` | Aligns with `"type": "module"`; use dynamic `import()` inside closures |
| **CommonJS Override** | `.cjs` | `require()` | Zero refactoring; explicit extension in an ESM-first codebase |
| **Synthetic Bridge** | `.js` | `createRequire` | `import { createRequire } from 'module'` boilerplate |

### Process Lifecycle on Windows

Never use `{ shell: true }` with `child_process.spawn` on Windows — it wraps the command in `cmd.exe` and `host.kill()` only terminates the shell, leaving the child `.NET` process alive and locking the port.

**Correct cleanup:**
```ts
import { spawn } from 'child_process';
// ... spawn without { shell: true }
const host = spawn('dotnet', ['run', '--project', '../../src/Game.Web']);
// In finally:
if (process.platform === 'win32') {
  spawn('taskkill', ['/pid', host.pid.toString(), '/f', '/t']);
} else {
  host.kill('SIGKILL');
}
```

### Path Resolution

Do NOT specify both `cwd` and `--project` with relative paths — `dotnet` resolves `--project` relative to `cwd`, doubling the traversal. Use `--project` alone from the default working directory.

### HTTP Readiness Polling

Replace `await wait(20000)` / `setTimeout` with HTTP polling to proceed as soon as the server is ready:
```ts
import http from 'http';
const pollServer = (url: string, timeoutMs = 30_000) => {
  const start = Date.now();
  return new Promise<void>((resolve, reject) => {
    const check = () => {
      http.get(url, () => resolve())
        .on('error', () => {
          if (Date.now() - start > timeoutMs) reject(new Error('Server startup timeout'));
          else setTimeout(check, 500);
        });
    };
    check();
  });
};
```

### Complete Screenshot Example (run from repo root)

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

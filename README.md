# Bonobo blazorwasm pixijs for games
.NET MAUI Blazor Hybrid. This template configures a monorepo workspace containing your pure C# Engine, the shared Razor Class Library (RCL), the .NET MAUI Hybrid client, the Blazor Web App, and a TypeScript-driven PixiJS build managed by Vite and Tailwind CLI.

Computing your entire game logic inside C#. It allows you to build a single, authoritative simulation engine that runs client-side inside .NET MAUI or Blazor today, and can be dropped directly onto a dedicated .NET Linux server tomorrow for authoritative multiplayer.
To make this architecture work without destroying performance, you must isolate the Simulation Layer (C#) from the Presentation Layer (PixiJS/Tailwind).

Here is the architectural blueprint to achieve this zero-duplicate-work setup.

🧱 The Authoritative C# ArchitectureTo ensure your C# code can run both on the client (MVP) and the server (Future), your core logic must have zero dependencies on UI libraries, Maui, or Blazor.

You should split your codebase into three distinct layers:

+---------------------------------------------------------------------------------+
|                         1. SHARED CORE ENGINE (C# Class Library)                |
|  - Holds Game State (Entities, Grid, Stats)     - Runs Simulation Ticks         |
|  - Command/Event System (Inputs, Actions)      - Pure C# Logic (AOT Friendly)   |
+---------------------------------------------------------------------------------+
                                       |
                   +-------------------+-------------------+
                   |                                       |
+--------------------------------------+   +--------------------------------------+
|       2. PRESENTATION BRIDGE         |   |       3. FUTURE SERVER HOSTER        |
|  - Blazor Component Shell            |   |  - ASP.NET Core Minimal API / WebSockets
|  - IJSRuntime Skimmed Bridge         |   |  - Runs the exact same Core Engine   |
|  - Maps C# State changes to PixiJS   |   |  - Verifies incoming client commands |
+--------------------------------------+   +--------------------------------------+

1. The Core Simulation Engine (Pure C#)This is a standard .NET Class Library. It knows absolutely nothing about graphics, rendering, or browsers.
State Management: It manages coordinates, stats, pathfinding matrices, and entity maps.
The Deterministic Tick: It processes input via a command pattern (e.g., ExecuteCommand(PlayerMoveCommand)), updates the state, and fires off highly optimized, lightweight C# events detailing what changed (e.g., OnEntityMoved(Id, NewX, NewY)).
2. The Presentation Layer (Blazor + PixiJS + Tailwind)This layer acts purely as a mirror of your C# state.
Tailwind UI: Blazor hooks into your C# state to display inventories or menus using standard data-binding.
PixiJS Canvas: Instead of polling C# for positions 60 times a second, PixiJS sits completely idle until the C# Core Engine fires a state change event. When an event fires, Blazor forwards a highly optimized, flat payload to PixiJS via IJSRuntime to animate the sprite.
⚠️ The Performance Gold Rule: Avoid JSON SerializationCalling IJSRuntime every single frame to pass a massive C# state tree to JavaScript will reduce your game's frame rate down to single digits. 
You must use a Push-Based Delta Event approach.
❌ Bad (Polling): PixiJS loops at 60fps and calls C# via Interop: "Where is everyone right now?" C# serializes 500 characters into JSON and passes it back.
✅ Good (Reactive Delta Push): The C# engine finishes a tick. Only two monsters moved. C# calls a single JavaScript function: DotNet.invokeMethod('UpdatePositions', [ { id: 4, x: 120, y: 80 }, { id: 9, x: 200, y: 300 } ]). PixiJS updates just those two sprites.
2.1. UI: Keep your presentation layer thin. Use Razor components and Tailwind CSS for your menus, inventories, and HUD. Drop a standard HTML5 <canvas> inside that Razor view, and use a modular, object-oriented vanilla TypeScript/JavaScript file to initialize PixiJS and map incoming C# events directly to sprites. Bypassing the React wrapper keeps your application simple, clean, and fast.

🛠️ Step-by-Step Blueprint for the MVP

Step 1: Define Your Command and Event SystemCreate a message-based communication boundary. This ensures your network transition later is as simple as routing these messages over a WebSocket connection instead of a local memory bridge.

// The single source of truth contract

public record EntityMovedEvent(int EntityId, float X, float Y);

public class GameSimulation
{
    public Dictionary<int, GameEntity> Entities { get; } = new();
    
    // UI or Network triggers this
    public void ProcessCommand(MoveCommand cmd)
    {
        var entity = Entities[cmd.EntityId];
        entity.X = cmd.TargetX;
        entity.Y = cmd.TargetY;
        
        // Notify the rendering layers
        OnEntityMoved?.Invoke(new EntityMovedEvent(entity.Id, entity.X, entity.Y));
    }
    
    public event Action<EntityMovedEvent> OnEntityMoved;
}

Step 2: The Skinny Blazor-to-PixiJS BridgeIn your Shared Razor Component, subscribe to your C# engine events. When an event triggers, push the raw data directly down to PixiJS.csharp

@inject IJSRuntime JS
@implements IDisposable

<div class="relative w-full h-screen">
    <!-- Tailwind HUD Layer -->
    <div class="absolute top-4 left-4 bg-slate-900/80 p-4 rounded text-white">
        Health: @PlayerHealth
    </div>

    <!-- PixiJS Canvas Holder -->
    <div id="pixi-container" class="w-full h-full"></div>
</div>

@code {
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            // Initialize PixiJS canvas locally
            await JS.InvokeVoidAsync("initPixi", "pixi-container");
            
            // Subscribe to authoritative C# state changes
            CoreEngine.OnEntityMoved += HandleEntityMoved;
        }
    }

    private async void HandleEntityMoved(EntityMovedEvent ev)
    {
        // Push only the delta change down to the Pixi rendering layer
        await JS.InvokeVoidAsync("pixiEngine.moveSprite", ev.EntityId, ev.X, ev.Y);
    }

    public void Dispose() => CoreEngine.OnEntityMoved -= HandleEntityMoved;
}

Step 3: Fast JavaScript PixiJS MirrorKeep your JavaScript thin. It shouldn't process game rules, combat math, or boundary checks; it should only load textures and move visual assets based on instructions from C#.

javascriptwindow.pixiEngine = {
    app: null,
    sprites: new Map(),

    initPixi: function (containerId) {
        const container = document.getElementById(containerId);
        this.app = new PIXI.Application({ resizeTo: container });
        container.appendChild(this.app.view);
    },

    moveSprite: function (id, x, y) {
        const sprite = this.sprites.get(id);
        if (sprite) {
            // Animate or teleport to the authoritative coordinates
            sprite.x = x;
            sprite.y = y;
        }
    }
};

🚀 Future-Proofing for Authoritative Multiplayer By designing your MVP this way, moving to a multiplayer model becomes a structural drop-in change:
You pluck your Shared Core Engine project out of the client build and compile it into a headless ASP.NET Core console application hosted on Linux.Instead of your client UI executing commands directly against a local GameSimulation instance, your client UI serializes the MoveCommand and shoots it over a SignalR or WebSocket connection.
The server runs the command through the exact same C# simulation code, processes the ticks, and broadcasts the EntityMovedEvent across the network to all connected clients.
Your Blazor/PixiJS setup handles the network event exactly like it handled the local event during the MVP phase.

# Sourced Ecosystem Libraries & Starting Points
Architectural stack uses specialized, lightweight libraries designed for maximum performance, data serialization, and strict zero-allocation boundaries:
- Game State Engine (Arch ECS): A high-performance, ultra-lightweight C# Archetype Entity Component System. It avoids rigid class inheritance and allows you to process game world calculations (e.g., matching a parsed Town Entity to its structural Garrison Army Entities) inside structured, flat database-like chunks.
- AOT-Friendly Persistence Loop (.NET System.Text.Json Source Generators): Essential for your saving/loading mechanics. Using JsonSourceGenerationOptions forces compilation to produce specialized metadata ahead-of-time (Native AOT-safe). This ensures fast, allocation-free serialization when passing structural map files, flat JSON configs, and delta-state frames across the .NET-to-JavaScript bridge.
- Canvas & Presentation layer (PixiJS v8): The leading HTML5 2D rendering pipeline. It provides code-only WebGL/WebGPU hardware acceleration inside the native .NET MAUI BlazorWebView container, entirely bypassing heavy game engine overhead.
- UI Layout & Theme Canvas (Tailwind CSS): Handles responsive HUDs, non-overlapping contextual menus, popups, inventory windows, and system options cleanly using standard HTML/CSS.2. 

## Specialized MCP Servers & Knowledge Bases
 When working with an MCP-capable AI agent (e.g., Claude Code, Cursor, Windsurf), inject these explicit resources into its context configurations:
 - GameDev MCP Server (sbenson2): A highly specialized tool that exposes structured game design patterns, structural gamedev guides, and documentation contexts specifically mapping out MonoGame + Arch ECS cross-over ecosystems. It ensures the agent does not stray from professional game loop conventions.
 - DevFlow MCP Server (Microsoft Learn): An essential development automation kit for your agent. It connects your workspace directly into live .NET MAUI BlazorWebView components via the Chrome DevTools Protocol (CDP). This lets the agent programmatically capture screen states, inspect element clipping/overlaps, and run functional UI tests automatically.

---------------------------------------------------------------------------------------
# .NET MAUI Hybrid client, the Blazor Web App, and a TypeScript-driven PixiJS build managed by Vite and Tailwind CLI

Here is the step-by-step environment setup guide to build your cross-platform C# authoritative game stack inside Visual Studio Code.This setup configures a monorepo workspace containing your pure C# Engine, the shared Razor Class Library (RCL), the .NET MAUI Hybrid client, the Blazor Web App, and a TypeScript-driven PixiJS build managed by Vite and Tailwind CLI.

🛠️ Step 1: Install Required System DependenciesBefore opening VS Code, install the required development kits on your machine:.NET 10 SDK: Installs the core C# compilers and runtime engines.Node.js (LTS Version): Installs Node and NPM required to run Vite and Tailwind CSS.MAUI Workloads: Open your terminal/command prompt and execute the following command to download the cross-platform native SDK wrappers:

```bash
dotnet workload install maui
```

🧩 Step 2: VS Code Extensions Configuration
Open Visual Studio Code, press Ctrl+Shift+X (or Cmd+Shift+X on macOS), and install these extensions:.NET MAUI: Handles compiling, targeting, and debugging your native Windows/macOS desktop apps or iOS/Android emulators.C# Dev Kit: Implements an automated Solution Explorer window and deep IntelliSense across .NET projects.Tailwind CSS IntelliSense: Predicts and autocomplete classes inside your .razor and .html layout elements.Volar or TypeScript Language Features: Provides strict compilation mapping and autocomplete arrays for your PixiJS TypeScript code.

📁 Step 3: Project Directory StructureCreate a root folder named bonoboWebGame. We will build a unified solution mapping structure. Open this root directory inside VS Code.Your folder architecture will look like this once setup is completed:

bonoboWebGame/
├── bonoboWebGame.sln             # The main .NET Solution file
├── src/
│   ├── Game.Engine/          # Pure C# class library (Core Simulation)
│   ├── Game.UI/              # Razor Class Library (Shared HUD Views + Components)
│   │   ├── wwwroot/
│   │   │   └── dist/         # Output directory for Vite and Tailwind bundle assets
│   │   └── Frontend/         # PixiJS TypeScript engine + Tailwind CSS configurations
│   ├── Game.Maui/            # Native App Shell Wrapper
│   └── Game.Web/             # ASP.NET Blazor Server/Web Web Hoster

⚙️ Step 4: .NET Solution Architecture SetupOpen the integrated VS Code terminal (Ctrl+~) and execute these commands sequentially to scaffold the .NET projects:

# 1. Create a master solution container file 

```bash
dotnet new sln -n bonoboWeGame
```

# 2. Build the headless, pure C# Core Simulation engine project 

```bash
dotnet new classlib -o src/Game.Engine -f net10.0
```

# 3. Build the shared Razor Class Library (RCL) for shared templates and components 

```bash
dotnet new razorclasslib -o src/Game.UI -f net10.0
```

# 4. Build the .NET MAUI Blazor Hybrid Native platform engine project 

```bash
dotnet new maui-blazor -o src/Game.Maui -f net10.0
```

# 5. Build the Blazor Web App server project 

```bash
dotnet new blazor -o src/Game.Web -f net10.0
```

# 6. Bind all standalone projects inside your master solution file 

```bash
dotnet sln add src/Game.Engine/Game.Engine.csproj
dotnet sln add src/Game.UI/Game.UI.csproj
dotnet sln add src/Game.Maui/Game.Maui.csproj
dotnet sln add src/Game.Web/Game.Web.csproj
```

## Link Dependencies:Connect your dependencies so they talk to each other correctly:

### The UI project reads your C# Core Game Engine

```bash
dotnet add src/Game.UI/Game.UI.csproj reference src/Game.Engine/Game.Engine.csproj
```

### The MAUI and Web apps inherit the shared UI layouts from the RCL

```bash
dotnet add src/Game.Maui/Game.Maui.csproj reference src/Game.UI/Game.UI.csproj
dotnet add src/Game.Web/Game.Web.csproj reference src/Game.UI/Game.UI.csproj
```

⚡ Step 5: Vite, TypeScript, & PixiJS Environment Configuration

Navigate directly into your shared UI folder. We will bundle your game presentation layer here so it's compiled automatically for both your web server and native app containers.bashcd src/Game.UI

# Initialize Node ecosystem package layout

```bash
npm init -y
```

# Install Vite development tools, TypeScript dependencies, and PixiJS libraries

```bash
npm install pixi.js
npm install -D vite typescript
```

Create a specialized configuration file named vite.config.ts inside src/Game.UI/ to compile your JavaScript directly into the Blazor web asset route:

```typescript
// src/Game.UI/vite.config.ts

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
```

Create the entry point TypeScript canvas file inside

```typescript
// src/Game.UI/Frontend/game.ts

import { Application } from 'pixi.js';

export async function initGame(containerId: string) {
    const el = document.getElementById(containerId);
    if (!el) return;
    const app = new Application();
    await app.init({ resizeTo: el, backgroundAlpha: 0 });
    el.appendChild(app.canvas);
}

// Bind directly to window scope so Blazor IJSRuntime can call it
(window as any).initGame = initGame;
```

🎨 Step 6: Tailwind CSS Integration Inside src/Game.UI/, set up Tailwind CSS to inspect both your C# Razor layouts and your TypeScript files:

# Install Tailwind CSS and its CLI dependencies

```bash
npm install -D tailwindcss postcss autoprefixer
npm install tailwindcss @tailwindcss/cli 
```

Create an entry CSS stylesheet named src/Game.UI/Frontend/app.css:

```css
@import "tailwindcss";

@theme {
  /* Define custom game variables here using native CSS */
  --color-hud-bg: rgba(15, 23, 42, 0.85);
  --color-mana: #3b82f6;
  --font-game: 'Orbitron', sans-serif;
}
```

🎛️ Step 7: Automation Build Automation Script (package.json)
Open your src/Game.UI/package.json file and map these build targets to compile both your styles and canvas engine into your Blazor architecture simultaneously:

```json
{
  "name": "bonoboengine.blazorwasm",
  "version": "1.0.0",
  "description": ".NET MAUI Blazor Hybrid. This template configures a monorepo workspace containing your pure C# Engine, the shared Razor Class Library (RCL), the .NET MAUI Hybrid client, the Blazor Web App, and a TypeScript-driven PixiJS build managed by Vite and Tailwind CLI.",
  "type": "module",
  "scripts": {
    "build:js": "vite build",
    "build:css": "npx @tailwindcss/cli -i ./Frontend/app.css -o ./wwwroot/dist/app.css --minify",
    "build": "npm run build:js && npm run build:css",
    "watch:js": "vite build --watch",
    "watch:css": "npx @tailwindcss/cli -i ./Frontend/app.css -o ./wwwroot/dist/app.css --watch"n
  },
  "repository": {
    "type": "git",
    "url": "git+https://github.com/josejaviercanon/bonoboengine.blazorwasm.git"
  },
  "keywords": [],
  "bugs": {
    "url": "https://github.com/josejaviercanon/bonoboengine.blazorwasm/issues"
  },
  "homepage": "https://github.com/josejaviercanon/bonoboengine.blazorwasm#readme",
  "dependencies": {
    "pixi.js": "^8.19.0"
  },
  "devDependencies": {
    "@tailwindcss/cli": "^4.3.3",
    "autoprefixer": "^10.5.4",
    "postcss": "^8.5.26",
    "tailwindcss": "^4.3.3",
    "typescript": "^7.0.2",
    "vite": "^8.2.1"
  }
}
```

🚀 Running the AppTo develop fluidly, run two terminals side-by-side inside VS Code:
Terminal 1 (Asset Monitor): Recompiles your PixiJS engine and Tailwind sheets instantly whenever you edit lines of frontend code:

```bash
cd src/Game.UI && npm run watch:js & npm run watch:css
```

Terminal 2 (Application Compilation): Launches your interactive project. Click the "{}" .NET MAUI status tray icon in the bottom right corner of VS Code to toggle targets between standard Web browser execution, native Windows App deployment, or your connected mobile emulators. Then press F5 to compile and begin debugging.

🚀 Lauch browser:

```bash
cd src/Game.Web
dotnet watch
```bash

🖥️ Verification Result
Your console will output a local loopback URL (e.g., http://localhost:5000 or https://localhost:5001). Ctrl+Click that link to display your browser viewport panel showing your "Hello World from PixiJS!" drop-shadow string text centered on screen, complete with a clean Tailwind CSS top HUD layer.

---

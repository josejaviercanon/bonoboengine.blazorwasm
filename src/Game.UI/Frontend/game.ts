import { Application, Text, TextStyle } from 'pixi.js';

// Debug helper: every interop entry/exit point logs under one prefix so the
// whole pipeline is traceable from the browser console (F12).
const dbg = (...args: unknown[]) => console.log('[pixi-debug]', ...args);

let app: Application | null = null;
let container: HTMLElement | null = null;
let messageText: Text | null = null;

export async function initGame(containerId: string): Promise<void> {
    dbg('initGame called, containerId =', containerId);

    container = document.getElementById(containerId);
    if (!container) {
        console.error(`[pixi-debug] container '#${containerId}' NOT found in DOM`);
        return;
    }
    dbg('container found, client size =', container.clientWidth, 'x', container.clientHeight);

    // Give the browser layout engine 50ms to calculate physical dimensions
    await new Promise((resolve) => setTimeout(resolve, 50));
    dbg('layout wait done, client size now =', container.clientWidth, 'x', container.clientHeight);

    // Ensure the container actually has a height and width now
    if (container.clientWidth === 0 || container.clientHeight === 0) {
        console.warn(`[pixi-debug] PixiJS Target Container '${containerId}' has a 0px boundary size. Forcing fallback dimensions.`);
        container.style.width = "100vw";
        container.style.height = "100vh";
    }

    dbg('creating PixiJS Application');
    app = new Application();

    // Initialize with fallback bounds if resizeTo yields zero size
    await app.init({
        resizeTo: container,
        backgroundAlpha: 0,
        antialias: true,
        hello: true // Forces PixiJS to log its boot signature to the console to verify execution
    });

    dbg('app.init succeeded, canvas size =', app.canvas.width, 'x', app.canvas.height);

    container.appendChild(app.canvas);
    dbg('canvas appended to container');

    // Re-center the message whenever the window/viewport resizes
    window.addEventListener('resize', centerMessage);
}

export function renderText(message: string): void {
    dbg('renderText called, message =', JSON.stringify(message));

    if (!app || !container) {
        console.error('[pixi-debug] renderText skipped: PixiJS app or container is not initialized');
        return;
    }

    if (!messageText) {
        dbg('creating PixiJS Text object');
        const textStyle = new TextStyle({
            fontFamily: 'Arial',
            fontSize: 36,
            fontWeight: 'bold',
            fill: '#ffffff'
        });

        messageText = new Text({ text: '', style: textStyle });
        messageText.anchor.set(0.5);
        app.stage.addChild(messageText);
        dbg('Text created and added to stage');
    }

    messageText.text = message;
    dbg('text set, measured size =', messageText.width, 'x', messageText.height);

    centerMessage();
}

function centerMessage(): void {
    if (!messageText || !container) return;
    messageText.x = container.clientWidth / 2;
    messageText.y = container.clientHeight / 2;
    dbg('message centered at', messageText.x, ',', messageText.y);
}

dbg('game-bundle loaded, exposing window.initGame / window.renderText');

(window as any).initGame = initGame;
(window as any).renderText = renderText;

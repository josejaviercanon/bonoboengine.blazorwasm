import { Application, Text, TextStyle } from 'pixi.js';

export async function initGame(containerId: string): Promise<void> {
    const container = document.getElementById(containerId);
    if (!container) return;

    // Give the browser layout engine 50ms to calculate physical dimensions
    await new Promise((resolve) => setTimeout(resolve, 50));

    // Ensure the container actually has a height and width now
    if (container.clientWidth === 0 || container.clientHeight === 0) {
        console.warn(`PixiJS Target Container '${containerId}' has a 0px boundary size. Forcing fallback dimensions.`);
        container.style.width = "100vw";
        container.style.height = "100vh";
    }

    const app = new Application();
    
    // Initialize with fallback bounds if resizeTo yields zero size
    await app.init({
        resizeTo: container,
        backgroundAlpha: 0,
        antialias: true,
        hello: true // Forces PixiJS to log its boot signature to the console to verify execution
    });

    container.appendChild(app.canvas);

    const textStyle = new TextStyle({
        fontFamily: 'Arial',
        fontSize: 36,
        fontWeight: 'bold',
        fill: '#ffffff'
    });

    const helloText = new Text({
        text: 'Hello World from PixiJS!',
        style: textStyle
    });

    helloText.anchor.set(0.5);
    helloText.x = container.clientWidth / 2;
    helloText.y = container.clientHeight / 2;

    app.stage.addChild(helloText);

    app.renderer.on('resize', () => {
        helloText.x = container.clientWidth / 2;
        helloText.y = container.clientHeight / 2;
    });
}

(window as any).initGame = initGame;

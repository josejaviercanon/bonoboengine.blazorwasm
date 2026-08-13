import { BitmapText, Container } from 'pixi.js';
import type { SceneBuilder } from './types';

export const bitmapText2Scene: SceneBuilder = (app) => {
    app.renderer.background.color = '#1099bb';

    const container = new Container();
    app.stage.addChild(container);

    const displayText = new BitmapText({
        text: 'Hello, PixiJS!',
        style: {
            fontFamily: 'Arial',
            fontSize: 16,
            fill: '#ddd',
        },
    });

    displayText.x = 100;
    displayText.y = 100;
    displayText.anchor.set(0.5);

    container.addChild(displayText);

    container.x = app.screen.width / 2;
    container.y = app.screen.height / 2;
    container.pivot.x = container.width / 2;
    container.pivot.y = container.height / 2;

    app.ticker.add((ticker) => {
        container.rotation -= 0.01 * ticker.deltaTime;
    });
};

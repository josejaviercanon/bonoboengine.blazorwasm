import { Assets, Container, Sprite } from 'pixi.js';
import type { SceneBuilder } from './types';

export const blendModesScene: SceneBuilder = async (app) => {
    app.renderer.background.color = '#ffffff';

    const container = new Container();
    app.stage.addChild(container);

    const texture = await Assets.load('https://pixijs.com/assets/bunny.png');

    for (let i = 0; i < 25; i++) {
        const bunny = new Sprite(texture);
        bunny.x = (i % 5) * 40;
        bunny.y = Math.floor(i / 5) * 40;
        container.addChild(bunny);
    }

    container.x = app.screen.width / 2;
    container.y = app.screen.height / 2;
    container.pivot.x = container.width / 2;
    container.pivot.y = container.height / 2;

    app.ticker.add((ticker) => {
        container.rotation -= 0.01 * ticker.deltaTime;
    });
};

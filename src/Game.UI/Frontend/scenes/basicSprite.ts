import { Assets, Sprite } from 'pixi.js';
import type { SceneBuilder } from './types';

export const basicSpriteScene: SceneBuilder = async (app) => {
    app.renderer.background.color = '#1099bb';

    const texture = await Assets.load('https://pixijs.com/assets/bunny.png');

    const bunny = new Sprite(texture);
    bunny.anchor.set(0.5);
    bunny.x = app.screen.width / 2;
    bunny.y = app.screen.height / 2;
    bunny.scale.set(4);
    bunny.eventMode = 'static';
    bunny.cursor = 'pointer';

    bunny.on('pointertap', () => {
        bunny.scale.x *= 1.25;
        bunny.scale.y *= 1.25;
    });

    app.stage.addChild(bunny);

    app.ticker.add((ticker) => {
        if (!bunny.destroyed) {
            bunny.rotation += 0.1 * ticker.deltaTime;
        }
    });
};

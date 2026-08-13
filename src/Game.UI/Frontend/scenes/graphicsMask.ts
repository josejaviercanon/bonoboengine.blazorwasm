import { Assets, Container, Graphics, Sprite } from 'pixi.js';
import type { SceneBuilder } from './types';

export const graphicsMaskScene: SceneBuilder = async (app) => {
    app.renderer.background.color = '#1099bb';

    const container = new Container();
    container.x = 400;
    container.y = 300;
    app.stage.addChild(container);

    const bg = await Assets.load('https://pixijs.com/assets/bg_rotate.jpg');
    const bgSprite = new Sprite(bg);
    bgSprite.anchor.set(0.5);
    container.addChild(bgSprite);

    const mask = new Graphics();
    mask.rect(-100, -100, 200, 200).fill(0x000000);

    container.mask = mask;
    container.addChild(mask);

    app.ticker.add((ticker) => {
        container.rotation += 0.01 * ticker.deltaTime;
        mask.rotation -= 0.01 * ticker.deltaTime;
    });
};

import { Assets, Sprite } from 'pixi.js';
import type { SceneBuilder } from './types';

export const assetBundleScene: SceneBuilder = async (app) => {
    app.renderer.background.color = '#1099bb';

    Assets.addBundle('animals', [
        { alias: 'bunny', src: 'https://pixijs.com/assets/bunny.png' },
        { alias: 'eggHead', src: 'https://pixijs.com/assets/eggHead.png' },
        { alias: 'flowerTop', src: 'https://pixijs.com/assets/flowerTop.png' },
    ]);

    await Assets.loadBundle('animals');

    const bunnyTexture = await Assets.load('bunny');
    const eggHeadTexture = await Assets.load('eggHead');
    const flowerTopTexture = await Assets.load('flowerTop');

    const bunny = new Sprite(bunnyTexture);
    bunny.anchor.set(0.5);
    bunny.x = app.screen.width / 2 - 150;
    bunny.y = app.screen.height / 2;
    bunny.scale.set(3);
    app.stage.addChild(bunny);

    const eggHead = new Sprite(eggHeadTexture);
    eggHead.anchor.set(0.5);
    eggHead.x = app.screen.width / 2;
    eggHead.y = app.screen.height / 2;
    eggHead.scale.set(0.5);
    app.stage.addChild(eggHead);

    const flowerTop = new Sprite(flowerTopTexture);
    flowerTop.anchor.set(0.5);
    flowerTop.x = app.screen.width / 2 + 150;
    flowerTop.y = app.screen.height / 2;
    flowerTop.scale.set(0.5);
    app.stage.addChild(flowerTop);
};

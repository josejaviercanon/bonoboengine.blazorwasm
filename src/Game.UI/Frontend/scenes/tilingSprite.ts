import { Assets, TilingSprite } from 'pixi.js';
import type { SceneBuilder } from './types';

export const tilingSpriteScene: SceneBuilder = async (app) => {
    app.renderer.background.color = '#1099bb';

    const texture = await Assets.load('https://pixijs.com/assets/p2.jpeg');

    const tilingSprite = new TilingSprite({
        texture,
        width: app.screen.width,
        height: app.screen.height,
    });

    app.stage.addChild(tilingSprite);

    let count = 0;
    app.ticker.add(() => {
        count += 0.005;
        tilingSprite.tileScale.x = 2 + Math.sin(count);
        tilingSprite.tileScale.y = 2 + Math.cos(count);
        tilingSprite.tilePosition.x += 1;
        tilingSprite.tilePosition.y += 1;
    });
};

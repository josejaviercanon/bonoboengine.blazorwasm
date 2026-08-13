import { Assets, Sprite } from 'pixi.js';
import type { Sprite as PixiSprite } from 'pixi.js';
import type { SceneBuilder } from './types';

export const draggingScene: SceneBuilder = async (app) => {
    app.renderer.background.color = '#1099bb';

    app.stage.eventMode = 'static';
    app.stage.hitArea = app.screen;

    const texture = await Assets.load('https://pixijs.com/assets/bunny.png');
    texture.source.scaleMode = 'nearest';

    let draggingBunny: PixiSprite | null = null;

    function onDragMove(event: { global: { x: number; y: number } }): void {
        if (draggingBunny) {
            draggingBunny.x = event.global.x;
            draggingBunny.y = event.global.y;
        }
    }

    function onDragEnd(): void {
        if (draggingBunny) {
            draggingBunny.alpha = 1;
            app.stage.off('pointermove', onDragMove);
            app.stage.off('pointerup', onDragEnd);
            app.stage.off('pointerupoutside', onDragEnd);
            draggingBunny = null;
        }
    }

    function createBunny(x: number, y: number): void {
        const bunny = new Sprite(texture);
        bunny.eventMode = 'static';
        bunny.cursor = 'pointer';
        bunny.anchor.set(0.5);
        bunny.scale.set(3);

        bunny.on('pointerdown', () => {
            draggingBunny = bunny;
            bunny.alpha = 0.5;
            app.stage.on('pointermove', onDragMove);
            app.stage.on('pointerup', onDragEnd);
            app.stage.on('pointerupoutside', onDragEnd);
        });

        bunny.x = x;
        bunny.y = y;

        app.stage.addChild(bunny);
    }

    for (let i = 0; i < 10; i++) {
        createBunny(Math.floor(Math.random() * app.screen.width), Math.floor(Math.random() * app.screen.height));
    }
};

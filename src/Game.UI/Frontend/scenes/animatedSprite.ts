import { AnimatedSprite, Assets } from 'pixi.js';
import type { Texture } from 'pixi.js';
import type { SceneBuilder } from './types';

export const animatedSpriteScene: SceneBuilder = async (app) => {
    app.renderer.background.color = '#1099bb';

    const spritesheet = await Assets.load('https://pixijs.com/assets/spritesheet/fighter.json');

    const textures: Texture[] = [];
    for (let i = 1; i <= 30; i++) {
        const key = `rollSequence${String(i).padStart(4, '0')}.png`;
        const texture = spritesheet.textures[key];
        if (texture) {
            textures.push(texture);
        }
    }

    const anim = new AnimatedSprite({
        textures,
        animationSpeed: 0.5,
        autoPlay: true,
    });
    anim.x = app.screen.width / 2;
    anim.y = app.screen.height / 2;
    anim.anchor.set(0.5);

    app.stage.addChild(anim);
};

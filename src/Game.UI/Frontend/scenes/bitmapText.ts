import { Assets, BitmapText } from 'pixi.js';
import type { SceneBuilder } from './types';

export const bitmapTextScene: SceneBuilder = async (app) => {
    app.renderer.background.color = '#1099bb';

    await Assets.load('https://pixijs.com/assets/bitmap-font/desyrel.xml');

    const bitmapFontText = new BitmapText({
        text: 'bitmap fonts are supported!\nWoo yay!',
        style: {
            fontFamily: 'Desyrel',
            fontSize: 55,
            align: 'left',
        },
    });

    bitmapFontText.x = 50;
    bitmapFontText.y = 200;

    app.stage.addChild(bitmapFontText);
};

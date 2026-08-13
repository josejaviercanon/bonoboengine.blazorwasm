import { Assets, Text } from 'pixi.js';
import type { SceneBuilder } from './types';

export const fromFontScene: SceneBuilder = async (app) => {
    app.renderer.background.color = '#1099bb';

    Assets.addBundle('fonts', [
        { alias: 'ChaChicle', src: 'https://pixijs.com/assets/webfont-loader/ChaChicle.ttf' },
        { alias: 'Lineal', src: 'https://pixijs.com/assets/webfont-loader/Lineal.otf' },
        { alias: 'Dotrice Regular', src: 'https://pixijs.com/assets/webfont-loader/Dotrice-Regular.woff' },
        { alias: 'Crosterian', src: 'https://pixijs.com/assets/webfont-loader/Crosterian.woff2' },
    ]);

    await Assets.loadBundle('fonts');

    const text1 = new Text({ text: 'ChaChicle.ttf', style: { fontFamily: 'ChaChicle', fontSize: 50 } });
    const text2 = new Text({ text: 'Lineal.otf', style: { fontFamily: 'Lineal', fontSize: 50 } });
    const text3 = new Text({ text: 'Dotrice Regular.woff', style: { fontFamily: 'Dotrice Regular', fontSize: 50 } });
    const text4 = new Text({ text: 'Crosterian.woff2', style: { fontFamily: 'Crosterian', fontSize: 50 } });

    text2.y = 150;
    text3.y = 300;
    text4.y = 450;

    app.stage.addChild(text1, text2, text3, text4);
};

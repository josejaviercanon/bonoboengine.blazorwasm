import { FillGradient, Text } from 'pixi.js';
import type { SceneBuilder } from './types';

export const pixiTextScene: SceneBuilder = (app) => {
    app.renderer.background.color = '#1099bb';

    const basicText = new Text({ text: 'Basic text in pixi' });
    basicText.x = 50;
    basicText.y = 100;
    app.stage.addChild(basicText);

    const fill = new FillGradient(0, 0, 0, 36 * 1.7 * 7);

    const colors = [0xffffff, 0x00ff99];
    colors.forEach((color, i) => {
        const ratio = i / colors.length;
        fill.addColorStop(ratio, color);
    });

    const richText = new Text({
        text: 'Rich text with a lot of options and across multiple lines',
        style: {
            fontFamily: 'Arial',
            fontSize: 36,
            fontStyle: 'italic',
            fontWeight: 'bold',
            fill,
            stroke: { color: '#4a1850', width: 5, join: 'round' },
            dropShadow: {
                color: '#000000',
                blur: 4,
                angle: Math.PI / 6,
                distance: 6,
            },
            wordWrap: true,
            wordWrapWidth: 440,
        },
    });
    richText.x = 50;
    richText.y = 220;
    app.stage.addChild(richText);

    const skewText = new Text({
        text: 'SKEW IS COOL',
        style: {
            fontFamily: 'Arial',
            dropShadow: {
                alpha: 0.8,
                angle: 2.1,
                blur: 4,
                color: '0x111111',
                distance: 10,
            },
            fill: '#ffffff',
            stroke: { color: '#004620', width: 12, join: 'round' },
            fontSize: 60,
            fontWeight: 'lighter',
        },
    });
    skewText.skew.set(0.65, -0.3);
    skewText.anchor.set(0.5);
    skewText.x = 300;
    skewText.y = 480;
    app.stage.addChild(skewText);
};

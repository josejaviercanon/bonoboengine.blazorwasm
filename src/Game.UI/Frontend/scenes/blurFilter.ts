import { BlurFilter, Graphics } from 'pixi.js';
import type { SceneBuilder } from './types';

export const blurFilterScene: SceneBuilder = (app) => {
    app.renderer.background.color = '#1099bb';

    const graphics1 = new Graphics();
    graphics1.circle(300, 300, 50).fill(0xff0000);
    app.stage.addChild(graphics1);

    const graphics2 = new Graphics();
    graphics2.rect(500, 250, 100, 100).fill(0x0000ff);
    app.stage.addChild(graphics2);

    const blurFilter1 = new BlurFilter();
    graphics1.filters = [blurFilter1];

    const blurFilter2 = new BlurFilter();
    graphics2.filters = [blurFilter2];

    let count = 0;
    app.ticker.add((ticker) => {
        count += 0.005 * ticker.deltaTime;
        const blurAmount1 = Math.cos(count);
        const blurAmount2 = Math.sin(count);
        blurFilter1.strength = 20 * blurAmount1;
        blurFilter2.strength = 20 * blurAmount2;
    });
};

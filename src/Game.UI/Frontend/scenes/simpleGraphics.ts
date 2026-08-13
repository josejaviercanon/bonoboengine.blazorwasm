import { Graphics } from 'pixi.js';
import type { SceneBuilder } from './types';

export const simpleGraphicsScene: SceneBuilder = (app) => {
    app.renderer.background.color = '#1099bb';

    const graphics = new Graphics();

    graphics.rect(50, 50, 100, 100).fill(0xde3249);

    graphics.rect(200, 50, 100, 100).fill(0x650a5a).stroke({ width: 2, color: 0xfeeb77 });

    graphics.rect(350, 50, 100, 100).fill(0xc34288).stroke({ width: 10, color: 0xffbd01 });

    graphics.rect(530, 50, 140, 100).fill(0xaa4f08).stroke({ width: 2, color: 0xffffff });

    graphics.circle(100, 250, 50).fill(0xde3249);

    graphics.circle(250, 250, 50).fill(0x650a5a).stroke({ width: 2, color: 0xfeeb77 });

    graphics.circle(400, 250, 50).fill(0xc34288).stroke({ width: 10, color: 0xffbd01 });

    graphics.ellipse(600, 250, 80, 50).fill(0xaa4f08).stroke({ width: 2, color: 0xffffff });

    graphics.moveTo(50, 350).lineTo(250, 350).lineTo(100, 400).lineTo(50, 350).fill(0xff3300).stroke({ width: 4, color: 0xffd900 });

    graphics.roundRect(50, 440, 100, 100, 16).fill({ color: 0x650a5a, alpha: 0.25 }).stroke({ width: 2, color: 0xff00ff });

    graphics.star(360, 370, 5, 50).fill(0x35cc5a).stroke({ width: 2, color: 0xffffff });

    graphics.star(280, 510, 7, 50).fill(0xffcc5a).stroke({ width: 2, color: 0xfffffd });

    graphics.star(470, 450, 4, 50).fill(0x55335a).stroke({ width: 2, color: 0xfffffd });

    graphics.poly([600, 370, 700, 460, 780, 420, 730, 570, 590, 520]).fill(0x3500fa);

    graphics.eventMode = 'static';
    graphics.cursor = 'pointer';
    graphics.on('pointertap', () => {
        console.log('Graphics clicked!');
        graphics.tint = 0x00ff00;
    });

    app.stage.addChild(graphics);
};

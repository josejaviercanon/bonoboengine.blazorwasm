import { Assets, MeshRope, Point } from 'pixi.js';
import type { SceneBuilder } from './types';

export const meshRopeScene: SceneBuilder = async (app) => {
    app.renderer.background.color = '#1099bb';

    const texture = await Assets.load('https://pixijs.com/assets/snake.png');

    const ropeLength = 45;
    const points: Point[] = [];
    for (let i = 0; i < 20; i++) {
        points.push(new Point(i * ropeLength, 0));
    }

    const rope = new MeshRope({ texture, points });
    rope.x = -40;
    rope.y = 300;

    app.stage.addChild(rope);

    let count = 0;
    app.ticker.add((ticker) => {
        count += 0.1 * ticker.deltaTime;
        for (let i = 0; i < points.length; i++) {
            points[i].y = Math.sin(i * 0.5 + count) * 30;
            points[i].x = i * ropeLength + Math.cos(i * 0.3 + count) * 20;
        }
    });
};

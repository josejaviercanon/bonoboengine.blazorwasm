import type { Application } from 'pixi.js';

export type SceneBuilder = (app: Application, params: Record<string, unknown>) => Promise<void> | void;

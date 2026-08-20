import { Assets, Container, Graphics, Rectangle, Sprite, Text, TextStyle, Texture, TilingSprite } from 'pixi.js';
import { sound } from '@pixi/sound';
import type { SceneBuilder } from './types';
import { publishCSharpStats } from '../stats/overlays';
import { SnapshotBuffer, lerp, lerpWrapped } from './interpolation';
import { connectSignalStream } from './signalSource';

interface RacerSettings {
    lanes: number;
    roadWidth: number;
    cameraHeight: number;
    drawDistance: number;
    fieldOfView: number;
    fogDensity: number;
    resolutionScale: number;
}

interface RacerSegmentState {
    index: number;
    p1WorldY: number;
    p2WorldY: number;
    curve: number;
    color: number;
}

interface RacerSceneryState {
    segmentIndex: number;
    offset: number;
    spriteKind: number;
}

interface RacerCarState {
    id: number;
    z: number;
    offset: number;
    speed: number;
    percent: number;
    spriteKind: number;
}

interface RacerPlayerState {
    x: number;
    z: number;
    speed: number;
    currentLapTime: number;
    lastLapTime: number;
    fastLapTime: number;
    lap: number;
    steer: number;
    uphill: boolean;
}

interface RacerTrackPayload {
    segments?: RacerSegmentState[];
    sprites?: RacerSceneryState[];
    trackLength?: number;
    segmentLength?: number;
    rumbleLength?: number;
}

interface RacerSceneParams {
    racer?: {
        track?: RacerTrackPayload;
        player?: RacerPlayerState;
        cars?: RacerCarState[];
        settings?: RacerSettings;
        streamUrl?: string;
    };
}

interface RacerRenderSignal {
    seq: number;
    entityCount: number;
    tickMs: number;
    stepMs?: number;
    epoch?: number;
    player: RacerPlayerState;
    cars: RacerCarState[];
    settings: RacerSettings;
    lapCompleted: boolean;
    collided: boolean;
}

interface RacerPlayerSample extends RacerPlayerState {
    id: number;
}

interface AtlasRect {
    x: number;
    y: number;
    w: number;
    h: number;
}

interface ProjectedPoint {
    x: number;
    y: number;
    w: number;
    scale: number;
    cameraZ: number;
    cameraY: number;
}

interface ProjectedSegment {
    segment: RacerSegmentState;
    p1: ProjectedPoint;
    p2: ProjectedPoint;
    fog: number;
    clip: number;
}

const SEGMENT_LIGHT = 0;
const SEGMENT_DARK = 1;
const SEGMENT_START = 2;
const SEGMENT_FINISH = 3;

// Values match RacerSpriteKind in Game.Engine.ECS.Racer.RacerComponents.cs.
const KIND = {
    PALM_TREE: 0,
    BILLBOARD08: 1,
    TREE1: 2,
    DEAD_TREE1: 3,
    BILLBOARD09: 4,
    BOULDER3: 5,
    COLUMN: 6,
    BILLBOARD01: 7,
    BILLBOARD06: 8,
    BILLBOARD05: 9,
    BOULDER2: 10,
    BILLBOARD07: 11,
    TREE2: 12,
    BILLBOARD04: 13,
    DEAD_TREE2: 14,
    BOULDER1: 15,
    BUSH1: 16,
    CACTUS: 17,
    BUSH2: 18,
    BILLBOARD03: 19,
    BILLBOARD02: 20,
    STUMP: 21,
    SEMI: 22,
    TRUCK: 23,
    CAR03: 24,
    CAR02: 25,
    CAR04: 26,
    CAR01: 27,
    PLAYER_UPHILL_LEFT: 28,
    PLAYER_UPHILL_STRAIGHT: 29,
    PLAYER_UPHILL_RIGHT: 30,
    PLAYER_LEFT: 31,
    PLAYER_STRAIGHT: 32,
    PLAYER_RIGHT: 33,
} as const;

const ATLAS: Record<number, AtlasRect> = {
    [KIND.PALM_TREE]: { x: 5, y: 5, w: 215, h: 540 },
    [KIND.BILLBOARD08]: { x: 230, y: 5, w: 385, h: 265 },
    [KIND.TREE1]: { x: 625, y: 5, w: 360, h: 360 },
    [KIND.DEAD_TREE1]: { x: 5, y: 555, w: 135, h: 332 },
    [KIND.BILLBOARD09]: { x: 150, y: 555, w: 328, h: 282 },
    [KIND.BOULDER3]: { x: 230, y: 280, w: 320, h: 220 },
    [KIND.COLUMN]: { x: 995, y: 5, w: 200, h: 315 },
    [KIND.BILLBOARD01]: { x: 625, y: 375, w: 300, h: 170 },
    [KIND.BILLBOARD06]: { x: 488, y: 555, w: 298, h: 190 },
    [KIND.BILLBOARD05]: { x: 5, y: 897, w: 298, h: 190 },
    [KIND.BOULDER2]: { x: 621, y: 897, w: 298, h: 140 },
    [KIND.BILLBOARD07]: { x: 313, y: 897, w: 298, h: 190 },
    [KIND.TREE2]: { x: 1205, y: 5, w: 282, h: 295 },
    [KIND.BILLBOARD04]: { x: 1205, y: 310, w: 268, h: 170 },
    [KIND.DEAD_TREE2]: { x: 1205, y: 490, w: 150, h: 260 },
    [KIND.BOULDER1]: { x: 1205, y: 760, w: 168, h: 248 },
    [KIND.BUSH1]: { x: 5, y: 1097, w: 240, h: 155 },
    [KIND.CACTUS]: { x: 929, y: 897, w: 235, h: 118 },
    [KIND.BUSH2]: { x: 255, y: 1097, w: 232, h: 152 },
    [KIND.BILLBOARD03]: { x: 5, y: 1262, w: 230, h: 220 },
    [KIND.BILLBOARD02]: { x: 245, y: 1262, w: 215, h: 220 },
    [KIND.STUMP]: { x: 995, y: 330, w: 195, h: 140 },
    [KIND.SEMI]: { x: 1365, y: 490, w: 122, h: 144 },
    [KIND.TRUCK]: { x: 1365, y: 644, w: 100, h: 78 },
    [KIND.CAR03]: { x: 1383, y: 760, w: 88, h: 55 },
    [KIND.CAR02]: { x: 1383, y: 825, w: 80, h: 59 },
    [KIND.CAR04]: { x: 1383, y: 894, w: 80, h: 57 },
    [KIND.CAR01]: { x: 1205, y: 1018, w: 80, h: 56 },
    [KIND.PLAYER_UPHILL_LEFT]: { x: 1383, y: 961, w: 80, h: 45 },
    [KIND.PLAYER_UPHILL_STRAIGHT]: { x: 1295, y: 1018, w: 80, h: 45 },
    [KIND.PLAYER_UPHILL_RIGHT]: { x: 1385, y: 1018, w: 80, h: 45 },
    [KIND.PLAYER_LEFT]: { x: 995, y: 480, w: 80, h: 41 },
    [KIND.PLAYER_STRAIGHT]: { x: 1085, y: 480, w: 80, h: 41 },
    [KIND.PLAYER_RIGHT]: { x: 995, y: 531, w: 80, h: 41 },
};

const DEFAULT_SETTINGS: RacerSettings = {
    lanes: 3,
    roadWidth: 2000,
    cameraHeight: 1000,
    drawDistance: 300,
    fieldOfView: 100,
    fogDensity: 5,
    resolutionScale: 1,
};

const COLORS: Record<number, { road: number; grass: number; rumble: number; lane: number | null }> = {
    [SEGMENT_LIGHT]: { road: 0x6b6b6b, grass: 0x10aa10, rumble: 0x555555, lane: 0xcccccc },
    [SEGMENT_DARK]: { road: 0x696969, grass: 0x009a00, rumble: 0xbbbbbb, lane: null },
    [SEGMENT_START]: { road: 0xffffff, grass: 0xffffff, rumble: 0xffffff, lane: null },
    [SEGMENT_FINISH]: { road: 0x000000, grass: 0x000000, rumble: 0x000000, lane: null },
};

const SKY_SPEED = 0.001;
const HILL_SPEED = 0.002;
const TREE_SPEED = 0.003;
const SPRITE_SCALE = 0.3 / 80;
const SOUND_ALIAS = 'racer-music';
const SOUND_URL = '_content/Game.UI/games/racer/racer.mp3';

const dbg = (...args: unknown[]) => console.log('[pixi-debug] racer:', ...args);

function isRacerRenderSignal(value: unknown): value is RacerRenderSignal {
    if (!value || typeof value !== 'object') return false;
    const candidate = value as Partial<RacerRenderSignal>;
    return typeof candidate.seq === 'number' &&
        typeof candidate.entityCount === 'number' &&
        typeof candidate.tickMs === 'number' &&
        typeof candidate.player === 'object' &&
        Array.isArray(candidate.cars) &&
        typeof candidate.settings === 'object';
}

function interpolate(a: number, b: number, percent: number): number {
    return a + (b - a) * percent;
}

function percentRemaining(value: number, total: number): number {
    const remainder = value % total;
    return (remainder < 0 ? remainder + total : remainder) / total;
}

function increase(start: number, increment: number, max: number): number {
    let result = start + increment;
    while (result >= max) result -= max;
    while (result < 0) result += max;
    return result;
}

function formatTime(value: number): string {
    const minutes = Math.floor(value / 60);
    const seconds = Math.floor(value - minutes * 60);
    const tenths = Math.floor(10 * (value - Math.floor(value)));
    return minutes > 0
        ? `${minutes}.${seconds < 10 ? '0' : ''}${seconds}.${tenths}`
        : `${seconds}.${tenths}`;
}

function clearChildren(container: Container): void {
    const children = container.removeChildren();
    for (const child of children) child.destroy();
}

interface SettingsPanel {
    element: HTMLDivElement;
    update: (next: RacerSettings) => void;
    read: () => RacerSettings;
    setVisible: (visible: boolean) => void;
    setBusy: (busy: boolean) => void;
}

function makeSettingsPanel(
    initial: RacerSettings,
    onApply: (next: RacerSettings) => Promise<void>,
    onCancel: () => Promise<void>,
): SettingsPanel {
    const panel = document.createElement('div');
    panel.style.cssText =
        'position:fixed;top:108px;right:12px;width:220px;padding:0.6rem;background:rgba(2,6,23,.9);' +
        'border:1px solid rgba(148,163,184,.35);border-radius:.5rem;color:#cbd5e1;font:12px sans-serif;' +
        'z-index:6;display:none;gap:.35rem;';
    const title = document.createElement('strong');
    title.textContent = 'Racer tuning (paused)';
    title.style.color = '#fbbf24';
    panel.appendChild(title);

    type NumericKey = keyof RacerSettings;
    const inputs = new Map<NumericKey, HTMLInputElement>();
    let draft = { ...initial };
    const ranges: Array<[NumericKey, string, number, number, number]> = [
        ['lanes', 'Lanes', 1, 4, 1],
        ['roadWidth', 'Road width', 500, 3000, 50],
        ['cameraHeight', 'Camera height', 500, 5000, 50],
        ['drawDistance', 'Draw distance', 100, 500, 10],
        ['fieldOfView', 'Field of view', 80, 140, 1],
        ['fogDensity', 'Fog density', 0, 50, 1],
        ['resolutionScale', 'Resolution', 0.4, 1.5, 0.1],
    ];

    for (const [key, labelText, min, max, step] of ranges) {
        const label = document.createElement('label');
        label.style.display = 'grid';
        label.style.gap = '2px';
        const caption = document.createElement('span');
        caption.textContent = labelText;
        const input = document.createElement('input');
        input.type = 'range';
        input.min = String(min);
        input.max = String(max);
        input.step = String(step);
        input.value = String(initial[key]);
        input.addEventListener('input', () => {
            draft = {
                lanes: Number(inputs.get('lanes')?.value ?? initial.lanes),
                roadWidth: Number(inputs.get('roadWidth')?.value ?? initial.roadWidth),
                cameraHeight: Number(inputs.get('cameraHeight')?.value ?? initial.cameraHeight),
                drawDistance: Number(inputs.get('drawDistance')?.value ?? initial.drawDistance),
                fieldOfView: Number(inputs.get('fieldOfView')?.value ?? initial.fieldOfView),
                fogDensity: Number(inputs.get('fogDensity')?.value ?? initial.fogDensity),
                resolutionScale: Number(inputs.get('resolutionScale')?.value ?? initial.resolutionScale),
            };
        });
        inputs.set(key, input);
        label.append(caption, input);
        panel.appendChild(label);
    }

    const actions = document.createElement('div');
    actions.style.cssText = 'display:flex;justify-content:flex-end;gap:.4rem;margin-top:.35rem;';
    const cancelButton = document.createElement('button');
    cancelButton.type = 'button';
    cancelButton.textContent = 'Cancel';
    cancelButton.style.cssText =
        'border:1px solid #64748b;background:#1e293b;color:#e2e8f0;border-radius:.3rem;padding:.3rem .55rem;cursor:pointer;';
    cancelButton.addEventListener('click', () => { void onCancel(); });
    const applyButton = document.createElement('button');
    applyButton.type = 'button';
    applyButton.textContent = 'Apply';
    applyButton.style.cssText =
        'border:1px solid #f59e0b;background:#f59e0b;color:#111827;border-radius:.3rem;padding:.3rem .55rem;cursor:pointer;';
    applyButton.addEventListener('click', () => { void onApply(draft); });
    actions.append(cancelButton, applyButton);
    panel.appendChild(actions);

    const update = (next: RacerSettings): void => {
        draft = { ...next };
        for (const [key, input] of inputs) input.value = String(next[key]);
    };

    const read = (): RacerSettings => ({ ...draft });
    const setVisible = (visible: boolean): void => {
        panel.style.display = visible ? 'grid' : 'none';
    };
    const setBusy = (busy: boolean): void => {
        cancelButton.disabled = busy;
        applyButton.disabled = busy;
        for (const input of inputs.values()) input.disabled = busy;
    };

    document.body.appendChild(panel);
    return { element: panel, update, read, setVisible, setBusy };
}

export const racerScene: SceneBuilder = async (app, params) => {
    const payload = (params ?? {}) as unknown as RacerSceneParams;
    const racer = payload.racer ?? {};
    const track = racer.track ?? {};
    const segments = track.segments ?? [];
    const scenery = track.sprites ?? [];
    const segmentLength = track.segmentLength ?? 200;
    const trackLength = track.trackLength ?? segments.length * segmentLength;
    let settings = racer.settings ?? DEFAULT_SETTINGS;
    let player = racer.player ?? {
        x: 0, z: 0, speed: 0, currentLapTime: 0, lastLapTime: 0, fastLapTime: 180,
        lap: 0, steer: 0, uphill: false,
    };
    let cars = racer.cars ?? [];
    let skyOffset = 0;
    let hillOffset = 0;
    let treeOffset = 0;
    let previousPosition = player.z;
    let stepMs = 1000 / 60;
    const playerInterpolation = new SnapshotBuffer<RacerPlayerSample>();
    const carInterpolation = new SnapshotBuffer<RacerCarState>();
    let lastEpoch: number | null = null;

    app.renderer.background.color = '#72d7ee';

    const [atlas, background] = await Promise.all([
        Assets.load('_content/Game.UI/games/racer/sprites.png') as Promise<Texture>,
        Assets.load('_content/Game.UI/games/racer/background.png') as Promise<Texture>,
    ]);

    const world = new Container();
    const roadGraphics = new Graphics();
    const sceneryContainer = new Container();
    const carContainer = new Container();
    const playerContainer = new Container();
    world.addChild(sceneryContainer, roadGraphics, carContainer, playerContainer);
    app.stage.addChild(world);

    const layerTextures = [
        new Texture({ source: background.source, frame: new Rectangle(5, 495, 640, 480) }),
        new Texture({ source: background.source, frame: new Rectangle(5, 5, 640, 480) }),
        new Texture({ source: background.source, frame: new Rectangle(5, 985, 640, 480) }),
    ];
    const backgroundLayers = layerTextures.map((texture) => {
        const layer = new TilingSprite({ texture, width: app.screen.width, height: app.screen.height });
        world.addChildAt(layer, 0);
        return layer;
    });

    const hud = new Text({
        text: '0 mph   Lap: 0.0',
        style: new TextStyle({ fontFamily: 'Arial', fontSize: 18, fontWeight: 'bold', fill: '#ffffff' }),
    });
    hud.position.set(16, 12);
    app.stage.addChild(hud);

    const postCommand = async (path: string): Promise<void> => {
        const response = await fetch(path, { method: 'POST' });
        if (!response.ok) throw new Error(`${path} returned ${response.status}`);
    };

    const postConfig = async (next: RacerSettings): Promise<void> => {
        const response = await fetch('/api/racer/config', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(next),
        });
        if (!response.ok) throw new Error(`/api/racer/config returned ${response.status}`);
    };

    let tuningOpen = false;
    let tuningBusy = false;
    let started = false;
    let settingsBeforeTuning = { ...settings };
    let panel: SettingsPanel;
    let configButton: HTMLButtonElement;

    const closeTuning = (): void => {
        tuningOpen = false;
        panel.setVisible(false);
        configButton.title = 'Configure race';
        configButton.setAttribute('aria-label', 'Configure race');
    };

    async function openTuning(): Promise<void> {
        if (tuningOpen || tuningBusy) return;
        tuningBusy = true;
        configButton.disabled = true;
        try {
            await postCommand('/api/racer/pause');
            settingsBeforeTuning = { ...settings };
            panel.update(settings);
            panel.setVisible(true);
            tuningOpen = true;
            configButton.title = 'Hide race tuning';
            configButton.setAttribute('aria-label', 'Hide race tuning');
        } catch (error: unknown) {
            console.error('[pixi-debug] racer pause failed:', error);
        } finally {
            tuningBusy = false;
            configButton.disabled = false;
        }
    }

    async function applyTuning(next: RacerSettings): Promise<void> {
        if (!tuningOpen || tuningBusy) return;
        tuningBusy = true;
        panel.setBusy(true);
        try {
            await postConfig(next);
            settings = { ...next };
            if (started) await postCommand('/api/racer/resume');
            closeTuning();
        } catch (error: unknown) {
            console.error('[pixi-debug] racer tuning apply failed:', error);
        } finally {
            tuningBusy = false;
            panel.setBusy(false);
            configButton.disabled = false;
        }
    }

    async function cancelTuning(): Promise<void> {
        if (!tuningOpen || tuningBusy) return;
        tuningBusy = true;
        panel.setBusy(true);
        try {
            // Only resume when a race is actually running; before START the sim
            // must stay paused behind the start overlay.
            if (started) await postCommand('/api/racer/resume');
            settings = { ...settingsBeforeTuning };
            panel.update(settings);
            closeTuning();
        } catch (error: unknown) {
            console.error('[pixi-debug] racer tuning cancel failed:', error);
        } finally {
            tuningBusy = false;
            panel.setBusy(false);
            configButton.disabled = false;
        }
    }

    configButton = document.createElement('button');
    configButton.id = 'racer-config-button';
    configButton.type = 'button';
    configButton.title = 'Configure race';
    configButton.setAttribute('aria-label', 'Configure race');
    configButton.innerHTML =
        '<svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true">' +
        '<path d="M12 8.5a3.5 3.5 0 1 0 0 7 3.5 3.5 0 0 0 0-7Z"/>' +
        '<path d="m19.4 15 .1.1a2 2 0 0 1-2.8 2.8l-.1-.1a2 2 0 0 0-3.4 1.4v.2a2 2 0 0 1-4 0v-.2a2 2 0 0 0-3.4-1.4l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1A2 2 0 0 0 1.6 11H1.4a2 2 0 0 1 0-4h.2A2 2 0 0 0 3 3.6l-.1-.1A2 2 0 1 1 5.7.7l.1.1A2 2 0 0 0 9.2-.6v-.2a2 2 0 0 1 4 0v.2a2 2 0 0 0 3.4 1.4l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1A2 2 0 0 0 20.8 7h.2a2 2 0 0 1 0 4h-.2a2 2 0 0 0-1.4 4Z" transform="translate(0 2) scale(.83)"/>' +
        '</svg>';
    configButton.style.cssText =
        'position:fixed;top:64px;right:12px;width:36px;height:36px;display:grid;place-items:center;' +
        'border:1px solid rgba(148,163,184,.45);border-radius:.45rem;background:rgba(2,6,23,.9);' +
        'color:#fbbf24;cursor:pointer;z-index:7;';
    configButton.addEventListener('click', () => {
        void (tuningOpen ? cancelTuning() : openTuning());
    });
    document.body.appendChild(configButton);

    // --- Start overlay + restart: every game scene needs an explicit start. ------
    // The endless racer has no game-over state, so "play again" is the RESTART
    // button shown once the race is running (server restart bumps the epoch,
    // which resets both snapshot buffers and the parallax offsets).
    const overlay = document.createElement('div');
    overlay.id = 'racer-start-overlay';
    overlay.style.cssText =
        'position:fixed;inset:0;display:flex;flex-direction:column;align-items:center;justify-content:center;' +
        'gap:1rem;background:rgba(2,6,23,.78);z-index:20;';
    const overlayTitle = document.createElement('h2');
    overlayTitle.textContent = 'ENDLESS RACER';
    overlayTitle.style.cssText = 'margin:0;font-family:Arial,sans-serif;font-size:2rem;color:#fbbf24;';
    const startButton = document.createElement('button');
    startButton.type = 'button';
    startButton.textContent = 'START GAME';
    startButton.style.cssText =
        'font-family:Arial,sans-serif;font-size:1.25rem;font-weight:bold;color:#0f172a;background:#fbbf24;' +
        'border:none;border-radius:.5rem;padding:.75rem 2.5rem;cursor:pointer;';
    const overlayHint = document.createElement('p');
    overlayHint.textContent = '↑/W accelerate · ↓/S brake · ←/→ or A/D steer';
    overlayHint.style.cssText = 'margin:0;font-family:Arial,sans-serif;color:#94a3b8;';
    overlay.append(overlayTitle, startButton, overlayHint);
    document.body.appendChild(overlay);

    const restartButton = document.createElement('button');
    restartButton.id = 'racer-restart-button';
    restartButton.type = 'button';
    restartButton.textContent = 'RESTART';
    restartButton.title = 'Restart race';
    restartButton.style.cssText =
        'position:fixed;top:106px;right:12px;display:none;font-family:Arial,sans-serif;font-weight:bold;' +
        'color:#fbbf24;background:rgba(2,6,23,.9);border:1px solid rgba(148,163,184,.45);border-radius:.45rem;' +
        'padding:.4rem .6rem;cursor:pointer;z-index:7;';
    document.body.appendChild(restartButton);

    const startGame = (): void => {
        if (started) return;
        void postCommand('/api/racer/resume')
            .then(() => {
                started = true;
                overlay.style.display = 'none';
                restartButton.style.display = 'block';
                void sound.play(SOUND_ALIAS, { loop: true, volume: 0.05 });
            })
            .catch((error: unknown) => console.error('[pixi-debug] racer start failed:', error));
    };
    const restartRun = (): void => {
        void postCommand('/api/racer/restart')
            .catch((error: unknown) => console.error('[pixi-debug] racer restart failed:', error));
    };
    startButton.addEventListener('click', startGame);
    restartButton.addEventListener('click', restartRun);

    panel = makeSettingsPanel(settings, applyTuning, cancelTuning);
    panel.element.id = 'racer-tuning-panel';

    const textureCache = new Map<number, Texture>();
    const sceneryBySegment = new Map<number, RacerSceneryState[]>();
    for (const sprite of scenery) {
        const list = sceneryBySegment.get(sprite.segmentIndex) ?? [];
        list.push(sprite);
        sceneryBySegment.set(sprite.segmentIndex, list);
    }

    const textureFor = (kind: number): Texture | null => {
        const rect = ATLAS[kind];
        if (!rect) return null;
        const cached = textureCache.get(kind);
        if (cached) return cached;
        const texture = new Texture({ source: atlas.source, frame: new Rectangle(rect.x, rect.y, rect.w, rect.h) });
        textureCache.set(kind, texture);
        return texture;
    };

    const project = (
        worldX: number,
        worldY: number,
        worldZ: number,
        cameraX: number,
        cameraY: number,
        cameraZ: number,
        cameraDepth: number,
        width: number,
        height: number,
        roadWidth: number,
    ): ProjectedPoint => {
        const cameraRelativeX = worldX - cameraX;
        const cameraRelativeY = worldY - cameraY;
        const cameraRelativeZ = worldZ - cameraZ;
        const scale = cameraDepth / cameraRelativeZ;
        return {
            x: Math.round(width / 2 + scale * cameraRelativeX * width / 2),
            y: Math.round(height / 2 - scale * cameraRelativeY * height / 2),
            w: Math.round(scale * roadWidth * width / 2),
            scale,
            cameraZ: cameraRelativeZ,
            cameraY: cameraRelativeY,
        };
    };

    const drawPoly = (points: number[], color: number, alpha = 1): void => {
        roadGraphics.poly(points).fill({ color, alpha });
    };

    const drawSegment = (projected: ProjectedSegment, width: number): void => {
        const { p1, p2, segment, fog } = projected;
        const palette = COLORS[segment.color] ?? COLORS[SEGMENT_LIGHT];
        const rumble1 = p1.w / Math.max(6, 2 * settings.lanes);
        const rumble2 = p2.w / Math.max(6, 2 * settings.lanes);
        const lane1 = p1.w / Math.max(32, 8 * settings.lanes);
        const lane2 = p2.w / Math.max(32, 8 * settings.lanes);

        roadGraphics.rect(0, p2.y, width, Math.max(0, p1.y - p2.y)).fill(palette.grass);
        drawPoly([p1.x - p1.w - rumble1, p1.y, p1.x - p1.w, p1.y, p2.x - p2.w, p2.y, p2.x - p2.w - rumble2, p2.y], palette.rumble);
        drawPoly([p1.x + p1.w + rumble1, p1.y, p1.x + p1.w, p1.y, p2.x + p2.w, p2.y, p2.x + p2.w + rumble2, p2.y], palette.rumble);
        drawPoly([p1.x - p1.w, p1.y, p1.x + p1.w, p1.y, p2.x + p2.w, p2.y, p2.x - p2.w, p2.y], palette.road);

        if (palette.lane !== null) {
            const laneWidth1 = p1.w * 2 / settings.lanes;
            const laneWidth2 = p2.w * 2 / settings.lanes;
            let laneX1 = p1.x - p1.w + laneWidth1;
            let laneX2 = p2.x - p2.w + laneWidth2;
            for (let lane = 1; lane < settings.lanes; lane++) {
                drawPoly([
                    laneX1 - lane1 / 2, p1.y, laneX1 + lane1 / 2, p1.y,
                    laneX2 + lane2 / 2, p2.y, laneX2 - lane2 / 2, p2.y,
                ], palette.lane);
                laneX1 += laneWidth1;
                laneX2 += laneWidth2;
            }
        }

        if (fog < 1) {
            roadGraphics.rect(0, p2.y, width, Math.max(0, p1.y - p2.y))
                .fill({ color: 0x005108, alpha: 1 - fog });
        }
    };

    const drawSprite = (
        container: Container,
        kind: number,
        scale: number,
        x: number,
        y: number,
        offsetX: number,
        clipY: number,
        width: number,
    ): void => {
        const rect = ATLAS[kind];
        const texture = textureFor(kind);
        if (!rect || !texture) return;
        const spriteWidth = rect.w * scale * width / 2 * (SPRITE_SCALE * settings.roadWidth);
        const spriteHeight = rect.h * scale * width / 2 * (SPRITE_SCALE * settings.roadWidth);
        const left = x + spriteWidth * offsetX;
        const top = y - spriteHeight;
        const visibleHeight = Math.min(spriteHeight, Math.max(0, clipY - top));
        if (visibleHeight <= 0 || spriteWidth <= 0) return;

        const sprite = new Sprite(texture);
        sprite.x = left;
        sprite.y = top;
        sprite.width = spriteWidth;
        sprite.height = visibleHeight;
        container.addChild(sprite);
    };

    const updateBackground = (logicalWidth: number, logicalHeight: number, playerY: number): void => {
        world.scale.set(settings.resolutionScale);
        for (const layer of backgroundLayers) {
            layer.width = logicalWidth;
            layer.height = logicalHeight;
            layer.tileScale.set(1, logicalHeight / 480);
        }
        backgroundLayers[0].tilePosition.set(-skyOffset * 1280, logicalHeight * 0.001 * playerY);
        backgroundLayers[1].tilePosition.set(-hillOffset * 1280, logicalHeight * 0.002 * playerY);
        backgroundLayers[2].tilePosition.set(-treeOffset * 1280, logicalHeight * 0.003 * playerY);
    };

    const render = (): void => {
        if (segments.length === 0) return;
        // Redraw gate: skip the full road/scenery rebuild when no new snapshot
        // arrived and interpolation has settled (e.g. paused at the overlay).
        const playerAlpha = playerInterpolation.advance(stepMs);
        if (playerAlpha === null) return;
        const playerEntry = playerInterpolation.values().next().value;
        // playerAlpha comes from the redraw gate above.
        const renderedPlayer = playerEntry
            ? {
                ...playerEntry.current,
                x: lerp(playerEntry.previous.x, playerEntry.current.x, playerAlpha),
                z: lerpWrapped(playerEntry.previous.z, playerEntry.current.z, playerAlpha, trackLength),
            }
            : player;
        const carAlpha = carInterpolation.alpha(stepMs);
        const renderedCars = Array.from(carInterpolation.values(), entry => {
            const z = lerpWrapped(entry.previous.z, entry.current.z, carAlpha, trackLength);
            return { ...entry.current, z, percent: percentRemaining(z, segmentLength) };
        });
        const delta = increase(renderedPlayer.z - previousPosition, 0, trackLength);
        const motionIndex = Math.floor(renderedPlayer.z / segmentLength) % segments.length;
        const motionSegment = segments[motionIndex < 0 ? motionIndex + segments.length : motionIndex];
        const motionCurve = motionSegment?.curve ?? 0;
        skyOffset = increase(skyOffset, SKY_SPEED * motionCurve * delta / segmentLength, 1);
        hillOffset = increase(hillOffset, HILL_SPEED * motionCurve * delta / segmentLength, 1);
        treeOffset = increase(treeOffset, TREE_SPEED * motionCurve * delta / segmentLength, 1);
        previousPosition = renderedPlayer.z;
        const scale = Math.max(0.1, settings.resolutionScale);
        const width = Math.max(1, app.screen.width / scale);
        const height = Math.max(1, app.screen.height / scale);
        const cameraDepth = 1 / Math.tan((settings.fieldOfView / 2) * Math.PI / 180);
        const playerZ = settings.cameraHeight * cameraDepth;
        const baseIndex = Math.floor(renderedPlayer.z / segmentLength) % segments.length;
        const normalizedBaseIndex = baseIndex < 0 ? baseIndex + segments.length : baseIndex;
        const basePercent = percentRemaining(renderedPlayer.z, segmentLength);
        const playerAbsoluteZ = increase(renderedPlayer.z + playerZ, 0, trackLength);
        const playerSegmentIndex = Math.floor(playerAbsoluteZ / segmentLength) % segments.length;
        const playerSegment = segments[playerSegmentIndex] ?? segments[0];
        const playerPercent = percentRemaining(playerAbsoluteZ, segmentLength);
        const playerY = interpolate(playerSegment?.p1WorldY ?? 0, playerSegment?.p2WorldY ?? 0, playerPercent);
        const cameraY = playerY + settings.cameraHeight;
        let maxY = height;
        let x = 0;
        let dx = -(segments[normalizedBaseIndex]?.curve ?? 0) * basePercent;
        const projectedSegments = new Map<number, ProjectedSegment>();

        updateBackground(width, height, playerY);
        roadGraphics.clear();
        clearChildren(sceneryContainer);
        clearChildren(carContainer);
        clearChildren(playerContainer);

        for (let n = 0; n < settings.drawDistance; n++) {
            const segment = segments[(normalizedBaseIndex + n) % segments.length];
            if (!segment) continue;
            const looped = segment.index < normalizedBaseIndex;
            const fog = 1 / Math.pow(Math.E, (n / settings.drawDistance) ** 2 * settings.fogDensity);
            const p1 = project(0, segment.p1WorldY, segment.index * segmentLength, renderedPlayer.x * settings.roadWidth - x,
                cameraY, renderedPlayer.z - (looped ? trackLength : 0), cameraDepth, width, height, settings.roadWidth);
            const p2 = project(0, segment.p2WorldY, (segment.index + 1) * segmentLength, renderedPlayer.x * settings.roadWidth - x - dx,
                cameraY, renderedPlayer.z - (looped ? trackLength : 0), cameraDepth, width, height, settings.roadWidth);
            x += dx;
            dx += segment.curve;

            const projected: ProjectedSegment = { segment, p1, p2, fog, clip: maxY };
            if (p1.cameraZ <= cameraDepth || p2.y >= p1.y || p2.y >= maxY) continue;
            drawSegment(projected, width);
            maxY = p1.y;
            projected.clip = maxY;
            projectedSegments.set(segment.index, projected);
        }

        const carsBySegment = new Map<number, RacerCarState[]>();
        for (const car of renderedCars) {
            const index = Math.floor(car.z / segmentLength) % segments.length;
            const normalized = index < 0 ? index + segments.length : index;
            const list = carsBySegment.get(normalized) ?? [];
            list.push(car);
            carsBySegment.set(normalized, list);
        }

        for (let n = settings.drawDistance - 1; n > 0; n--) {
            const segment = segments[(normalizedBaseIndex + n) % segments.length];
            if (!segment) continue;
            const projected = projectedSegments.get(segment.index);
            if (!projected) continue;
            const segmentCars = carsBySegment.get(segment.index) ?? [];
            for (const car of segmentCars) {
                const carScale = interpolate(projected.p1.scale, projected.p2.scale, car.percent);
                const carX = interpolate(projected.p1.x, projected.p2.x, car.percent) +
                    carScale * car.offset * settings.roadWidth * width / 2;
                const carY = interpolate(projected.p1.y, projected.p2.y, car.percent);
                drawSprite(carContainer, car.spriteKind, carScale, carX, carY, -0.5, projected.clip, width);
            }

            for (const scenerySprite of sceneryBySegment.get(segment.index) ?? []) {
                drawSprite(
                    sceneryContainer,
                    scenerySprite.spriteKind,
                    projected.p1.scale,
                    projected.p1.x + projected.p1.scale * scenerySprite.offset * settings.roadWidth * width / 2,
                    projected.p1.y,
                    scenerySprite.offset < 0 ? -1 : 0,
                    projected.clip,
                    width);
            }

            if (segment.index === playerSegmentIndex) {
                const playerCameraY = interpolate(
                    segment.p1WorldY - cameraY,
                    segment.p2WorldY - cameraY,
                    playerPercent);
                const playerScale = cameraDepth / playerZ;
                const playerScreenY = height / 2 - playerScale * playerCameraY * height / 2;
                const playerKind = renderedPlayer.uphill
                    ? renderedPlayer.steer < 0 ? KIND.PLAYER_UPHILL_LEFT : renderedPlayer.steer > 0 ? KIND.PLAYER_UPHILL_RIGHT : KIND.PLAYER_UPHILL_STRAIGHT
                    : renderedPlayer.steer < 0 ? KIND.PLAYER_LEFT : renderedPlayer.steer > 0 ? KIND.PLAYER_RIGHT : KIND.PLAYER_STRAIGHT;
                const bounce = 1.5 * Math.random() * (renderedPlayer.speed / 60000) * settings.resolutionScale;
                drawSprite(playerContainer, playerKind, playerScale, width / 2, playerScreenY + bounce,
                    -0.5, height, width);
            }
        }

        hud.text = `${Math.round(5 * Math.round(renderedPlayer.speed / 500))} mph   ` +
            `Time: ${formatTime(renderedPlayer.currentLapTime)}   Lap: ${renderedPlayer.lap}` +
            (renderedPlayer.lastLapTime > 0 ? `   Last: ${formatTime(renderedPlayer.lastLapTime)}` : '');
        hud.position.set(16, 12);
        const viewport = document.getElementById('pixi-viewport');
        viewport?.setAttribute('data-racer-bounds', `${Math.round(width)}x${Math.round(height)}`);
    };

    const applySignal = (signal: RacerRenderSignal): void => {
        // New epoch = server-side restart: reset client-side parallax so the
        // wrap-around does not jump when player z snaps back to 0. The snapshot
        // buffers clear themselves on epoch change (see interpolation.ts).
        if (signal.epoch !== undefined && signal.epoch !== lastEpoch) {
            if (lastEpoch !== null) {
                skyOffset = 0;
                hillOffset = 0;
                treeOffset = 0;
                previousPosition = signal.player.z;
            }
            lastEpoch = signal.epoch;
        }
        stepMs = Math.max(1, signal.stepMs ?? 1000 / 60);
        player = signal.player;
        cars = signal.cars;
        settings = signal.settings;
        playerInterpolation.ingest([{ id: 0, ...signal.player }], signal.seq, signal.epoch);
        carInterpolation.ingest(signal.cars, signal.seq, signal.epoch);
        if (!tuningOpen) panel.update(settings);
        publishCSharpStats({ seq: signal.seq, entityCount: signal.entityCount, tickMs: signal.tickMs });
        if (signal.lapCompleted) dbg('lap completed:', player.lap, formatTime(player.lastLapTime));
        if (signal.collided) dbg('collision resolved by ECS');
    };

    const postInput = (): void => {
        fetch('/api/racer/input', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ left: leftDown, right: rightDown, faster: fasterDown, slower: slowerDown }),
        }).catch((error: unknown) => console.error('[pixi-debug] racer input failed:', error));
    };

    let leftDown = false;
    let rightDown = false;
    let fasterDown = false;
    let slowerDown = false;
    const setKey = (event: KeyboardEvent, down: boolean): boolean => {
        switch (event.key) {
            case 'ArrowLeft':
            case 'a':
            case 'A':
                leftDown = down;
                return true;
            case 'ArrowRight':
            case 'd':
            case 'D':
                rightDown = down;
                return true;
            case 'ArrowUp':
            case 'w':
            case 'W':
                fasterDown = down;
                return true;
            case 'ArrowDown':
            case 's':
            case 'S':
                slowerDown = down;
                return true;
            default:
                return false;
        }
    };
    const onKeyDown = (event: KeyboardEvent): void => {
        if (event.key === ' ' || event.key === 'Enter') {
            event.preventDefault();
            startGame();
            return;
        }
        if (setKey(event, true)) {
            event.preventDefault();
            postInput();
        }
    };
    const onKeyUp = (event: KeyboardEvent): void => {
        if (setKey(event, false)) {
            event.preventDefault();
            postInput();
        }
    };
    window.addEventListener('keydown', onKeyDown);
    window.addEventListener('keyup', onKeyUp);

    const stream = connectSignalStream(racer.streamUrl);
    stream?.addSignalListener('racer-move', (data) => {
        try {
            const parsed: unknown = JSON.parse(data);
            if (!isRacerRenderSignal(parsed)) return;
            applySignal(parsed);
        } catch (error: unknown) {
            console.error('[pixi-debug] racer-move parse failed:', error);
        }
    });
    stream?.onInterrupted(() => dbg('SSE connection error'));

    playerInterpolation.ingest([{ id: 0, ...player }]);
    carInterpolation.ingest(cars);
    sound.add(SOUND_ALIAS, SOUND_URL);
    // Hold the race behind the start overlay: pause the sim until START is
    // pressed (or Space/Enter). Music starts on START, not at boot.
    void postCommand('/api/racer/pause')
        .catch((error: unknown) => console.error('[pixi-debug] racer pause failed:', error));
    app.ticker.add(render);
    render();
    dbg('scene boot:', segments.length, 'segments,', cars.length, 'cars');

    const cleanup = (): void => {
        stream?.close();
        window.removeEventListener('keydown', onKeyDown);
        window.removeEventListener('keyup', onKeyUp);
        app.ticker.remove(render);
        sound.stop(SOUND_ALIAS);
        panel.element.remove();
        configButton.remove();
        overlay.remove();
        restartButton.remove();
        clearChildren(sceneryContainer);
        clearChildren(carContainer);
        clearChildren(playerContainer);
        for (const texture of textureCache.values()) texture.destroy();
        for (const texture of layerTextures) texture.destroy();
    };
    window.addEventListener('beforeunload', cleanup);
};

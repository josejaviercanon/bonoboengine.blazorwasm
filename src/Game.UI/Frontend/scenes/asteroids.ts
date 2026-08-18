import { Container, Graphics, ParticleContainer, Text, TextStyle, Texture } from 'pixi.js';
import type { Ticker } from 'pixi.js';
import { GlowFilter } from 'pixi-filters';
import { Emitter } from '@spd789562/particle-emitter';
import type { EmitterConfigV3 } from '@spd789562/particle-emitter';
import { sound } from '@pixi/sound';
import RAPIER from '@dimforge/rapier2d';
import type { SceneBuilder } from './types';
import { publishCSharpStats } from '../stats/overlays';
import { SnapshotBuffer, clampedDeltaSeconds, lerpAngle, lerpWrapped } from './interpolation';

interface AsteroidSpriteState {
    id: number;
    x: number;
    y: number;
    rotation: number;
    vx: number;
    vy: number;
    kind: number;
    size: number;
    r: number;
    g: number;
    b: number;
}

interface AsteroidsRenderSignal {
    seq: number;
    entityCount: number;
    tickMs: number;
    stepMs?: number;
    epoch?: number;
    sprites: AsteroidSpriteState[];
    score: number;
    highScore: number;
    lives: number;
    level: number;
    gameOver: boolean;
    started: boolean;
    thrustOn: boolean;
    exploded: boolean;
    fired: boolean;
    saucerSpawned: boolean;
    levelUp: boolean;
    lifeGained: boolean;
}

interface AsteroidsSceneParams {
    /** Nested asteroids state from the SSR payload (camelCase of AsteroidsScenePayload). */
    asteroids?: {
        sprites?: AsteroidSpriteState[];
        score?: number;
        highScore?: number;
        lives?: number;
        level?: number;
        gameOver?: boolean;
        started?: boolean;
        courtWidth?: number;
        courtHeight?: number;
        streamUrl?: string;
    };
}

// Kind discriminator: MUST match AsteroidsSpriteKind in Game.Engine (C# is the source of truth).
const KIND_SHIP = 0;
const KIND_ASTEROID = 1;
const KIND_BULLET = 2;
const KIND_SAUCER = 3;
const KIND_MISSILE = 4;
const KIND_EXPLOSION = 5;

// Ship polygon template (reference game scaled 0.08). Nose points up (y-down).
const SHIP_POINTS: ReadonlyArray<readonly [number, number]> = [
    [0, -16], [4, 0], [8, 16], [2.4, 9.6], [-2.4, 9.6], [-8, 16], [-4, 0],
];

// Saucer polygon template (reference game scaled 0.08).
const SAUCER_POINTS: ReadonlyArray<readonly [number, number]> = [
    [-24, 0], [-12, -6], [-6, -6], [-6, -12], [6, -12], [6, -6], [12, -6], [24, 0], [12, 6], [-12, 6],
];

const SOUND_BASE_URL = '_content/Game.UI/audio/';
const soundRegistered = new Set<string>();

function ensureSound(alias: string, name: string): void {
    if (soundRegistered.has(alias)) return;
    sound.add(alias, `${SOUND_BASE_URL}${name}`);
    soundRegistered.add(alias);
}

function playSound(alias: string, name: string): void {
    ensureSound(alias, name);
    void sound.play(alias);
}

function loopSound(alias: string, name: string, loop: boolean): void {
    ensureSound(alias, name);
    if (loop) {
        void sound.play(alias, { loop: true, volume: 0.25 });
    } else {
        sound.stop(alias);
    }
}

const dbg = (...args: unknown[]) => console.log('[pixi-debug] asteroids:', ...args);

/** Deterministic per-vertex jitter for an asteroid polygon (stable per render id). */
function asteroidVertexRadius(id: number, vertex: number, size: number): number {
    const hash = Math.sin(id * 127.1 + vertex * 311.7) * 43758.5453;
    const jitter = hash - Math.floor(hash);
    return size * (0.75 + 0.45 * jitter);
}

function rotatePoint(x: number, y: number, cos: number, sin: number): [number, number] {
    return [x * cos - y * sin, x * sin + y * cos];
}

/** Small radial-gradient glow dot shared by every particle emitter (single TextureSource). */
function makeParticleTexture(): Texture {
    const canvas = document.createElement('canvas');
    canvas.width = 32;
    canvas.height = 32;
    const context = canvas.getContext('2d');
    if (context) {
        const gradient = context.createRadialGradient(16, 16, 0, 16, 16, 16);
        gradient.addColorStop(0, 'rgba(255,255,255,1)');
        gradient.addColorStop(0.4, 'rgba(255,255,255,0.8)');
        gradient.addColorStop(1, 'rgba(255,255,255,0)');
        context.fillStyle = gradient;
        context.fillRect(0, 0, 32, 32);
    }
    return Texture.from(canvas);
}

const explosionConfig = (texture: Texture): EmitterConfigV3 => ({
    lifetime: { min: 0.35, max: 0.7 },
    frequency: 0,
    particlesPerWave: 70,
    emitterLifetime: 0.06,
    maxParticles: 160,
    pos: { x: 0, y: 0 },
    addAtBack: false,
    autoUpdate: false,
    behaviors: [
        {
            type: 'spawnShape',
            config: {
                type: 'torus',
                data: { x: 0, y: 0, radius: 2, affectRotation: true },
            },
        },
        {
            type: 'moveSpeed',
            config: {
                speed: {
                    list: [
                        { value: 420, time: 0 },
                        { value: 40, time: 1 },
                    ],
                },
                minMult: 0.6,
            },
        },
        {
            type: 'alpha',
            config: {
                alpha: {
                    list: [
                        { value: 1, time: 0 },
                        { value: 0, time: 1 },
                    ],
                },
            },
        },
        {
            type: 'scale',
            config: {
                scale: {
                    list: [
                        { value: 1, time: 0 },
                        { value: 0.25, time: 1 },
                    ],
                },
                minMult: 0.5,
            },
        },
        {
            type: 'color',
            config: {
                color: {
                    list: [
                        { value: 'ffffff', time: 0 },
                        { value: 'ff8c00', time: 0.6 },
                        { value: '442200', time: 1 },
                    ],
                },
            },
        },
        {
            type: 'rotationStatic',
            config: { min: 0, max: 360 },
        },
        {
            type: 'textureSingle',
            config: { texture },
        },
    ],
});

const flameConfig = (texture: Texture): EmitterConfigV3 => ({
    lifetime: { min: 0.18, max: 0.32 },
    frequency: 0.008,
    spawnChance: 1,
    particlesPerWave: 1,
    maxParticles: 120,
    pos: { x: 0, y: 0 },
    addAtBack: false,
    autoUpdate: false,
    behaviors: [
        {
            type: 'spawnShape',
            config: {
                type: 'rect',
                data: { x: -2, y: 0, w: 4, h: 2 },
            },
        },
        {
            type: 'moveSpeed',
            config: {
                speed: {
                    list: [
                        { value: 240, time: 0 },
                        { value: 20, time: 1 },
                    ],
                },
                minMult: 0.5,
            },
        },
        {
            type: 'alpha',
            config: {
                alpha: {
                    list: [
                        { value: 0.9, time: 0 },
                        { value: 0, time: 1 },
                    ],
                },
            },
        },
        {
            type: 'scale',
            config: {
                scale: {
                    list: [
                        { value: 1.2, time: 0 },
                        { value: 0.3, time: 1 },
                    ],
                },
                minMult: 0.5,
            },
        },
        {
            type: 'color',
            config: {
                color: {
                    list: [
                        { value: 'ffffff', time: 0 },
                        { value: 'ffa500', time: 0.4 },
                        { value: 'ff3300', time: 1 },
                    ],
                },
            },
        },
        {
            type: 'rotationStatic',
            config: { min: 168, max: 192 },
        },
        {
            type: 'textureSingle',
            config: { texture },
        },
    ],
});

interface DebrisBody {
    body: RAPIER.RigidBody;
    radius: number;
    born: number;
}

function isAsteroidSpriteState(value: unknown): value is AsteroidSpriteState {
    if (!value || typeof value !== 'object') return false;
    const state = value as Partial<AsteroidSpriteState>;
    return typeof state.id === 'number' && typeof state.x === 'number' && typeof state.y === 'number' &&
        typeof state.rotation === 'number' && typeof state.kind === 'number' && typeof state.size === 'number';
}

function isAsteroidsRenderSignal(value: unknown): value is AsteroidsRenderSignal {
    if (!value || typeof value !== 'object') return false;
    const signal = value as Partial<AsteroidsRenderSignal>;
    return typeof signal.seq === 'number' && typeof signal.entityCount === 'number' &&
        typeof signal.tickMs === 'number' && Array.isArray(signal.sprites) &&
        signal.sprites.every(isAsteroidSpriteState) && typeof signal.score === 'number' &&
        typeof signal.highScore === 'number' && typeof signal.lives === 'number' &&
        typeof signal.level === 'number' && typeof signal.gameOver === 'boolean' &&
        typeof signal.started === 'boolean' && typeof signal.thrustOn === 'boolean';
}

/**
 * Asteroids scene: the C# simulation owns the court (Box2D.NET authoritative physics,
 * ADR-002) and pushes one batched signal per 60 Hz tick over SSE. This scene only
 * interpolates and renders vector sprites (ADR-003/005), forwards held controls as
 * suggestions, and runs the presentation layer: particle-emitter bursts, Rapier
 * debris (visual only) and a neon GlowFilter. C# is the sole authority.
 */
export const asteroidsScene: SceneBuilder = (app, params) => {
    const a = ((params ?? {}) as AsteroidsSceneParams).asteroids ?? {};
    app.renderer.background.color = '#020617';

    const courtWidth = a.courtWidth ?? 800;
    const courtHeight = a.courtHeight ?? 600;

    const court = new Container();
    const scale = Math.min(app.screen.width / courtWidth, app.screen.height / courtHeight);
    court.scale.set(scale);
    court.x = (app.screen.width - courtWidth * scale) / 2;
    court.y = (app.screen.height - courtHeight * scale) / 2;
    app.stage.addChild(court);

    const world = new Graphics();
    court.addChild(world);

    // Neon vector glow for everything in the court (pixi-filters).
    court.filters = [
        new GlowFilter({
            distance: 12,
            outerStrength: 2.2,
            innerStrength: 0.6,
            color: 0x4da6ff,
            quality: 0.5,
        }),
    ];

    const scoreText = new Text({
        text: 'SCORE: 000000',
        style: new TextStyle({ fontFamily: 'monospace', fontSize: 16, fontWeight: 'bold', fill: '#e2e8f0' }),
    });
    scoreText.position.set(12, 12);
    app.stage.addChild(scoreText);

    const hiText = new Text({
        text: 'HI: 000000',
        style: new TextStyle({ fontFamily: 'monospace', fontSize: 16, fontWeight: 'bold', fill: '#64748b' }),
    });
    hiText.anchor.set(0.5, 0);
    hiText.position.set(app.screen.width / 2, 12);
    app.stage.addChild(hiText);

    const livesText = new Text({
        text: '^'.repeat(2),
        style: new TextStyle({ fontFamily: 'monospace', fontSize: 18, fontWeight: 'bold', fill: '#34d399' }),
    });
    livesText.anchor.set(1, 0);
    livesText.position.set(app.screen.width - 12, 12);
    app.stage.addChild(livesText);

    const overlay = document.createElement('div');
    overlay.style.cssText =
        'position:fixed;top:52px;left:0;right:0;bottom:0;display:flex;flex-direction:column;' +
        'align-items:center;justify-content:center;gap:1rem;background:rgba(2,6,23,0.55);z-index:5;';
    const overlayTitle = document.createElement('div');
    overlayTitle.style.cssText = 'font:bold 2rem sans-serif;color:#4da6ff;text-align:center;';
    const startButton = document.createElement('button');
    startButton.type = 'button';
    startButton.textContent = 'START GAME';
    startButton.style.cssText =
        'background-color:#4da6ff;color:#020617;border:none;border-radius:0.5rem;' +
        'padding:0.75rem 2rem;font-size:1.1rem;font-weight:bold;cursor:pointer;';
    const hint = document.createElement('div');
    hint.style.cssText = 'color:#94a3b8;font:0.85rem sans-serif;text-align:center;';
    hint.textContent = 'ARROWS/A-D rotate · W/UP thrust · SPACE fire · H hyperspace';
    overlay.append(overlayTitle, startButton, hint);
    document.body.appendChild(overlay);

    let started = a.started ?? false;
    let gameOver = a.gameOver ?? false;
    let score = a.score ?? 0;
    let highScore = a.highScore ?? 0;
    let lives = a.lives ?? 3;
    let thrustOn = false;
    let prevGameOver = gameOver;

    const updateOverlay = () => {
        if (started && !gameOver) {
            overlay.style.display = 'none';
            return;
        }
        overlay.style.display = 'flex';
        overlayTitle.textContent = gameOver ? `GAME OVER - SCORE: ${score}` : 'ASTEROIDS';
        startButton.textContent = gameOver ? 'PLAY AGAIN' : 'START GAME';
    };

    const startGame = () => {
        if (started && !gameOver) return;
        dbg('starting game (button or space)');
        fetch('/api/asteroids/start', { method: 'POST' })
            .then(() => {
                started = true;
                gameOver = false;
                updateOverlay();
            })
            .catch((err) => console.error('[pixi-debug] asteroids start failed:', err));
    };
    startButton.addEventListener('click', startGame);

    // --- Interpolation state (ADR-003): prev/curr snapshots lerped at display Hz. ---
    const interpolation = new SnapshotBuffer<AsteroidSpriteState>();
    let stepMs = 1000 / 60;
    let renderedShip: AsteroidSpriteState | null = null;

    // --- Presentation particles: one ParticleContainer, one texture source. ----------
    const particleContainer = new ParticleContainer({
        dynamicProperties: { position: true, rotation: true, scale: true, color: true },
    });
    court.addChild(particleContainer);
    const particleTexture = makeParticleTexture();

    let flameEmitter: Emitter | null = null;
    const flameInit = () => {
        flameEmitter = new Emitter(particleContainer, flameConfig(particleTexture), particleTexture);
        flameEmitter.updateOwnerPos(courtWidth / 2, courtHeight / 2);
        flameEmitter.emit = false;
    };
    flameInit();

    const oneShotEmitters: Emitter[] = [];
    const ignitedExplosions = new Set<number>();

    const burstAt = (x: number, y: number) => {
        const emitter = new Emitter(particleContainer, explosionConfig(particleTexture), particleTexture);
        emitter.updateOwnerPos(x, y);
        emitter.playOnceAndDestroy(() => {
            const index = oneShotEmitters.indexOf(emitter);
            if (index >= 0) oneShotEmitters.splice(index, 1);
            emitter.destroy();
        });
        oneShotEmitters.push(emitter);
    };

    // --- Presentation physics: Rapier debris (visual only, ADR-002/005). ------------
    let physicsWorld: RAPIER.World | null = null;
    const debris: DebrisBody[] = [];
    const debrisLayer = new Graphics();
    court.addChild(debrisLayer);

    const initPhysics = async () => {
        if (physicsWorld) return;
        const initFn = (RAPIER as unknown as { init?: () => Promise<void> }).init;
        if (initFn) await initFn();
        physicsWorld = new RAPIER.World({ x: 0, y: 0 });
        dbg('Rapier world initialized');
    };

    const spawnDebris = async (x: number, y: number) => {
        await initPhysics();
        if (!physicsWorld) return;
        for (let i = 0; i < 10; i++) {
            const radius = 2 + Math.random() * 4;
            const angle = Math.random() * Math.PI * 2;
            const speed = 120 + Math.random() * 240;
            const body = physicsWorld.createRigidBody(
                RAPIER.RigidBodyDesc.dynamic()
                    .setTranslation(x, y)
                    .setLinvel(Math.cos(angle) * speed, Math.sin(angle) * speed),
            );
            physicsWorld.createCollider(
                RAPIER.ColliderDesc.cuboid(radius, radius).setRestitution(0.6).setFriction(0),
                body,
            );
            debris.push({ body, radius, born: performance.now() });
        }
    };

    // --- Rendering ----------------------------------------------------------------

    const drawShip = (g: Graphics, state: AsteroidSpriteState, color: number) => {
        const cos = Math.cos(state.rotation);
        const sin = Math.sin(state.rotation);
        const pts: number[] = [];
        for (const [tx, ty] of SHIP_POINTS) {
            const [rx, ry] = rotatePoint(tx, ty, cos, sin);
            pts.push(rx + state.x, ry + state.y);
        }
        g.poly(pts).fill(color);
    };

    const drawAsteroid = (g: Graphics, state: AsteroidSpriteState, color: number) => {
        const pts: number[] = [];
        const radius = state.size > 0 ? state.size : 18;
        for (let i = 0; i < 9; i++) {
            const angle = state.rotation + (i * (Math.PI * 2)) / 9;
            const r = asteroidVertexRadius(state.id, i, radius);
            pts.push(state.x + Math.cos(angle) * r, state.y + Math.sin(angle) * r);
        }
        g.poly(pts).stroke({ width: 2, color });
    };

    const drawSaucer = (g: Graphics, state: AsteroidSpriteState, color: number) => {
        const cos = Math.cos(state.rotation);
        const sin = Math.sin(state.rotation);
        const pts: number[] = [];
        for (const [tx, ty] of SAUCER_POINTS) {
            const [rx, ry] = rotatePoint(tx, ty, cos, sin);
            pts.push(rx + state.x, ry + state.y);
        }
        g.poly(pts).stroke({ width: 2, color });
    };

    const drawMissile = (g: Graphics, state: AsteroidSpriteState, color: number) => {
        const cos = Math.cos(state.rotation);
        const sin = Math.sin(state.rotation);
        const [nx, ny] = rotatePoint(0, -8, cos, sin);
        const [tx2, ty2] = rotatePoint(0, 8, cos, sin);
        g.moveTo(state.x - nx, state.y - ny).lineTo(state.x + tx2, state.y + ty2).stroke({ width: 2, color });
    };

    const drawExplosion = (g: Graphics, state: AsteroidSpriteState, color: number) => {
        const fraction = Math.max(0, Math.min(1, state.size));
        const radius = 8 + fraction * 44;
        g.circle(state.x, state.y, radius).stroke({ width: 2, color, alpha: 1 - fraction * 0.9 });
    };

    const drawWorld = () => {
        const alpha = interpolation.alpha(stepMs);
        renderedShip = null;
        world.clear();
        for (const entry of interpolation.values()) {
            const { previous: prev, current: curr } = entry;
            const x = lerpWrapped(prev.x, curr.x, alpha, courtWidth);
            const y = lerpWrapped(prev.y, curr.y, alpha, courtHeight);
            const rotation = lerpAngle(prev.rotation, curr.rotation, alpha);
            const color = (curr.r << 16) | (curr.g << 8) | curr.b;
            const state: AsteroidSpriteState = { ...curr, x, y, rotation };
            if (curr.kind === KIND_SHIP) renderedShip = state;

            switch (curr.kind) {
                case KIND_SHIP:
                    drawShip(world, state, color);
                    break;
                case KIND_ASTEROID:
                    drawAsteroid(world, state, color);
                    break;
                case KIND_BULLET:
                    world.circle(x, y, 2.5).fill(color);
                    break;
                case KIND_SAUCER:
                    drawSaucer(world, state, color);
                    break;
                case KIND_MISSILE:
                    drawMissile(world, state, color);
                    break;
                case KIND_EXPLOSION:
                    drawExplosion(world, state, color);
                    break;
            }
        }
    };

    const setGameState = (nextScore: number, nextHigh: number, nextLives: number, over: boolean, isStarted: boolean) => {
        scoreText.text = `SCORE: ${String(nextScore).padStart(6, '0')}`;
        hiText.text = `HI: ${String(nextHigh).padStart(6, '0')}`;
        livesText.text = '^'.repeat(Math.max(0, nextLives - 1));
        score = nextScore;
        highScore = nextHigh;
        lives = nextLives;
        gameOver = over;
        started = isStarted;
        if (gameOver && !prevGameOver) {
            dbg('game ended (ECS signal) - score', score);
            playSound('asteroids-endgame', 'asteroids-explode3.wav');
        }
        prevGameOver = gameOver;
        updateOverlay();
    };

    interpolation.ingest((a.sprites ?? []).filter(isAsteroidSpriteState));
    setGameState(score, highScore, lives, gameOver, started);

    // --- Input ----------------------------------------------------------------------
    let thrustDown = false;
    let leftDown = false;
    let rightDown = false;

    const postInput = (thrust: boolean, left: boolean, right: boolean, fire: boolean, hyperspace: boolean) => {
        fetch('/api/asteroids/input', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ thrust, left, right, fire, hyperspace }),
        }).catch((err) => console.error('[pixi-debug] asteroids input failed:', err));
    };

    const onKeyDown = (event: KeyboardEvent) => {
        if (event.key === ' ' || event.key === 'Enter') {
            event.preventDefault();
            if (started && !gameOver) {
                if (event.key === ' ' && !event.repeat) postInput(thrustDown, leftDown, rightDown, true, false);
            } else {
                startGame();
            }
            return;
        }
        if (event.key === 'ArrowUp' || event.key === 'w' || event.key === 'W') {
            event.preventDefault();
            thrustDown = true;
            postInput(true, leftDown, rightDown, false, false);
            return;
        }
        if (event.key === 'ArrowLeft' || event.key === 'a' || event.key === 'A') {
            event.preventDefault();
            leftDown = true;
            postInput(thrustDown, true, rightDown, false, false);
            return;
        }
        if (event.key === 'ArrowRight' || event.key === 'd' || event.key === 'D') {
            event.preventDefault();
            rightDown = true;
            postInput(thrustDown, leftDown, true, false, false);
            return;
        }
        if (event.key === 'h' || event.key === 'H') {
            event.preventDefault();
            if (!event.repeat) postInput(thrustDown, leftDown, rightDown, false, true);
        }
    };

    const onKeyUp = (event: KeyboardEvent) => {
        if (event.key === 'ArrowUp' || event.key === 'w' || event.key === 'W') {
            thrustDown = false;
            postInput(false, leftDown, rightDown, false, false);
        } else if (event.key === 'ArrowLeft' || event.key === 'a' || event.key === 'A') {
            leftDown = false;
            postInput(thrustDown, false, rightDown, false, false);
        } else if (event.key === 'ArrowRight' || event.key === 'd' || event.key === 'D') {
            rightDown = false;
            postInput(thrustDown, leftDown, false, false, false);
        }
    };
    window.addEventListener('keydown', onKeyDown);
    window.addEventListener('keyup', onKeyUp);

    // --- Ticker: interpolation render + flame follow + particles + Rapier debris ----
    const onTicker = (ticker: Ticker) => {
        const dt = clampedDeltaSeconds(ticker.deltaMS);

        drawWorld();

        // Thrust flame follows the interpolated ship tail.
        if (flameEmitter) {
            const ship = renderedShip;
            if (ship && thrustOn && started && !gameOver) {
                const tailX = ship.x - Math.sin(ship.rotation) * 14;
                const tailY = ship.y + Math.cos(ship.rotation) * 14;
                flameEmitter.updateOwnerPos(tailX, tailY);
                flameEmitter.rotate((ship.rotation * 180) / Math.PI + 180);
                flameEmitter.emit = true;
            } else {
                flameEmitter.emit = false;
            }
            flameEmitter.update(dt);
        }

        for (const emitter of oneShotEmitters) {
            emitter.update(dt);
        }

        // Rapier debris (presentation only).
        if (physicsWorld && debris.length > 0) {
            physicsWorld.timestep = dt;
            physicsWorld.step();
            const now = performance.now();
            debrisLayer.clear();
            for (let i = debris.length - 1; i >= 0; i--) {
                const piece = debris[i];
                if (now - piece.born > 1800) {
                    physicsWorld.removeRigidBody(piece.body);
                    debris.splice(i, 1);
                    continue;
                }
                const pos = piece.body.translation();
                debrisLayer.circle(pos.x, pos.y, piece.radius).fill(0xffa07a);
            }
        }
    };
    app.ticker.add(onTicker);

    dbg('scene boot: screen', app.screen.width, 'x', app.screen.height,
        'court', courtWidth, 'x', courtHeight,
        'sprites', (a.sprites ?? []).length,
        'started', started, 'gameOver', gameOver,
        'stream', a.streamUrl);

    if (!a.streamUrl) return;

    const source = new EventSource(a.streamUrl);
    dbg('SSE connected:', a.streamUrl);
    source.addEventListener('asteroids-move', (event) => {
        try {
            const parsed: unknown = JSON.parse((event as MessageEvent<string>).data);
            if (!isAsteroidsRenderSignal(parsed)) throw new Error('invalid asteroids render signal');
            const signal = parsed;
            publishCSharpStats({ seq: signal.seq, entityCount: signal.entityCount, tickMs: signal.tickMs });
            stepMs = Math.max(1, signal.stepMs ?? 1000 / 60);
            interpolation.ingest(signal.sprites, signal.seq, signal.epoch);
            setGameState(signal.score, signal.highScore, signal.lives, signal.gameOver, signal.started);

            if (signal.exploded) {
                for (const state of signal.sprites) {
                    if (state.kind !== KIND_EXPLOSION || ignitedExplosions.has(state.id)) continue;
                    ignitedExplosions.add(state.id);
                    burstAt(state.x, state.y);
                    void spawnDebris(state.x, state.y);
                    playSound(`asteroids-explode${1 + Math.floor(Math.random() * 3)}`,
                        `asteroids-explode${1 + Math.floor(Math.random() * 3)}.wav`);
                }
            }
            if (signal.fired) {
                playSound('asteroids-fire', 'asteroids-fire.wav');
            }
            if (signal.saucerSpawned) {
                dbg('ECS event: saucer spawned');
                playSound('asteroids-ssaucer', 'asteroids-ssaucer.wav');
            }
            if (signal.lifeGained) {
                dbg('ECS event: extra ship');
                playSound('asteroids-life', 'asteroids-life.wav');
            }
            if (signal.levelUp) {
                dbg('ECS event: new belt, level', signal.level);
                playSound('asteroids-thumphi', 'asteroids-thumphi.wav');
            }
            if (signal.thrustOn !== thrustOn) {
                thrustOn = signal.thrustOn;
                loopSound('asteroids-thrust', 'asteroids-thrust.wav', thrustOn);
            }
        } catch (err) {
            console.error('[pixi-debug] asteroids-move parse failed:', err);
        }
    });

    const cleanup = () => {
        source.close();
        window.removeEventListener('keydown', onKeyDown);
        window.removeEventListener('keyup', onKeyUp);
        app.ticker.remove(onTicker);
        if (flameEmitter) {
            flameEmitter.destroy();
            flameEmitter = null;
        }
        for (const emitter of oneShotEmitters) {
            emitter.destroy();
        }
        oneShotEmitters.length = 0;
        if (physicsWorld) {
            physicsWorld.free();
        }
        physicsWorld = null;
        debris.length = 0;
        sound.stop('asteroids-thrust');
        overlay.remove();
    };
    source.onerror = () => cleanup();
    window.addEventListener('beforeunload', cleanup);
};

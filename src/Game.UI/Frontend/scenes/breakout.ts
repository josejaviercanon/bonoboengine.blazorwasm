import { Graphics, Text, TextStyle } from 'pixi.js';
import { sound } from '@pixi/sound';
import type { SceneBuilder } from './types';
import { SnapshotBuffer, lerp } from './interpolation';

interface BreakoutSpriteState {
    id: number;
    x: number;
    y: number;
    width: number;
    height: number;
    r: number;
    g: number;
    b: number;
}

interface BreakoutRenderSignal {
    seq: number;
    entityCount: number;
    tickMs: number;
    stepMs?: number;
    epoch?: number;
    sprites: BreakoutSpriteState[];
    score: number;
    lives: number;
    level: number;
    gameOver: boolean;
    started: boolean;
    brickHit: boolean;
    paddleHit: boolean;
    levelUp: boolean;
    loseLife: boolean;
}

interface BreakoutSceneParams {
    /** Nested breakout state from the SSR payload (camelCase of BreakoutScenePayload). */
    breakout?: {
        sprites?: BreakoutSpriteState[];
        score?: number;
        lives?: number;
        level?: number;
        gameOver?: boolean;
        started?: boolean;
        courtWidth?: number;
        courtHeight?: number;
        chunkSize?: number;
        streamUrl?: string;
    };
}

const BRICK_SOUND_ALIAS = 'breakout-brick';
const PADDLE_SOUND_ALIAS = 'breakout-paddle';
const LEVELUP_SOUND_ALIAS = 'breakout-levelup';
const LOSELIFE_SOUND_ALIAS = 'breakout-loselife';
const GAMEOVER_SOUND_ALIAS = 'breakout-gameover';
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

const dbg = (...args: unknown[]) => console.log('[pixi-debug] breakout:', ...args);

/**
 * Breakout scene: the C# simulation owns the court (bricks/paddle/ball as ECS entities)
 * and pushes one batched signal per 60 Hz physics tick over SSE. This scene only renders
 * sprites, forwards held paddle input + launch as a suggestion, and reacts to ECS edge
 * events (brick/paddle/levelup/loselife/gameover sounds). C# is the sole authority.
 */
export const breakoutScene: SceneBuilder = (app, params) => {
    const b = ((params ?? {}) as BreakoutSceneParams).breakout ?? {};
    // Opaque dark clear: canvas is opaque (backgroundAlpha: 1 at init), so the board
    // renders on a solid backdrop without needing a covering background rect.
    app.renderer.background.color = '#020617';

    const courtWidth = b.courtWidth ?? 600;
    const courtHeight = b.courtHeight ?? 500;

    const court = new Graphics();
    const scale = Math.min(app.screen.width / courtWidth, app.screen.height / courtHeight);
    court.scale.set(scale);
    // Top-left aligned in the viewport (no centering): keeps the board inside the
    // visible canvas region on every display/DPR combination.
    court.x = 0;
    court.y = 0;
    app.stage.addChild(court);

    const border = new Graphics();
    border.rect(0, 0, courtWidth, courtHeight).stroke({ width: 4, color: '#8B0000' });
    border.scale.set(scale);
    border.x = court.x;
    border.y = court.y;
    app.stage.addChild(border);

    const hudText = new Text({
        text: 'Score: 0  Lives: 3  Level: 1',
        style: new TextStyle({ fontFamily: 'Arial', fontSize: 18, fontWeight: 'bold', fill: '#e2e8f0' }),
    });
    hudText.anchor.set(1, 0);
    hudText.position.set(app.screen.width - 16, 12);
    app.stage.addChild(hudText);

    const overlay = document.createElement('div');
    overlay.style.cssText =
        'position:fixed;top:52px;left:0;right:0;bottom:0;display:flex;flex-direction:column;' +
        'align-items:center;justify-content:center;gap:1rem;background:rgba(2,6,23,0.55);z-index:5;';
    const overlayTitle = document.createElement('div');
    overlayTitle.style.cssText = 'font:bold 2rem sans-serif;color:#f97316;text-align:center;';
    const startButton = document.createElement('button');
    startButton.type = 'button';
    startButton.textContent = 'START GAME';
    startButton.style.cssText =
        'background-color:#f97316;color:#020617;border:none;border-radius:0.5rem;' +
        'padding:0.75rem 2rem;font-size:1.1rem;font-weight:bold;cursor:pointer;';
    const hint = document.createElement('div');
    hint.style.cssText = 'color:#94a3b8;font:0.85rem sans-serif;';
    hint.textContent = 'arrow keys move · SPACE launches the ball';
    overlay.append(overlayTitle, startButton, hint);
    document.body.appendChild(overlay);

    let started = b.started ?? false;
    let gameOver = b.gameOver ?? false;
    let score = b.score ?? 0;
    let lives = b.lives ?? 3;
    let level = b.level ?? 0;
    let prevGameOver = gameOver;
    let leftDown = false;
    let rightDown = false;
    let stepMs = 1000 / 60;
    const interpolation = new SnapshotBuffer<BreakoutSpriteState>();

    const logTransitions = () => {
        if (gameOver && !prevGameOver) {
            dbg('game ended (ECS signal) - score', score);
            playSound(GAMEOVER_SOUND_ALIAS, 'breakout-gameover.mp3');
        }
        prevGameOver = gameOver;
    };

    const updateOverlay = () => {
        if (started && !gameOver) {
            overlay.style.display = 'none';
            return;
        }
        overlay.style.display = 'flex';
        overlayTitle.textContent = gameOver ? `GAME OVER - SCORE: ${score}` : 'BREAKOUT';
        startButton.textContent = gameOver ? 'PLAY AGAIN' : 'START GAME';
    };

    const postInput = (left: boolean, right: boolean, launch: boolean) => {
        fetch('/api/breakout/input', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ left, right, launch }),
        }).catch((err) => console.error('[pixi-debug] breakout input failed:', err));
    };

    const startGame = () => {
        if (started && !gameOver) return;
        dbg('starting game (button or space)');
        fetch('/api/breakout/start', { method: 'POST' })
            .then(() => {
                started = true;
                gameOver = false;
                dbg('game started (sim confirmed)');
                updateOverlay();
            })
            .catch((err) => console.error('[pixi-debug] breakout start failed:', err));
    };
    startButton.addEventListener('click', startGame);

    const draw = () => {
        const alpha = interpolation.alpha(stepMs);
        court.clear();
        for (const entry of interpolation.values()) {
            const state = entry.current;
            const x = lerp(entry.previous.x, state.x, alpha);
            const y = lerp(entry.previous.y, state.y, alpha);
            const color = (state.r << 16) | (state.g << 8) | state.b;
            if (state.id === 2) {
                // Ball (sim renders it with render id 2): circular.
                court.circle(x, y, state.width / 2).fill(color);
            } else {
                // Bricks + paddle: rectangles centered on (x, y).
                court.rect(x - state.width / 2, y - state.height / 2, state.width, state.height).fill(color);
            }
        }
    };

    const setGameState = (nextScore: number, nextLives: number, nextLevel: number, over: boolean, isStarted: boolean) => {
        hudText.text = `Score: ${nextScore}  Lives: ${nextLives}  Level: ${nextLevel + 1}`;
        score = nextScore;
        lives = nextLives;
        level = nextLevel;
        gameOver = over;
        started = isStarted;
        logTransitions();
        updateOverlay();
    };

    interpolation.ingest(b.sprites ?? []);
    draw();
    const onTicker = () => draw();
    app.ticker.add(onTicker);
    setGameState(score, lives, level, gameOver, started);

    dbg('scene boot: screen', app.screen.width, 'x', app.screen.height,
        'sprites', (b.sprites ?? []).length,
        'bricks', (b.sprites ?? []).filter(s => s.id >= 1000).length,
        'score', score, 'lives', lives, 'level', level + 1, 'started', started, 'gameOver', gameOver,
        'stream', b.streamUrl);

    // Render-verification hook: publish the court geometry bounds so E2E can assert
    // the board actually built geometry (catches payload-contract mismatches).
    app.ticker.addOnce(() => {
        const cb = court.getBounds();
        const viewportEl = document.getElementById('pixi-viewport');
        viewportEl?.setAttribute('data-court-bounds', `${Math.round(cb.width)}x${Math.round(cb.height)}`);
    });

    const onKeyDown = (event: KeyboardEvent) => {
        if (event.key === ' ' || event.key === 'Enter') {
            event.preventDefault();
            if (started && !gameOver) {
                dbg('launch ball');
                postInput(leftDown, rightDown, true);
            } else {
                startGame();
            }
            return;
        }
        if (event.key === 'ArrowLeft' || event.key === 'a' || event.key === 'A') {
            event.preventDefault();
            leftDown = true;
            postInput(true, rightDown, false);
            return;
        }
        if (event.key === 'ArrowRight' || event.key === 'd' || event.key === 'D') {
            event.preventDefault();
            rightDown = true;
            postInput(leftDown, true, false);
        }
    };

    const onKeyUp = (event: KeyboardEvent) => {
        if (event.key === 'ArrowLeft' || event.key === 'a' || event.key === 'A') {
            leftDown = false;
            postInput(false, rightDown, false);
        } else if (event.key === 'ArrowRight' || event.key === 'd' || event.key === 'D') {
            rightDown = false;
            postInput(leftDown, false, false);
        }
    };
    window.addEventListener('keydown', onKeyDown);
    window.addEventListener('keyup', onKeyUp);

    if (!b.streamUrl) return;

    const source = new EventSource(b.streamUrl);
    dbg('SSE connected:', b.streamUrl);
    source.addEventListener('breakout-move', (event) => {
        try {
            const signal = JSON.parse((event as MessageEvent<string>).data) as BreakoutRenderSignal;
            stepMs = Math.max(1, signal.stepMs ?? 1000 / 60);
            interpolation.ingest(signal.sprites, signal.seq, signal.epoch);
            setGameState(signal.score, signal.lives, signal.level, signal.gameOver, signal.started);
            if (signal.brickHit) {
                dbg('event: brick hit - score', signal.score);
                playSound(BRICK_SOUND_ALIAS, 'breakout-brick.mp3');
            }
            if (signal.paddleHit) {
                dbg('event: paddle hit');
                playSound(PADDLE_SOUND_ALIAS, 'breakout-paddle.mp3');
            }
            if (signal.levelUp) {
                dbg('event: level up -> level', signal.level + 1);
                playSound(LEVELUP_SOUND_ALIAS, 'breakout-levelup.mp3');
            }
            if (signal.loseLife) {
                dbg('event: lost a life -> lives', signal.lives);
                playSound(LOSELIFE_SOUND_ALIAS, 'breakout-loselife.mp3');
            }
        } catch (err) {
            console.error('[pixi-debug] breakout-move parse failed:', err);
        }
    });
    const cleanup = () => {
        source.close();
        window.removeEventListener('keydown', onKeyDown);
        window.removeEventListener('keyup', onKeyUp);
        app.ticker.remove(onTicker);
        overlay.remove();
    };
    source.onerror = () => cleanup();
    window.addEventListener('beforeunload', cleanup);
};

import { Graphics, Text, TextStyle } from 'pixi.js';
import type { Ticker } from 'pixi.js';
import { sound } from '@pixi/sound';
import type { SceneBuilder } from './types';
import { publishCSharpStats } from '../stats/overlays';
import { SnapshotBuffer } from './interpolation';

interface SnakeSpriteState {
    id: number;
    x: number;
    y: number;
    previousX: number;
    previousY: number;
    velocityX: number;
    velocityY: number;
    kind: number;
    r: number;
    g: number;
    b: number;
}

interface SnakeRenderSignal {
    seq: number;
    entityCount: number;
    tickMs: number;
    stepMs: number;
    epoch?: number;
    sprites: SnakeSpriteState[];
    score: number;
    gameOver: boolean;
    started: boolean;
    ate: boolean;
    foodSpawned: boolean;
    foodFalling: boolean;
}

interface SnakeSceneParams {
    /** Nested snake state from the SSR payload (camelCase of SnakeScenePayload). */
    snake?: {
        sprites?: SnakeSpriteState[];
        score?: number;
        gameOver?: boolean;
        started?: boolean;
        gridWidth?: number;
        gridHeight?: number;
        cellSize?: number;
        streamUrl?: string;
    };
}

const GOOD_FOOD_KIND = 2;
const BAD_FOOD_KIND = 3;
const DEFAULT_STEP_MS = 125;

const KEY_TO_DIRECTION: Record<string, string> = {
    ArrowUp: 'up',
    w: 'up',
    W: 'up',
    k: 'up',
    K: 'up',
    ArrowDown: 'down',
    s: 'down',
    S: 'down',
    j: 'down',
    J: 'down',
    ArrowLeft: 'left',
    a: 'left',
    A: 'left',
    h: 'left',
    H: 'left',
    ArrowRight: 'right',
    d: 'right',
    D: 'right',
    l: 'right',
    L: 'right',
};

const EAT_SOUND_ALIAS = 'snake-eat';
const SPAWN_SOUND_ALIAS = 'snake-spawn';
const ENDGAME_SOUND_ALIAS = 'snake-endgame';
const EAT_SOUND_URL = '_content/Game.UI/audio/snake-eat.mp3';
const SPAWN_SOUND_URL = '_content/Game.UI/audio/snake-spawn.mp3';
const ENDGAME_SOUND_URL = '_content/Game.UI/audio/snake-endgame.mp3';

const soundRegistered = new Set<string>();

function ensureSound(alias: string, url: string): void {
    if (soundRegistered.has(alias)) return;
    sound.add(alias, url);
    soundRegistered.add(alias);
}

function playSound(alias: string, url: string): void {
    ensureSound(alias, url);
    void sound.play(alias);
}

function isSnakeSpriteState(value: unknown): value is SnakeSpriteState {
    if (!value || typeof value !== 'object') return false;
    const state = value as Partial<SnakeSpriteState>;
    return typeof state.id === 'number' &&
        typeof state.x === 'number' &&
        typeof state.y === 'number' &&
        typeof state.previousX === 'number' &&
        typeof state.previousY === 'number' &&
        typeof state.velocityX === 'number' &&
        typeof state.velocityY === 'number' &&
        typeof state.kind === 'number' &&
        typeof state.r === 'number' &&
        typeof state.g === 'number' &&
        typeof state.b === 'number';
}

function isSnakeRenderSignal(value: unknown): value is SnakeRenderSignal {
    if (!value || typeof value !== 'object') return false;
    const signal = value as Partial<SnakeRenderSignal>;
    return typeof signal.seq === 'number' &&
        typeof signal.entityCount === 'number' &&
        typeof signal.tickMs === 'number' &&
        typeof signal.stepMs === 'number' &&
        Array.isArray(signal.sprites) &&
        signal.sprites.every(isSnakeSpriteState) &&
        typeof signal.score === 'number' &&
        typeof signal.gameOver === 'boolean' &&
        typeof signal.started === 'boolean' &&
        typeof signal.ate === 'boolean' &&
        typeof signal.foodSpawned === 'boolean' &&
        typeof signal.foodFalling === 'boolean';
}

/**
 * Snake scene: C# owns grid rules, food fall, collision and state. SSE carries
 * batched snapshots; Pixi interpolates previous/current positions at display Hz.
 */
export const snakeScene: SceneBuilder = (app, params) => {
    const s = ((params ?? {}) as SnakeSceneParams).snake ?? {};
    app.renderer.background.color = '#020617';

    const gridWidth = s.gridWidth ?? 40;
    const gridHeight = s.gridHeight ?? 30;
    const cellSize = s.cellSize ?? 20;
    const boardWidth = gridWidth * cellSize;
    const boardHeight = gridHeight * cellSize;

    const board = new Graphics();
    const foodGfx = new Graphics();
    const scale = Math.min(app.screen.width / boardWidth, app.screen.height / boardHeight);
    board.scale.set(scale);
    board.x = (app.screen.width - boardWidth * scale) / 2;
    board.y = (app.screen.height - boardHeight * scale) / 2;
    foodGfx.scale.set(scale);
    foodGfx.x = board.x;
    foodGfx.y = board.y;
    app.stage.addChild(board);
    app.stage.addChild(foodGfx);

    // Wall border around the play field: static presentation, drawn once.
    const border = new Graphics();
    border.rect(0, 0, boardWidth, boardHeight).stroke({ width: 4, color: '#8B0000' });
    border.scale.set(scale);
    border.x = board.x;
    border.y = board.y;
    app.stage.addChild(border);

    const scoreText = new Text({
        text: 'Score: 0',
        style: new TextStyle({ fontFamily: 'Arial', fontSize: 20, fontWeight: 'bold', fill: '#e2e8f0' }),
    });
    scoreText.anchor.set(1, 0);
    scoreText.position.set(app.screen.width - 16, 12);
    app.stage.addChild(scoreText);

    // Simple start/game-over GUI: DOM overlay with a start button.
    const overlay = document.createElement('div');
    overlay.style.cssText =
        'position:fixed;top:52px;left:0;right:0;bottom:0;display:flex;flex-direction:column;' +
        'align-items:center;justify-content:center;gap:1rem;background:rgba(2,6,23,0.55);z-index:5;';
    const overlayTitle = document.createElement('div');
    overlayTitle.style.cssText = 'font:bold 2rem sans-serif;color:#34d399;text-align:center;';
    const startButton = document.createElement('button');
    startButton.type = 'button';
    startButton.textContent = 'START GAME';
    startButton.style.cssText =
        'background-color:#34d399;color:#020617;border:none;border-radius:0.5rem;' +
        'padding:0.75rem 2rem;font-size:1.1rem;font-weight:bold;cursor:pointer;';
    const hint = document.createElement('div');
    hint.style.cssText = 'color:#94a3b8;font:0.85rem sans-serif;';
    hint.textContent = 'or press SPACE';
    overlay.append(overlayTitle, startButton, hint);
    document.body.appendChild(overlay);

    let started = s.started ?? false;
    let gameOver = s.gameOver ?? false;
    let score = s.score ?? 0;
    let prevStarted = started;
    let prevGameOver = gameOver;
    let stepMs = DEFAULT_STEP_MS;
    const interpolation = new SnapshotBuffer<SnakeSpriteState>();

    const logTransitions = () => {
        if (started && !prevStarted) console.debug('[pixi-debug] snake started (ECS signal)');
        if (gameOver && !prevGameOver) {
            console.debug('[pixi-debug] snake ended (ECS signal) - score', score);
            playSound(ENDGAME_SOUND_ALIAS, ENDGAME_SOUND_URL);
        }
        prevStarted = started;
        prevGameOver = gameOver;
    };

    const updateOverlay = () => {
        if (started && !gameOver) {
            overlay.style.display = 'none';
            return;
        }
        overlay.style.display = 'flex';
        overlayTitle.textContent = gameOver ? `GAME OVER - SCORE: ${score}` : 'SNAKE';
        startButton.textContent = gameOver ? 'PLAY AGAIN' : 'START GAME';
    };

    const startGame = () => {
        if (started && !gameOver) return;
        fetch('/api/snake/start', { method: 'POST' })
            .then((response: Response) => {
                if (!response.ok) throw new Error(`start failed with HTTP ${response.status}`);
                started = true;
                gameOver = false;
                updateOverlay();
            })
            .catch((error: unknown) => console.error('[pixi-debug] snake start failed:', error));
    };
    startButton.addEventListener('click', startGame);

    const draw = () => {
        const alpha = interpolation.alpha(stepMs);
        board.clear();
        foodGfx.clear();

        for (const { previous, current } of interpolation.values()) {
            const x = previous.x + (current.x - previous.x) * alpha;
            const y = previous.y + (current.y - previous.y) * alpha;
            const color = (current.r << 16) | (current.g << 8) | current.b;
            const target = current.kind === GOOD_FOOD_KIND || current.kind === BAD_FOOD_KIND
                ? foodGfx
                : board;

            target.rect(x - cellSize / 2, y - cellSize / 2, cellSize, cellSize).fill(color);
        }
    };

    const setGameState = (nextScore: number, over: boolean, isStarted: boolean) => {
        scoreText.text = `Score: ${nextScore}`;
        gameOver = over;
        started = isStarted;
        score = nextScore;
        logTransitions();
        updateOverlay();
    };

    interpolation.ingest((s.sprites ?? []).filter(isSnakeSpriteState));
    setGameState(s.score ?? 0, s.gameOver ?? false, started);
    const onTicker = (_ticker: Ticker) => draw();
    app.ticker.add(onTicker);

    const onKeyDown = (event: KeyboardEvent) => {
        if (event.key === ' ' || event.key === 'Enter') {
            event.preventDefault();
            startGame();
            return;
        }
        const direction = KEY_TO_DIRECTION[event.key];
        if (!direction) return;
        event.preventDefault();
        fetch('/api/snake/input', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ direction }),
        }).catch((error: unknown) => console.error('[pixi-debug] snake input failed:', error));
    };
    window.addEventListener('keydown', onKeyDown);

    if (!s.streamUrl) return;

    const source = new EventSource(s.streamUrl);
    source.addEventListener('snake-move', (event: Event) => {
        try {
            const parsed: unknown = JSON.parse((event as MessageEvent<string>).data);
            if (!isSnakeRenderSignal(parsed)) throw new Error('invalid snake render signal');
            publishCSharpStats({ seq: parsed.seq, entityCount: parsed.entityCount, tickMs: parsed.tickMs });
            stepMs = Math.max(1, parsed.stepMs);
            interpolation.ingest(parsed.sprites, parsed.seq, parsed.epoch);
            setGameState(parsed.score, parsed.gameOver, parsed.started);

            if (parsed.ate) playSound(EAT_SOUND_ALIAS, EAT_SOUND_URL);
            if (parsed.foodSpawned) playSound(SPAWN_SOUND_ALIAS, SPAWN_SOUND_URL);
            if (parsed.foodFalling) console.debug('[pixi-debug] snake bad food started falling');
        } catch (error: unknown) {
            console.error('[pixi-debug] snake-move parse failed:', error);
        }
    });

    const cleanup = () => {
        source.close();
        window.removeEventListener('keydown', onKeyDown);
        app.ticker.remove(onTicker);
        overlay.remove();
    };
    source.onerror = () => console.warn('[pixi-debug] snake SSE interrupted; browser will retry');
    window.addEventListener('beforeunload', cleanup, { once: true });
};

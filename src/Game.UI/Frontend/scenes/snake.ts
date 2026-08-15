import { Graphics, Text, TextStyle } from 'pixi.js';
import { sound } from '@pixi/sound';
import type { SceneBuilder } from './types';
import { publishCSharpStats } from '../stats/overlays';

interface SnakeSpriteState {
    id: number;
    x: number;
    y: number;
    r: number;
    g: number;
    b: number;
}

interface SnakeRenderSignal {
    seq: number;
    entityCount: number;
    tickMs: number;
    sprites: SnakeSpriteState[];
    score: number;
    gameOver: boolean;
    started: boolean;
    ate: boolean;
    foodSpawned: boolean;
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

const dbg = (...args: unknown[]) => console.log('[pixi-debug] snake:', ...args);

/**
 * Snake scene: the C# simulation owns the grid and pushes one batched signal per
 * 8 Hz step over SSE. This scene only renders cells, forwards key input to the
 * sim as a suggestion, shows score/game-over state, and reacts to ECS events
 * (e.g. `ate` plays the eat sound). C# is the sole authority.
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
    const scale = Math.min(app.screen.width / boardWidth, app.screen.height / boardHeight);
    board.scale.set(scale);
    board.x = (app.screen.width - boardWidth * scale) / 2;
    board.y = (app.screen.height - boardHeight * scale) / 2;
    app.stage.addChild(board);

    const scoreText = new Text({
        text: 'Score: 0',
        style: new TextStyle({ fontFamily: 'Arial', fontSize: 20, fontWeight: 'bold', fill: '#e2e8f0' }),
    });
    scoreText.anchor.set(1, 0);
    scoreText.position.set(app.screen.width - 16, 12);
    app.stage.addChild(scoreText);

    // Simple start/game-over GUI: DOM overlay with a start button (works with mouse
    // and is immune to canvas focus quirks). Space/Enter do the same thing.
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

    // Logs ECS state transitions and plays the end sound exactly once per game over.
    const logTransitions = () => {
        if (started && !prevStarted) dbg('game started (ECS signal)');
        if (gameOver && !prevGameOver) {
            dbg('game ended (ECS signal) - score', score);
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
        dbg('starting game (button or space)');
        fetch('/api/snake/start', { method: 'POST' })
            .then(() => {
                started = true;
                gameOver = false;
                updateOverlay();
            })
            .catch((err) => console.error('[pixi-debug] snake start failed:', err));
    };
    startButton.addEventListener('click', startGame);

    const draw = (states: SnakeSpriteState[]) => {
        board.clear();
        for (const state of states) {
            board.rect(state.x - cellSize / 2, state.y - cellSize / 2, cellSize, cellSize)
                .fill((state.r << 16) | (state.g << 8) | state.b);
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

    draw(s.sprites ?? []);
    setGameState(s.score ?? 0, s.gameOver ?? false, started);

    const onKeyDown = (event: KeyboardEvent) => {
        if (event.key === ' ' || event.key === 'Enter') {
            event.preventDefault();
            startGame();
            return;
        }
        const direction = KEY_TO_DIRECTION[event.key];
        if (!direction) return;
        event.preventDefault();
        dbg('input direction:', direction);
        fetch('/api/snake/input', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ direction }),
        }).catch((err) => console.error('[pixi-debug] snake input failed:', err));
    };
    window.addEventListener('keydown', onKeyDown);

    if (!s.streamUrl) return;

    const source = new EventSource(s.streamUrl);
    source.addEventListener('snake-move', (event) => {
        try {
            const signal = JSON.parse((event as MessageEvent).data) as SnakeRenderSignal;
            publishCSharpStats({ seq: signal.seq, entityCount: signal.entityCount, tickMs: signal.tickMs });
            draw(signal.sprites);
            setGameState(signal.score, signal.gameOver, signal.started);
            if (signal.ate) {
                dbg('ECS event: ate food, score', signal.score);
                playSound(EAT_SOUND_ALIAS, EAT_SOUND_URL);
            }
            if (signal.foodSpawned) {
                dbg('ECS event: food spawned');
                playSound(SPAWN_SOUND_ALIAS, SPAWN_SOUND_URL);
            }
        } catch (err) {
            console.error('[pixi-debug] snake-move parse failed:', err);
        }
    });
    const cleanup = () => {
        source.close();
        window.removeEventListener('keydown', onKeyDown);
        overlay.remove();
    };
    source.onerror = () => cleanup();
    window.addEventListener('beforeunload', cleanup);
};

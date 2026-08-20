import { Container, Graphics, Text, TextStyle } from 'pixi.js';
import type { Ticker } from 'pixi.js';
import { sound } from '@pixi/sound';
import type { SceneBuilder } from './types';
import { publishCSharpStats } from '../stats/overlays';
import { SnapshotBuffer } from './interpolation';
import { connectSignalStream } from './signalSource';

interface PacmanSpriteState {
    id: number;
    x: number;
    y: number;
    previousX: number;
    previousY: number;
    velocityX: number;
    velocityY: number;
    rotation: number;
    kind: number;
    direction: number;
    mode: number;
    visible: boolean;
    r: number;
    g: number;
    b: number;
}

interface PacmanRenderSignal {
    seq: number;
    entityCount: number;
    tickMs: number;
    stepMs?: number;
    epoch?: number;
    sprites: PacmanSpriteState[];
    score: number;
    lives: number;
    level: number;
    pelletsRemaining: number;
    gameOver: boolean;
    started: boolean;
    frightened: boolean;
    atePellet: boolean;
    atePowerPellet: boolean;
    ghostEaten: boolean;
    died: boolean;
    levelUp: boolean;
}

interface PacmanSceneParams {
    pacman?: {
        sprites?: PacmanSpriteState[];
        mazeRows?: string[];
        score?: number;
        lives?: number;
        level?: number;
        pelletsRemaining?: number;
        gameOver?: boolean;
        started?: boolean;
        mazeWidth?: number;
        mazeHeight?: number;
        cellSize?: number;
        streamUrl?: string;
    };
}

function isPacmanSpriteState(value: unknown): value is PacmanSpriteState {
    if (!value || typeof value !== 'object') return false;
    const state = value as Partial<PacmanSpriteState>;
    return typeof state.id === 'number' && typeof state.x === 'number' && typeof state.y === 'number' &&
        typeof state.previousX === 'number' && typeof state.previousY === 'number' &&
        typeof state.rotation === 'number' && typeof state.kind === 'number' &&
        typeof state.visible === 'boolean';
}

function isPacmanRenderSignal(value: unknown): value is PacmanRenderSignal {
    if (!value || typeof value !== 'object') return false;
    const signal = value as Partial<PacmanRenderSignal>;
    return typeof signal.seq === 'number' &&
        typeof signal.entityCount === 'number' &&
        typeof signal.tickMs === 'number' &&
        Array.isArray(signal.sprites) &&
        signal.sprites.every(isPacmanSpriteState) &&
        typeof signal.score === 'number' &&
        typeof signal.lives === 'number' &&
        typeof signal.level === 'number' &&
        typeof signal.pelletsRemaining === 'number' &&
        typeof signal.gameOver === 'boolean' &&
        typeof signal.started === 'boolean';
}

const KIND_PLAYER = 0;
const KIND_BLINKY = 1;
const KIND_PINKY = 2;
const KIND_INKY = 3;
const KIND_CLYDE = 4;
const KIND_PELLET = 5;
const KIND_POWER_PELLET = 6;

const MODE_FRIGHTENED = 2;
const MODE_EYES = 3;

const KEY_TO_DIRECTION: Record<string, string> = {
    ArrowUp: 'up',
    w: 'up',
    W: 'up',
    ArrowLeft: 'left',
    a: 'left',
    A: 'left',
    ArrowDown: 'down',
    s: 'down',
    S: 'down',
    ArrowRight: 'right',
    d: 'right',
    D: 'right',
};

const GHOST_COLORS: Record<number, number> = {
    [KIND_BLINKY]: 0xff3b30,
    [KIND_PINKY]: 0xff9de2,
    [KIND_INKY]: 0x32e6e6,
    [KIND_CLYDE]: 0xffb74a,
};

const SOUND_BASE_URL = '_content/Game.UI/audio/';
const soundRegistered = new Set<string>();

function ensureSound(alias: string, fileName: string): void {
    if (soundRegistered.has(alias)) return;
    sound.add(alias, `${SOUND_BASE_URL}${fileName}`);
    soundRegistered.add(alias);
}

function playSound(alias: string, fileName: string): void {
    ensureSound(alias, fileName);
    void sound.play(alias);
}

/**
 * Pacman presentation. C# owns maze rules, movement, ghost AI and collisions.
 * This scene only interpolates snapshots, draws, forwards input and plays edge-event audio.
 */
export const pacmanScene: SceneBuilder = (app, params) => {
    const p = ((params ?? {}) as PacmanSceneParams).pacman ?? {};
    app.renderer.background.color = '#020617';

    const mazeRows = p.mazeRows ?? [];
    const mazeWidth = p.mazeWidth ?? 29;
    const mazeHeight = p.mazeHeight ?? 31;
    const cellSize = p.cellSize ?? 8;
    const boardWidth = mazeWidth * cellSize;
    const boardHeight = mazeHeight * cellSize;

    const board = new Container();
    const mazeLayer = new Graphics();
    const pelletLayer = new Graphics();
    const actorLayer = new Graphics();
    board.addChild(mazeLayer, pelletLayer, actorLayer);
    app.stage.addChild(board);

    const drawMaze = () => {
        mazeLayer.clear();
        mazeLayer.rect(0, 0, boardWidth, boardHeight).fill(0x00030d);

        for (let y = 0; y < mazeHeight; y++) {
            const row = mazeRows[y] ?? '';
            for (let x = 0; x < mazeWidth; x++) {
                const wall = row[x] === ' ' || row[x] === undefined;
                const left = x * cellSize;
                const top = y * cellSize;
                if (wall) {
                    mazeLayer.rect(left, top, cellSize, cellSize).fill(0x07153c);
                } else {
                    mazeLayer.rect(left + 1, top + 1, cellSize - 2, cellSize - 2)
                        .fill(0x020817)
                        .stroke({ width: 0.8, color: 0x1d4ed8, alpha: 0.65 });
                }
            }
        }

        mazeLayer.rect(0, 0, boardWidth, boardHeight)
            .stroke({ width: 2, color: 0x2563eb, alpha: 0.85 });
    };

    drawMaze();

    const scoreText = new Text({
        text: 'SCORE 000000',
        style: new TextStyle({ fontFamily: 'monospace', fontSize: 16, fontWeight: 'bold', fill: '#f8fafc' }),
    });
    const levelText = new Text({
        text: 'LEVEL 1',
        style: new TextStyle({ fontFamily: 'monospace', fontSize: 16, fontWeight: 'bold', fill: '#60a5fa' }),
    });
    const livesText = new Text({
        text: 'LIVES 3',
        style: new TextStyle({ fontFamily: 'monospace', fontSize: 16, fontWeight: 'bold', fill: '#fde047' }),
    });
    app.stage.addChild(scoreText, levelText, livesText);

    const overlay = document.createElement('div');
    overlay.style.cssText =
        'position:fixed;top:52px;left:0;right:0;bottom:0;display:flex;flex-direction:column;' +
        'align-items:center;justify-content:center;gap:1rem;background:rgba(2,6,23,0.58);z-index:5;';
    const overlayTitle = document.createElement('div');
    overlayTitle.style.cssText = 'font:bold 2rem monospace;color:#facc15;text-align:center;';
    const startButton = document.createElement('button');
    startButton.type = 'button';
    startButton.textContent = 'START GAME';
    startButton.style.cssText =
        'background:#facc15;color:#020617;border:0;border-radius:0.5rem;padding:0.75rem 2rem;' +
        'font:bold 1.1rem monospace;cursor:pointer;';
    const hint = document.createElement('div');
    hint.style.cssText = 'color:#94a3b8;font:0.85rem monospace;text-align:center;';
    hint.textContent = 'ARROWS / WASD TO MOVE · SPACE TO START';
    overlay.append(overlayTitle, startButton, hint);
    document.body.appendChild(overlay);

    let started = p.started ?? false;
    let gameOver = p.gameOver ?? false;
    let score = p.score ?? 0;
    let lives = p.lives ?? 3;
    let level = p.level ?? 1;
    let previousGameOver = gameOver;
    let stepMs = 1000 / 60;
    const interpolation = new SnapshotBuffer<PacmanSpriteState>();

    const layout = () => {
        const scale = Math.min(app.screen.width / boardWidth, app.screen.height / boardHeight);
        board.scale.set(scale);
        board.x = (app.screen.width - boardWidth * scale) / 2;
        board.y = Math.max(0, (app.screen.height - boardHeight * scale) / 2);
        scoreText.position.set(12, 12);
        levelText.anchor.set(0.5, 0);
        levelText.position.set(app.screen.width / 2, 12);
        livesText.anchor.set(1, 0);
        livesText.position.set(app.screen.width - 12, 12);
    };

    const updateOverlay = () => {
        overlay.style.display = started && !gameOver ? 'none' : 'flex';
        overlayTitle.textContent = gameOver ? `GAME OVER · SCORE ${score}` : 'PAC-MAN';
        startButton.textContent = gameOver ? 'PLAY AGAIN' : 'START GAME';
    };

    const setStats = (nextScore: number, nextLives: number, nextLevel: number, over: boolean, isStarted: boolean) => {
        score = nextScore;
        lives = nextLives;
        level = nextLevel;
        gameOver = over;
        started = isStarted;
        scoreText.text = `SCORE ${String(score).padStart(6, '0')}`;
        levelText.text = `LEVEL ${level}`;
        livesText.text = `LIVES ${lives}`;

        if (gameOver && !previousGameOver) playSound('pacman-dying', 'pacman-dying.wav');
        previousGameOver = gameOver;
        updateOverlay();
    };

    const startGame = () => {
        if (started && !gameOver) return;
        fetch('/api/pacman/start', { method: 'POST' })
            .then((response: Response) => {
                if (!response.ok) throw new Error(`start failed with HTTP ${response.status}`);
                started = true;
                gameOver = false;
                playSound('pacman-start', 'pacman-start.wav');
                updateOverlay();
            })
            .catch((error: unknown) => console.error('[pixi-debug] pacman start failed:', error));
    };

    const postDirection = (direction: string) => {
        fetch('/api/pacman/input', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ direction }),
        }).catch((error: unknown) => console.error('[pixi-debug] pacman input failed:', error));
    };

    const onKeyDown = (event: KeyboardEvent) => {
        if (event.key === ' ' || event.key === 'Enter') {
            event.preventDefault();
            startGame();
            return;
        }

        const direction = KEY_TO_DIRECTION[event.key];
        if (!direction) return;
        event.preventDefault();
        postDirection(direction);
    };

    const draw = (alpha: number) => {
        pelletLayer.clear();
        actorLayer.clear();

        for (const { previous, current } of interpolation.values()) {
            const x = previous.x + (current.x - previous.x) * alpha;
            const y = previous.y + (current.y - previous.y) * alpha;
            if (!current.visible) continue;

            if (current.kind === KIND_PELLET || current.kind === KIND_POWER_PELLET) {
                const radius = current.kind === KIND_POWER_PELLET ? 2.4 + Math.sin(performance.now() / 160) * 0.5 : 1.2;
                pelletLayer.circle(x, y, radius).fill(current.kind === KIND_POWER_PELLET ? 0xfef08a : 0xffffff);
                continue;
            }

            if (current.kind === KIND_PLAYER) {
                actorLayer.circle(x, y, cellSize * 0.42).fill(0xfacc15);
                continue;
            }

            const ghostColor = current.mode === MODE_FRIGHTENED ? 0x2563eb : GHOST_COLORS[current.kind] ?? 0xffffff;
            actorLayer.roundRect(x - cellSize * 0.42, y - cellSize * 0.42, cellSize * 0.84, cellSize * 0.84, 2).fill(ghostColor);
            actorLayer.circle(x - 1.6, y - 1, 1.4).fill(0xffffff);
            actorLayer.circle(x + 1.6, y - 1, 1.4).fill(0xffffff);

            if (current.mode === MODE_EYES) {
                actorLayer.circle(x - 1.6, y - 1, 0.55).fill(0x1d4ed8);
                actorLayer.circle(x + 1.6, y - 1, 0.55).fill(0x1d4ed8);
            }
        }
    };

    // Redraw only when a fresh snapshot arrived or interpolation is still in
    // flight; idle frames (start overlay / game over / paused) are skipped.
    // Trade-off: the power-pellet pulse (performance.now() sine) freezes while
    // the sim is idle — acceptable, pellets animate while the game runs.
    const onTicker = (_ticker: Ticker) => {
        const alpha = interpolation.advance(stepMs);
        if (alpha !== null) draw(alpha);
    };

    interpolation.ingest((p.sprites ?? []).filter(isPacmanSpriteState));
    setStats(score, lives, level, gameOver, started);
    layout();
    updateOverlay();
    window.addEventListener('resize', layout);
    window.addEventListener('keydown', onKeyDown);
    startButton.addEventListener('click', startGame);
    app.ticker.add(onTicker);

    if (!p.streamUrl) return;

    const stream = connectSignalStream(p.streamUrl);
    if (!stream) return;
    stream.addSignalListener('pacman-move', (data) => {
        try {
            const parsed: unknown = JSON.parse(data);
            if (!isPacmanRenderSignal(parsed)) throw new Error('invalid Pacman render signal');
            const signal = parsed;
            publishCSharpStats({ seq: signal.seq, entityCount: signal.entityCount, tickMs: signal.tickMs });
            stepMs = Math.max(1, signal.stepMs ?? 1000 / 60);
            interpolation.ingest(signal.sprites, signal.seq, signal.epoch);
            setStats(signal.score, signal.lives, signal.level, signal.gameOver, signal.started);

            if (signal.atePellet) playSound('pacman-munch', 'pacman-munch1.wav');
            if (signal.atePowerPellet) playSound('pacman-power', 'pacman-frightened.wav');
            if (signal.ghostEaten) playSound('pacman-ghost-eaten', 'pacman-ghost-eaten.wav');
            if (signal.levelUp) playSound('pacman-level-up', 'pacman-extra-life.wav');
            if (signal.died && !signal.gameOver) playSound('pacman-dying', 'pacman-dying.wav');
        } catch (error: unknown) {
            console.error('[pixi-debug] pacman-move parse failed:', error);
        }
    });

    let cleanedUp = false;
    const cleanup = () => {
        if (cleanedUp) return;
        cleanedUp = true;
        stream.close();
        app.ticker.remove(onTicker);
        window.removeEventListener('resize', layout);
        window.removeEventListener('keydown', onKeyDown);
        overlay.remove();
        scoreText.destroy();
        levelText.destroy();
        livesText.destroy();
        board.destroy({ children: true });
    };

    stream.onInterrupted(() => {
        // EventSource reconnects automatically. Keep scene alive for transient network loss.
        console.warn('[pixi-debug] pacman SSE connection interrupted; browser will retry');
    });
    window.addEventListener('beforeunload', cleanup, { once: true });
};

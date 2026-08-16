import { Graphics, Text, TextStyle } from 'pixi.js';
import type { Ticker } from 'pixi.js';
import { sound } from '@pixi/sound';
import RAPIER from '@dimforge/rapier2d';
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

// Food entities get monotonically increasing render ids starting at 1000
// (must match SnakeSimulation.FoodRenderId). id >= FOOD_ID_START => food.
const FOOD_ID_START = 1000;
const isFood = (id: number) => id >= FOOD_ID_START;

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

    // --- Presentation physics: Rapier food drop (ADR-002/005) -------------------
    // The ECS flags the food as falling after 3 s; this client runs the gravity
    // fall, then reports the FINAL position once. The ECS only ever sees the
    // initial and final positions - never the intermediate frames.
    let physicsWorld: RAPIER.World | null = null;
    let foodBody: RAPIER.RigidBody | null = null;
    let foodFalling = false;
    let fallingFoodId: number | null = null;
    let settleFrames = 0;
    let dropPosted = false;
    // Ids of foods that already settled at the bottom (drawn statically, black).
    const settledFoodIds = new Set<number>();

    const initPhysics = async () => {
        if (physicsWorld) return;
        const initFn = (RAPIER as unknown as { init?: () => Promise<void> }).init;
        if (initFn) await initFn();
        // Rapier is y-up; the board is y-down, so gravity must point DOWN in world
        // space (negative y) for the food to fall to the bottom of the screen.
        physicsWorld = new RAPIER.World({ x: 0, y: -9.81 });
        const floorBody = physicsWorld.createRigidBody(RAPIER.RigidBodyDesc.fixed().setTranslation(boardWidth / 2, -2));
        physicsWorld.createCollider(RAPIER.ColliderDesc.cuboid(boardWidth / 2, 2), floorBody);
        const leftBody = physicsWorld.createRigidBody(RAPIER.RigidBodyDesc.fixed().setTranslation(-2, boardHeight / 2));
        physicsWorld.createCollider(RAPIER.ColliderDesc.cuboid(2, boardHeight), leftBody);
        const rightBody = physicsWorld.createRigidBody(RAPIER.RigidBodyDesc.fixed().setTranslation(boardWidth + 2, boardHeight / 2));
        physicsWorld.createCollider(RAPIER.ColliderDesc.cuboid(2, boardHeight), rightBody);
        dbg('Rapier world initialized');
    };

    const startFoodFall = async (cellX: number, cellY: number) => {
        if (foodFalling) return;
        await initPhysics();
        if (!physicsWorld) return;

        // Board coords are y-down; Rapier is y-up: flip when entering the world.
        const worldX = (cellX + 0.5) * cellSize;
        const worldY = boardHeight - (cellY + 0.5) * cellSize;
        const randomDrift = (Math.random() - 0.5) * cellSize * 2;
        foodBody = physicsWorld.createRigidBody(
            RAPIER.RigidBodyDesc.dynamic()
                .setTranslation(worldX, worldY)
                .setLinvel(randomDrift, 0),
        );
        physicsWorld.createCollider(RAPIER.ColliderDesc.cuboid(cellSize / 2 - 1, cellSize / 2 - 1), foodBody);
        foodFalling = true;
        settleFrames = 0;
        dropPosted = false;
        dbg('ECS event: food falling, Rapier drop started at cell', cellX, cellY);
    };

    const reportFoodDrop = () => {
        if (dropPosted || !physicsWorld || !foodBody) return;
        const pos = foodBody.translation();
        const gx = Math.max(0, Math.min(gridWidth - 1, Math.round(pos.x / cellSize - 0.5)));
        const gy = Math.max(0, Math.min(gridHeight - 1, Math.round((boardHeight - pos.y) / cellSize - 0.5)));
        physicsWorld.removeRigidBody(foodBody);
        foodBody = null;
        foodFalling = false;
        dropPosted = true;
        if (fallingFoodId !== null) settledFoodIds.add(fallingFoodId);
        fallingFoodId = null;
        dbg('food dropped, reporting final cell', gx, gy);
        fetch('/api/snake/food-dropped', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ x: gx, y: gy }),
        }).catch((err) => console.error('[pixi-debug] snake food-dropped failed:', err));
    };

    const onTicker = (ticker: Ticker) => {
        if (!foodFalling || !physicsWorld || !foodBody) return;
        physicsWorld.timestep = Math.min(ticker.deltaMS / 1000, 1 / 30);
        physicsWorld.step();

        const pos = foodBody.translation();
        const gx = pos.x / cellSize - 0.5;
        const gy = (boardHeight - pos.y) / cellSize - 0.5;
        foodGfx.clear();
        foodGfx.rect(gx * cellSize - cellSize / 2, gy * cellSize - cellSize / 2, cellSize, cellSize)
            .fill(0x000000);

        const speed = Math.abs(foodBody.linvel().y) + Math.abs(foodBody.linvel().x);
        if (foodBody.isSleeping() || (pos.y <= 2 && speed < 0.5 && ++settleFrames > 20)) {
            reportFoodDrop();
        }
    };
    app.ticker.add(onTicker);

    const draw = (states: SnakeSpriteState[]) => {
        board.clear();
        foodGfx.clear();
        for (const state of states) {
            if (!isFood(state.id)) {
                board.rect(state.x - cellSize / 2, state.y - cellSize / 2, cellSize, cellSize)
                    .fill((state.r << 16) | (state.g << 8) | state.b);
                continue;
            }
            // The falling food is animated by the Rapier ticker; skip its stale position.
            if (state.id === fallingFoodId) continue;
            foodGfx.rect(state.x - cellSize / 2, state.y - cellSize / 2, cellSize, cellSize)
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
            if (signal.foodFalling) {
                // The falling food is the black one not yet settled at the bottom.
                const blackFoods = signal.sprites.filter(
                    (state) => isFood(state.id) && state.r === 0 && state.g === 0 && state.b === 0);
                const food = blackFoods.find((state) => !settledFoodIds.has(state.id)) ?? blackFoods[0];
                if (food) {
                    fallingFoodId = food.id;
                    void startFoodFall(
                        Math.round(food.x / cellSize - 0.5),
                        Math.round(food.y / cellSize - 0.5));
                } else {
                    dbg('ECS event: food falling, but no black food found in snapshot');
                }
            }
            if (signal.gameOver) {
                // Stop any running drop: the physics body dies with the round.
                if (physicsWorld && foodBody) {
                    physicsWorld.removeRigidBody(foodBody);
                    foodBody = null;
                }
                foodFalling = false;
            }
        } catch (err) {
            console.error('[pixi-debug] snake-move parse failed:', err);
        }
    });
    const cleanup = () => {
        source.close();
        window.removeEventListener('keydown', onKeyDown);
        app.ticker.remove(onTicker);
        if (physicsWorld) physicsWorld.free();
        physicsWorld = null;
        overlay.remove();
    };
    source.onerror = () => cleanup();
    window.addEventListener('beforeunload', cleanup);
};

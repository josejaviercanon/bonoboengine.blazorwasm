import type { Ticker } from 'pixi.js';
import Stats from './Stats.js';

/** Server-side ECS telemetry snapshot (published 1 Hz over the SSE stream). */
export interface CSharpStats {
    seq: number;
    entityCount: number;
    tickMs: number;
}

const HUD_BAR_HEIGHT_PX = 56;

let statsInstance: Stats | null = null;
let statsVisible = false;
let statsTicker: Pick<Ticker, 'add' | 'remove'> | null = null;
const statsTick = () => {
    if (statsInstance && statsVisible) statsInstance.update();
};

/**
 * Creates the stats.js DOM overlay and hooks it to the Pixi ticker.
 * Pure JS per frame - never crosses the JS/.NET boundary during updates.
 */
export function attachStatsOverlay(ticker: Pick<Ticker, 'add' | 'remove'>): void {
    if (statsInstance) return;

    statsInstance = new Stats();
    statsInstance.showPanel(0);
    statsInstance.dom.style.top = `${HUD_BAR_HEIGHT_PX}px`;
    statsInstance.dom.style.left = '0px';
    statsInstance.dom.style.display = 'none';
    document.body.appendChild(statsInstance.dom);

    statsTicker = ticker;
    statsTicker.add(statsTick);
}

export function togglePixiStats(): void {
    if (!statsInstance) return;
    statsVisible = !statsVisible;
    statsInstance.dom.style.display = statsVisible ? 'block' : 'none';
}

let csharpOverlay: HTMLDivElement | null = null;
let csharpVisible = false;

export function attachCSharpStatsOverlay(): void {
    if (csharpOverlay) return;

    csharpOverlay = document.createElement('div');
    csharpOverlay.style.cssText =
        'position:fixed;top:' + HUD_BAR_HEIGHT_PX + 'px;left:0;z-index:10000;' +
        'display:none;pointer-events:none;' +
        'font:bold 11px monospace;color:#4ade80;' +
        'background:rgba(2,6,23,0.9);padding:6px 8px;white-space:pre;';
    csharpOverlay.textContent = 'C# ECS: waiting for telemetry...';
    document.body.appendChild(csharpOverlay);
}

export function toggleCSharpStats(): void {
    if (!csharpOverlay) return;
    csharpVisible = !csharpVisible;
    csharpOverlay.style.display = csharpVisible ? 'block' : 'none';
}

/** Renders the latest server ECS telemetry into the C# overlay (call on each SSE signal). */
export function publishCSharpStats(stats: CSharpStats): void {
    if (!csharpOverlay) return;
    csharpOverlay.textContent =
        `C# ECS\n` +
        `entities: ${stats.entityCount}\n` +
        `signal: #${stats.seq}\n` +
        `tick: ${stats.tickMs.toFixed(1)} ms`;
}

import { BUFFER_HEADER_LENGTH, decodeEntities, type BufferHeader, type EntityDecoder } from './bufferLayout';

export interface InterpolationEntry<T> {
    previous: T;
    current: T;
    receivedAt: number;
}

/**
 * Presentation render mode — the single scene CONST that governs how snapshot
 * time maps to sprite positions (ADR-005: presentation-side concern, PixiJS owns it).
 *
 * - 'interpolate'  α ∈ [0, 1]: classic receipt-anchored LERP between the previous
 *   and current snapshot. Lags one full sim tick behind (16.6ms @ 60Hz, 125ms for
 *   snake's 8Hz tick) and distorts apparent speed under SSE jitter: burst arrivals
 *   restart α near 0 (sprites slow down), late arrivals saturate α at 1 (freeze,
 *   then snap). Kept as the fallback for A/B comparison.
 * - 'extrapolate'  α ∈ [1, 1 + EXTRAPOLATION_CAP]: renders at the current snapshot
 *   the moment it arrives (α=1), then dead-reckons forward along the implied per-tick
 *   delta (curr − prev) at full sim speed. With the cap at one full tick, steady-rate
 *   delivery is perfectly continuous — by the next ingest the extrapolated position
 *   has landed exactly on the new curr. Zero added latency; jitter shows only as
 *   v·jitter position error instead of freeze/slowdown; a stalled stream runs at
 *   most one tick ahead then holds. Works with the scenes' existing lerp calls:
 *   lerp(prev, curr, α>1) = curr + (curr−prev)·(α−1), which IS linear dead
 *   reckoning; lerpAngle/lerpWrapped extrapolate rotation/track-Z the same way.
 * - 'snap'         α = 1: draw the authoritative state verbatim (diagnostics).
 */
export type RenderMode = 'interpolate' | 'extrapolate' | 'snap';

/** Single source of truth for every scene's render mode. */
export const RENDER_MODE: RenderMode = 'extrapolate';

/**
 * How far past the current snapshot we may render, as a fraction of one sim tick.
 * Must be ≥ 1 for continuity (smaller caps cause hold-then-jump stutter at steady
 * rate); 1 also bounds the visual overshoot when a signal stalls (tab throttle,
 * GC pause). After the cap, `advance` reports the frame settled so scenes stop
 * redrawing.
 */
const EXTRAPOLATION_CAP = 1;

export class SnapshotBuffer<T extends { id: number }> {
    private readonly entries = new Map<number, InterpolationEntry<T>>();
    private lastSeq = -1;
    private epoch: number | null = null;
    private lastSignalAt = 0;
    private dirty = false;

    public ingest(states: readonly T[], seq?: number, epoch?: number, now = performance.now()): boolean {
        // Epoch change = server-side world reset (Restart/Start-after-game-over):
        // the server also resets its seq counter, so the epoch check MUST run
        // before the stale-seq rejection below. Otherwise every signal of the
        // new run (seq 1, 2, ... <= previous-run seq) is dropped and the scene
        // freezes on the dead board until a page reload.
        if (epoch !== undefined && this.epoch !== null && epoch !== this.epoch) {
            this.entries.clear();
            this.lastSeq = -1;
        }
        if (seq !== undefined && seq <= this.lastSeq) return false;

        if (epoch !== undefined) this.epoch = epoch;
        if (seq !== undefined) this.lastSeq = seq;
        this.lastSignalAt = now;
        this.dirty = true;

        const seen = new Set<number>();
        for (const state of states) {
            seen.add(state.id);
            const entry = this.entries.get(state.id);
            if (entry) {
                entry.previous = entry.current;
                entry.current = state;
                entry.receivedAt = now;
            } else {
                this.entries.set(state.id, { previous: state, current: state, receivedAt: now });
            }
        }

        for (const id of this.entries.keys()) {
            if (!seen.has(id)) this.entries.delete(id);
        }
        return true;
    }

    /**
     * Fractional snapshot-time position for this frame. In extrapolate mode this
     * exceeds 1 by up to EXTRAPOLATION_CAP; scenes feed it straight into lerp/
     * lerpAngle/lerpWrapped, which extend linearly past 1 (dead reckoning).
     */
    public alpha(stepMs: number, now = performance.now()): number {
        const duration = Math.max(1, stepMs);
        const t = (now - this.lastSignalAt) / duration;
        if (RENDER_MODE === 'snap') return 1;
        if (RENDER_MODE === 'extrapolate') return 1 + Math.min(EXTRAPOLATION_CAP, t);
        return Math.min(1, Math.max(0, t));
    }

    /**
     * Per-frame redraw gate. Returns the alpha to render this frame, or null
     * when the visual state cannot have changed since the last draw (no new
     * snapshot ingested and interpolation already settled at alpha 1).
     * Scenes call this from the Pixi ticker and skip the full redraw on null —
     * this keeps idle scenes (start overlay, game over, paused sim) from
     * rebuilding identical Graphics every display frame, which was the cause
     * of the FPS collapse after the interpolation change.
     */
    public advance(stepMs: number, now = performance.now()): number | null {
        const alpha = this.alpha(stepMs, now);
        if (this.dirty) {
            this.dirty = false;
            return alpha;
        }
        const settledAt = RENDER_MODE === 'extrapolate' ? 1 + EXTRAPOLATION_CAP : 1;
        return alpha < settledAt ? alpha : null;
    }

    /**
     * Typed-array ingest source (ADR-007 Phase 3): decodes entities straight
     * out of a shared-memory float32 signal buffer — no JSON, no strings — and
     * runs the exact same epoch/seq bookkeeping as `ingest`. Returns the signal
     * header when accepted (for stats + stepMs), null when a stale seq was
     * rejected. `entityBase` skips scene-specific header extras.
     */
    public ingestFromBuffer(
        floats: Float32Array,
        decode: EntityDecoder<T>,
        entityBase = BUFFER_HEADER_LENGTH,
        now = performance.now()
    ): BufferHeader | null {
        const { header, states } = decodeEntities(floats, decode, entityBase);
        const accepted = this.ingest(states, header.seq, header.epoch, now);
        return accepted ? header : null;
    }

    public values(): IterableIterator<InterpolationEntry<T>> {
        return this.entries.values();
    }

    public clear(): void {
        this.entries.clear();
        this.lastSeq = -1;
        this.epoch = null;
        this.lastSignalAt = performance.now();
        this.dirty = true;
    }

    public removeWhere(predicate: (id: number) => boolean): void {
        for (const id of this.entries.keys()) {
            if (predicate(id)) this.entries.delete(id);
        }
    }
}

export function lerp(previous: number, current: number, alpha: number): number {
    return previous + (current - previous) * alpha;
}

export function lerpAngle(previous: number, current: number, alpha: number): number {
    let delta = current - previous;
    if (delta > Math.PI) delta -= Math.PI * 2;
    if (delta < -Math.PI) delta += Math.PI * 2;
    return previous + delta * alpha;
}

export function lerpWrapped(previous: number, current: number, alpha: number, period: number): number {
    let delta = current - previous;
    if (delta > period / 2) delta -= period;
    if (delta < -period / 2) delta += period;
    let value = previous + delta * alpha;
    while (value < 0) value += period;
    while (value >= period) value -= period;
    return value;
}

export function clampedDeltaSeconds(deltaMs: number): number {
    return Math.min(Math.max(deltaMs, 0) / 1000, 1 / 30);
}

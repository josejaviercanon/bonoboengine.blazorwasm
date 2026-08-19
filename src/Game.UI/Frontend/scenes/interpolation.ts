export interface InterpolationEntry<T> {
    previous: T;
    current: T;
    receivedAt: number;
}

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

    public alpha(stepMs: number, now = performance.now()): number {
        const duration = Math.max(1, stepMs);
        return Math.min(1, Math.max(0, (now - this.lastSignalAt) / duration));
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
        return alpha < 1 ? alpha : null;
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

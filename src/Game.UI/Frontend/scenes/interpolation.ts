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

    public ingest(states: readonly T[], seq?: number, epoch?: number, now = performance.now()): boolean {
        if (seq !== undefined && seq <= this.lastSeq) return false;

        if (epoch !== undefined && this.epoch !== null && epoch !== this.epoch) {
            this.entries.clear();
        }
        if (epoch !== undefined) this.epoch = epoch;
        if (seq !== undefined) this.lastSeq = seq;
        this.lastSignalAt = now;

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

    public values(): IterableIterator<InterpolationEntry<T>> {
        return this.entries.values();
    }

    public clear(): void {
        this.entries.clear();
        this.lastSeq = -1;
        this.epoch = null;
        this.lastSignalAt = performance.now();
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

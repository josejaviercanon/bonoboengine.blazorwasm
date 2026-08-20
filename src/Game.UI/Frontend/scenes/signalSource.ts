// Compile-time render-source selection — the frontend half of ADR-007's flag
// compilation (C# half: `IsMultiplayer`/`IsEcsServerSide` → `SINGLE_PLAYER_LOCAL`).
//
// `__RENDER_SOURCE__` is replaced textually by Vite `define` (see vite.config.ts)
// BEFORE Rollup tree-shaking, so the transport branch that is not selected is
// dead-code-eliminated from the dist bundle. Scenes never ship — or pay for —
// the transport they do not use:
//   npm run build        → 'sse'          (Game.Web static-SSR + SSE bridge, default)
//   npm run build:local  → 'local-buffer' (co-located Game.Wasm host, ADR-007 Phase 2/3)

export type RenderSource = 'sse' | 'local-buffer';

declare global {
    // eslint-disable-next-line no-var
    const __RENDER_SOURCE__: RenderSource;
}

/** Scene CONST: which transport feeds snapshots into the interpolation layer. */
export const RENDER_SOURCE: RenderSource = __RENDER_SOURCE__;

/** Uniform handle over the selected transport; created by `connectSignalStream`. */
export interface SignalStream {
    /** Subscribe to one named signal (SSE event name today); `data` is raw JSON. */
    addSignalListener(eventName: string, onData: (data: string) => void): void;
    /**
     * Subscribe to one named signal as a raw float32 buffer (ADR-007 Phase 3).
     * Only ever fires in `local-buffer` builds — the SSE-branch stub is a no-op,
     * so scenes can register both listeners and stay free of transport
     * branching: only the listener matching the compiled-in transport runs.
     */
    addBufferListener(eventName: string, onData: (floats: Float32Array) => void): void;
    /** Tear the stream down (EventSource.close / provider close). */
    close(): void;
    /** Called when the stream is interrupted; SSE reconnects automatically. */
    onInterrupted(handler: () => void): void;
}

/**
 * The co-located WASM host registers a typed-array bridge here (ADR-007
 * Phase 2/3): every signal is delivered as the Float32Array view over the
 * pinned shared buffer written by `DirectRenderTransport`, laid out per
 * `bufferLayout.ts`. Only reachable in `--mode wasm` bundles (DCE'd otherwise).
 */
export interface LocalBufferProvider {
    onSignal(eventName: string, onData: (floats: Float32Array) => void): void;
    close?(): void;
}

let localBufferProvider: LocalBufferProvider | null = null;

export function registerLocalBufferProvider(provider: LocalBufferProvider): void {
    localBufferProvider = provider;
}

export function connectSignalStream(url: string | undefined): SignalStream | null {
    if (!url) return null;

    if (__RENDER_SOURCE__ === 'sse') {
        const source = new EventSource(url);
        return {
            addSignalListener: (eventName, onData) => {
                source.addEventListener(eventName, (event) =>
                    onData((event as MessageEvent<string>).data));
            },
            // SSE builds carry no typed-array source; registered buffer
            // listeners simply never fire (see SignalStream.addBufferListener).
            addBufferListener: () => { /* no-op in SSE bundles */ },
            close: () => source.close(),
            onInterrupted: (handler) => { source.onerror = () => handler(); }
        };
    }

    // 'local-buffer' branch: exists only in `--mode wasm` builds.
    const provider = localBufferProvider;
    if (!provider) {
        console.error(
            '[pixi-debug] RENDER_SOURCE is "local-buffer" but no local buffer provider is registered. ' +
            'This bundle must be served by the co-located Game.Wasm host (ADR-007 Phase 2/3). ' +
            'Either run it under that host, or rebuild the frontend with `npm run build` (SSE mode).');
        return null;
    }
    return {
        // No JSON-text path in local-buffer bundles; registered text listeners
        // simply never fire (the buffer listener is the live one).
        addSignalListener: () => { /* no-op in local-buffer bundles */ },
        addBufferListener: (eventName, onData) => provider.onSignal(eventName, onData),
        close: () => provider.close?.(),
        onInterrupted: () => { /* in-memory bridge never disconnects */ }
    };
}

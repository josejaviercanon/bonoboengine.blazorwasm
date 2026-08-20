// Shared-memory float32 signal layout — ADR-007 Phase 3 (ADR-003 HEAPF32 target).
//
// The co-located WASM host (Phase 2 `DirectRenderTransport`) writes each batched
// render signal into a pinned float[] exposed to JS as a Float32Array view, and
// the registered LocalBufferProvider delivers that view per signal. No JSON, no
// strings, no loopback HTTP — scenes decode entities straight out of the typed
// array into SnapshotBuffer.ingestFromBuffer, which runs the SAME interpolation
// math (epoch/seq bookkeeping, extrapolation) as the SSE path.
//
// Layout contract (must match the C# writer):
//
//   floats[0 .. BUFFER_HEADER_LENGTH-1]  standard header (see BufferHeader)
//   floats[BUFFER_HEADER_LENGTH .. entityBase-1]  scene-specific scalar extras
//        (score, lives, event flags...) — owned by each scene's decoder
//   floats[entityBase ..]               entityCount × stride entity records
//
// Entity ids ride in float32, so they are exact only up to 2^24 — fine for the
// ECS id ranges these examples use.

/** Standard signal header: the first six floats of every signal buffer. */
export interface BufferHeader {
    seq: number;
    epoch: number;
    entityCount: number;
    stride: number;
    stepMs: number;
    tickMs: number;
}

export const BUFFER_HEADER_LENGTH = 6;

/** Reads the standard header from a raw signal buffer. */
export function readSignalHeader(floats: Float32Array): BufferHeader {
    if (floats.length < BUFFER_HEADER_LENGTH) {
        throw new Error(`signal buffer too short for header: ${floats.length} < ${BUFFER_HEADER_LENGTH}`);
    }
    return {
        seq: floats[0],
        epoch: floats[1],
        entityCount: floats[2],
        stride: floats[3],
        stepMs: floats[4],
        tickMs: floats[5]
    };
}

/** Decodes one entity record starting at `offset`; scenes define one per game. */
export type EntityDecoder<T> = (floats: Float32Array, offset: number) => T;

/**
 * Materializes the entity array from a raw signal buffer. `entityBase` is where
 * the entity region starts — BUFFER_HEADER_LENGTH plus any scene-specific
 * header extras. Decoding is allocation-light (one output array + one object
 * per entity) versus SSE's full JSON parse of the same payload.
 */
export function decodeEntities<T>(
    floats: Float32Array,
    decode: EntityDecoder<T>,
    entityBase = BUFFER_HEADER_LENGTH
): { header: BufferHeader; states: T[] } {
    const header = readSignalHeader(floats);
    const states: T[] = [];
    for (let i = 0; i < header.entityCount; i++) {
        states.push(decode(floats, entityBase + i * header.stride));
    }
    return { header, states };
}

/** Float-encoded boolean: 0 = false, non-zero = true. */
export function floatBool(value: number): boolean {
    return value !== 0;
}

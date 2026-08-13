// =============================================================================
// SeededRandom.cs — Deterministic random wrapper for procedural generation
// Extracted from: G53 — Procedural Generation (Section 1)
// Guide: /G/G53_procedural_generation.md
// =============================================================================

using System;

namespace U2DToolkit.Examples.Procgen
{
    /// <summary>
    /// Deterministic random number generator wrapper. Same seed always produces
    /// the same sequence — essential for reproducible procedural generation,
    /// seed sharing between players, replays, and bug reproduction.
    /// <para>
    /// Key rule: Never mix <see cref="System.Random"/> instances across systems.
    /// Each subsystem (dungeon layout, loot, enemy placement) should derive its
    /// own child seed from the master seed via <see cref="DeriveChildSeed"/> so
    /// they stay independent.
    /// </para>
    /// </summary>
    public sealed class SeededRandom
    {
        /// <summary>The original seed this instance was created with.</summary>
        public int Seed { get; }

        private Random _rng;

        public SeededRandom(int seed)
        {
            Seed = seed;
            _rng = new Random(seed);
        }

        /// <summary>Reset the RNG to its initial state (same seed).</summary>
        public void Reset() => _rng = new Random(Seed);

        /// <summary>Random int in [0, max).</summary>
        public int Next(int max) => _rng.Next(max);

        /// <summary>Random int in [min, max).</summary>
        public int Next(int min, int max) => _rng.Next(min, max);

        /// <summary>Random float in [0, 1).</summary>
        public float NextFloat() => (float)_rng.NextDouble();

        /// <summary>Random float in [min, max).</summary>
        public float NextFloat(float min, float max) => min + (max - min) * NextFloat();

        /// <summary>Random boolean with configurable probability.</summary>
        public bool NextBool(float chance = 0.5f) => NextFloat() < chance;

        /// <summary>Pick a random element from a span.</summary>
        public T Pick<T>(ReadOnlySpan<T> items) => items[Next(items.Length)];

        /// <summary>
        /// Weighted random selection. Returns the index of the chosen element.
        /// Weights do not need to sum to 1.
        /// </summary>
        public int WeightedIndex(ReadOnlySpan<float> weights)
        {
            float total = 0f;
            foreach (var w in weights) total += w;
            float roll = NextFloat() * total;
            float acc = 0f;
            for (int i = 0; i < weights.Length; i++)
            {
                acc += weights[i];
                if (roll < acc) return i;
            }
            return weights.Length - 1;
        }

        /// <summary>Fisher-Yates shuffle in place.</summary>
        public void Shuffle<T>(Span<T> span)
        {
            for (int i = span.Length - 1; i > 0; i--)
            {
                int j = Next(i + 1);
                (span[i], span[j]) = (span[j], span[i]);
            }
        }

        /// <summary>
        /// Derive a deterministic child seed for sub-generators (biome, loot, etc.).
        /// Each channel value produces a unique but reproducible child sequence.
        /// </summary>
        public int DeriveChildSeed(int channel) => unchecked(Seed * 31 + channel);

        // =====================================================================
        // Seed Display — human-readable seed strings for sharing
        // =====================================================================

        private const string DisplayChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no ambiguous I/1/O/0

        /// <summary>
        /// Convert a numeric seed to a readable 8-character alphanumeric string
        /// for display to players (e.g., "ABCD1234").
        /// </summary>
        public static string SeedToDisplay(int seed)
        {
            Span<char> buf = stackalloc char[8];
            uint v = unchecked((uint)seed);
            for (int i = 0; i < 8; i++)
            {
                buf[i] = DisplayChars[(int)(v % (uint)DisplayChars.Length)];
                v /= (uint)DisplayChars.Length;
            }
            return new string(buf);
        }

        /// <summary>
        /// Convert a display string back to a numeric seed.
        /// </summary>
        public static int DisplayToSeed(ReadOnlySpan<char> display)
        {
            uint v = 0, mult = 1;
            for (int i = 0; i < display.Length; i++)
            {
                int idx = DisplayChars.IndexOf(display[i]);
                if (idx < 0) idx = 0;
                v += (uint)idx * mult;
                mult *= (uint)DisplayChars.Length;
            }
            return unchecked((int)v);
        }
    }

    /// <summary>
    /// Well-known seed channel constants for separating RNG streams per subsystem.
    /// </summary>
    public static class SeedChannels
    {
        public const int Dungeon = 0;
        public const int Loot = 1;
        public const int Enemies = 2;
        public const int Terrain = 3;
        public const int Events = 4;
    }
}

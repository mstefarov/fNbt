using System;

namespace fNbt {
    /// <summary> Immutable settings for reading and writing NBT: the flavor, validation toggles,
    /// and limits. Create one per context and reuse it; a file parser and a network parser can
    /// each hold their own. </summary>
    public sealed class NbtOptions {
        /// <summary> The default settings: <see cref="NbtFlavor.Java"/>, validation on write only,
        /// no allocation limit. </summary>
        public static NbtOptions Default { get; } = new NbtOptions();

        /// <summary> The wire encoding to read and write. Defaults to <see cref="NbtFlavor.Java"/>. </summary>
        public NbtFlavor Flavor { get; init; } = NbtFlavor.Java;

        /// <summary> Whether reads enforce the flavor's conformance rules (permitted tag types and
        /// string ceilings) in addition to parsing. Defaults to <c>false</c>: reads accept anything
        /// parseable, so files that merely bend the rules still load. </summary>
        public bool ValidateOnRead { get; init; }

        /// <summary> Whether writes enforce the flavor's conformance rules, refusing to produce a
        /// document the flavor's own readers would reject. Defaults to <c>true</c>. Flavors without
        /// restrictions (like <see cref="NbtFlavor.Java"/>) pay no cost either way. </summary>
        public bool ValidateOnWrite { get; init; } = true;

        /// <summary> Maximum size, in bytes, of any single allocation made on behalf of a length
        /// declared in the input: array payloads, list-as-array reads, and strings. Guards against
        /// tiny corrupt or hostile documents declaring huge lengths, which matters most on
        /// compressed and non-seekable streams where declared lengths cannot be checked against the
        /// bytes actually available. Per-allocation, not a total document quota.
        /// Defaults to <c>null</c>: no limit. </summary>
        public long? MaxAllocation { get; init; }
    }
}

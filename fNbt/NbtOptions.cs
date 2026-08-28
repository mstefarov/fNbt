using System;

namespace fNbt {
    /// <summary> Settings for reading and writing NBT: the flavor, validation toggles, and limits.
    /// Every consumer snapshots the values at its own construction, so changing an options
    /// instance later does not affect objects already created from it. </summary>
    public sealed class NbtOptions {
        /// <summary> Returns a new options instance with default settings:
        /// <see cref="NbtFlavor.Java"/>, validation on write only, no allocation limit. </summary>
        public static NbtOptions Default {
            get { return new NbtOptions(); }
        }

        /// <summary> The wire encoding to read and write. Defaults to <see cref="NbtFlavor.Java"/>. </summary>
        public NbtFlavor Flavor { get; set; } = NbtFlavor.Java;

        /// <summary> Whether reads enforce the flavor's conformance rules (permitted tag types and
        /// string ceilings) in addition to parsing. Defaults to <c>false</c>: reads accept anything
        /// parseable, so files that merely bend the rules still load. </summary>
        public bool ValidateOnRead { get; set; }

        /// <summary> Whether writes enforce the flavor's conformance rules, refusing to produce a
        /// document the flavor's own readers would reject. Defaults to <c>true</c>. Flavors without
        /// restrictions (like <see cref="NbtFlavor.Java"/>) pay no cost either way. </summary>
        public bool ValidateOnWrite { get; set; } = true;

        /// <summary> Maximum size, in bytes, of any single allocation made on behalf of a length
        /// declared in the input: array payloads, list-as-array reads, and strings. Guards against
        /// tiny corrupt or hostile documents declaring huge lengths, which matters most on
        /// compressed and non-seekable streams where declared lengths cannot be checked against the
        /// bytes actually available. Per-allocation, not a total document quota.
        /// Defaults to <c>null</c>: no limit. </summary>
        public long? MaxAllocation { get; set; }
    }
}

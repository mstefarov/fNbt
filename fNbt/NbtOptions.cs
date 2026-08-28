using System;

namespace fNbt {
    /// <summary> Settings for reading and writing NBT: the flavor, validation toggles, and limits.
    /// Every consumer snapshots the values at its own construction, so changing an options
    /// instance later does not affect objects already created from it. </summary>
    public sealed class NbtOptions {
        /// <summary> Creates options with default settings:
        /// <see cref="NbtFlavor.Java"/>, validation on write only, no allocation limit. </summary>
        public NbtOptions() { }


        /// <summary> Creates options for the given flavor, with otherwise-default settings. </summary>
        /// <param name="flavor"> The wire encoding to read and write. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="flavor"/> is <c>null</c>. </exception>
        public NbtOptions(NbtFlavor flavor) {
            if (flavor == null) throw new ArgumentNullException(nameof(flavor));
            Flavor = flavor;
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


        // Shared snapshot-and-validate helpers for every entry point that takes options.
        // Each reads its option once, so a concurrently mutated instance cannot bypass validation.

        internal static NbtFlavor SnapshotFlavor(NbtOptions options) {
            if (options == null) throw new ArgumentNullException(nameof(options));
            NbtFlavor flavor = options.Flavor;
            if (flavor == null) {
                throw new ArgumentNullException(nameof(options), "Options must name a flavor.");
            }
            return flavor;
        }


        // For the named-root APIs; NbtCodec uses SnapshotFlavor and permits JavaNetwork
        internal static NbtFlavor SnapshotFileFlavor(NbtOptions options) {
            NbtFlavor flavor = SnapshotFlavor(options);
            flavor.EnsureUsableForFiles(nameof(options));
            return flavor;
        }


        internal static long? SnapshotMaxAllocation(NbtOptions options) {
            long? maxAllocation = options.MaxAllocation;
            if (maxAllocation <= 0) {
                throw new ArgumentOutOfRangeException(nameof(options), maxAllocation,
                                                      "MaxAllocation must be positive.");
            }
            return maxAllocation;
        }
    }
}

using System;
using System.Threading;

namespace fNbt {
    /// <summary> Settings for reading and writing NBT: the flavor, validation toggles, and limits.
    /// Every consumer snapshots the values at its own construction, so changing an options
    /// instance later does not affect objects already created from it. Application-wide defaults
    /// live on the static <c>Default*</c> properties; configure those during startup, before any
    /// NBT work. </summary>
    public sealed class NbtOptions {
        // The three policy values, separate from the flavor so NbtCodec.For can key its
        // per-flavor cache on this object: a flavor-default change reuses it, and only a
        // policy change rebuilds a cached codec.
        internal sealed class PolicySnapshot {
            public readonly bool ValidateOnRead;
            public readonly bool ValidateOnWrite;
            public readonly long MaxAllocation;

            public PolicySnapshot(bool validateOnRead, bool validateOnWrite, long maxAllocation) {
                ValidateOnRead = validateOnRead;
                ValidateOnWrite = validateOnWrite;
                MaxAllocation = maxAllocation;
            }
        }


        // Two independent references, each published whole. A reader may pair a new flavor with
        // the previous policy; each value is valid on its own, and nothing promises atomic
        // reconfiguration.
        // NbtFlavor initialization must never read this class back: the type initializers
        // stay cycle-free only because the dependency runs strictly NbtOptions -> NbtFlavor.
        static NbtFlavor defaultFlavor = NbtFlavor.Java;
        static PolicySnapshot defaultPolicy =
            new PolicySnapshot(validateOnRead: false, validateOnWrite: true, maxAllocation: long.MaxValue);


        /// <summary> The flavor used by entry points constructed without one: <see cref="NbtFile"/>,
        /// <see cref="NbtReader"/>, <see cref="NbtWriter"/>, and <see cref="NbtFile.ReadRootTagName(string)"/>.
        /// Initially <see cref="NbtFlavor.Java"/>. Every such entry point works with named roots,
        /// so flavors without a root name are rejected here; pass those to <see cref="NbtCodec"/>
        /// explicitly. </summary>
        /// <exception cref="ArgumentNullException"> value is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> value is a flavor without a root name. </exception>
        public static NbtFlavor DefaultFlavor {
            get { return Volatile.Read(ref defaultFlavor); }
            set {
                if (value == null) throw new ArgumentNullException(nameof(value));
                if (!value.HasRootName) {
                    throw new ArgumentException(
                        "The " + value.Name + " flavor has no root name, so it cannot be a default: " +
                        "NbtFile, NbtReader, and NbtWriter all use named roots. " +
                        "Use NbtCodec.For(NbtFlavor." + value.Name + ") instead.", nameof(value));
                }
                Volatile.Write(ref defaultFlavor, value);
            }
        }

        /// <summary> Initial <see cref="ValidateOnRead"/> value for new options and for entry
        /// points constructed without options. Initially <c>false</c>. Enabling it process-wide
        /// makes documents that merely bend a flavor's rules or repeat a name throw where they
        /// previously loaded, even in code that did not opt in. </summary>
        public static bool DefaultValidateOnRead {
            get { return CurrentPolicy.ValidateOnRead; }
            set { ReplacePolicy(value, null, null); }
        }

        /// <summary> Initial <see cref="ValidateOnWrite"/> value for new options and for entry
        /// points constructed without options. Initially <c>true</c>. Turning it off process-wide
        /// trades conformance checking away for restricted flavors: writes that relied on the
        /// refusal start producing nonconformant documents silently. </summary>
        public static bool DefaultValidateOnWrite {
            get { return CurrentPolicy.ValidateOnWrite; }
            set { ReplacePolicy(null, value, null); }
        }

        /// <summary> Initial <see cref="MaxAllocation"/> value for new options and for entry
        /// points constructed without options. Initially <see cref="long.MaxValue"/>, meaning no
        /// limit. </summary>
        /// <exception cref="ArgumentOutOfRangeException"> value is zero or negative. </exception>
        public static long DefaultMaxAllocation {
            get { return CurrentPolicy.MaxAllocation; }
            set {
                if (value <= 0) {
                    throw new ArgumentOutOfRangeException(nameof(value), value,
                                                          "DefaultMaxAllocation must be positive.");
                }
                ReplacePolicy(null, null, value);
            }
        }


        // The no-op early-out keeps the policy object's identity stable, so a same-value set
        // does not rebuild NbtCodec.For's cached codecs.
        static void ReplacePolicy(bool? validateOnRead, bool? validateOnWrite, long? maxAllocation) {
            PolicySnapshot policy = CurrentPolicy;
            bool newRead = validateOnRead ?? policy.ValidateOnRead;
            bool newWrite = validateOnWrite ?? policy.ValidateOnWrite;
            long newMax = maxAllocation ?? policy.MaxAllocation;
            if (newRead == policy.ValidateOnRead && newWrite == policy.ValidateOnWrite &&
                newMax == policy.MaxAllocation) {
                return;
            }
            Volatile.Write(ref defaultPolicy, new PolicySnapshot(newRead, newWrite, newMax));
        }


        internal static PolicySnapshot CurrentPolicy {
            get { return Volatile.Read(ref defaultPolicy); }
        }


        /// <summary> Creates options initialized from the current defaults
        /// (<see cref="DefaultFlavor"/> and the other <c>Default*</c> properties).
        /// Later changes to the defaults do not affect this instance. </summary>
        public NbtOptions() {
            Flavor = Volatile.Read(ref defaultFlavor);
            PolicySnapshot policy = CurrentPolicy;
            ValidateOnRead = policy.ValidateOnRead;
            ValidateOnWrite = policy.ValidateOnWrite;
            MaxAllocation = policy.MaxAllocation;
        }


        /// <summary> Creates options for the given flavor, with the current default policy
        /// settings. Any flavor is accepted here; the named-root entry points reject flavors
        /// without a root name themselves. </summary>
        /// <param name="flavor"> The wire encoding to read and write. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="flavor"/> is <c>null</c>. </exception>
        public NbtOptions(NbtFlavor flavor)
            : this() {
            if (flavor == null) throw new ArgumentNullException(nameof(flavor));
            Flavor = flavor;
        }


        /// <summary> The wire encoding to read and write.
        /// Initialized from <see cref="DefaultFlavor"/>. </summary>
        public NbtFlavor Flavor { get; set; }

        /// <summary> Whether reads enforce the flavor's conformance rules (permitted tag types,
        /// string ceilings, and for <see cref="NbtCodec"/> the root tag type) and the format's
        /// rule against a repeated name in a compound, in addition to parsing. The repeated-name
        /// check covers the members a load builds into a compound; <see cref="NbtReader"/>'s
        /// streaming walk and the members a selector skips go unchecked. Initialized from
        /// <see cref="DefaultValidateOnRead"/>. When off, reads accept anything parseable, so
        /// files that merely bend the rules still load, and a repeated name keeps the last value
        /// in the first tag's place, the way Minecraft loads it. </summary>
        public bool ValidateOnRead { get; set; }

        /// <summary> Whether writes enforce the flavor's conformance rules, refusing to produce a
        /// document the flavor's own readers would reject. Initialized from
        /// <see cref="DefaultValidateOnWrite"/>. Flavors without restrictions
        /// (like <see cref="NbtFlavor.Java"/>) pay no cost either way. </summary>
        public bool ValidateOnWrite { get; set; }

        long maxAllocation;

        /// <summary> Maximum size, in bytes, of any single allocation made on behalf of a length
        /// declared in the input: array payloads, list-as-array reads, strings, and the array of
        /// references a loaded list holds its elements in (one pointer per element; the element
        /// objects themselves are not counted). Guards against tiny corrupt or hostile documents
        /// declaring huge lengths, which matters most on compressed and non-seekable streams where
        /// declared lengths cannot be checked against the bytes actually available.
        /// Per-allocation, not a total document quota. Initialized from
        /// <see cref="DefaultMaxAllocation"/>; <see cref="long.MaxValue"/> means no limit. </summary>
        /// <exception cref="ArgumentOutOfRangeException"> value is zero or negative. </exception>
        public long MaxAllocation {
            get { return maxAllocation; }
            set {
                if (value <= 0) {
                    throw new ArgumentOutOfRangeException(nameof(value), value,
                                                          "MaxAllocation must be positive.");
                }
                maxAllocation = value;
            }
        }


        // Immutable settings for one entry point, produced by the Resolve methods below so that
        // every option is read exactly once and validated before use. A concurrently mutated
        // options instance can therefore never bypass validation.
        internal readonly struct Resolved {
            public readonly NbtFlavor Flavor;
            public readonly bool ValidateOnRead;
            public readonly bool ValidateOnWrite;
            public readonly long MaxAllocation;

            public Resolved(NbtFlavor flavor, bool validateOnRead, bool validateOnWrite,
                            long maxAllocation) {
                Flavor = flavor;
                ValidateOnRead = validateOnRead;
                ValidateOnWrite = validateOnWrite;
                MaxAllocation = maxAllocation;
            }

            public Resolved(NbtFlavor flavor, PolicySnapshot policy)
                : this(flavor, policy.ValidateOnRead, policy.ValidateOnWrite, policy.MaxAllocation) { }
        }


        // For the named-root entry points (NbtFile, NbtReader, NbtWriter)
        internal static Resolved ResolveForFile(NbtOptions options, string paramName) {
            Resolved resolved = ResolveForCodec(options, paramName);
            resolved.Flavor.EnsureUsableForFiles(paramName);
            return resolved;
        }


        // For flavor-overload entry points: the given flavor plus the current policy defaults
        internal static Resolved ResolveForFile(NbtFlavor flavor, string paramName) {
            Resolved resolved = ResolveForCodec(flavor, paramName);
            flavor.EnsureUsableForFiles(paramName);
            return resolved;
        }


        // For flavorless entry points. DefaultFlavor is always usable for files, so no check.
        internal static Resolved ResolveDefaults() {
            return new Resolved(Volatile.Read(ref defaultFlavor), CurrentPolicy);
        }


        // NbtCodec permits flavors without a root name
        internal static Resolved ResolveForCodec(NbtOptions options, string paramName) {
            if (options == null) throw new ArgumentNullException(paramName);
            NbtFlavor flavor = options.Flavor;
            if (flavor == null) {
                throw new ArgumentNullException(paramName, "Options must name a flavor.");
            }
            return new Resolved(flavor, options.ValidateOnRead, options.ValidateOnWrite,
                                options.MaxAllocation);
        }


        internal static Resolved ResolveForCodec(NbtFlavor flavor, string paramName) {
            if (flavor == null) throw new ArgumentNullException(paramName);
            return new Resolved(flavor, CurrentPolicy);
        }
    }
}

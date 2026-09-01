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


        sealed class DefaultsSnapshot {
            public readonly NbtFlavor Flavor;
            public readonly PolicySnapshot Policy;

            public DefaultsSnapshot(NbtFlavor flavor, PolicySnapshot policy) {
                Flavor = flavor;
                Policy = policy;
            }
        }


        // NbtFlavor initialization must never read this class back: the type initializers
        // stay cycle-free only because the dependency runs strictly NbtOptions -> NbtFlavor.
        static DefaultsSnapshot defaults =
            new DefaultsSnapshot(NbtFlavor.Java, new PolicySnapshot(
                validateOnRead: false, validateOnWrite: true, maxAllocation: long.MaxValue));


        /// <summary> The flavor used by entry points constructed without one: <see cref="NbtFile"/>,
        /// <see cref="NbtReader"/>, <see cref="NbtWriter"/>, and <c>NbtFile.ReadRootTagName</c>.
        /// Initially <see cref="NbtFlavor.Java"/>. Every such entry point works with named roots,
        /// so flavors without a root name are rejected here; pass those to <see cref="NbtCodec"/>
        /// explicitly. </summary>
        /// <exception cref="ArgumentNullException"> value is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> value is a flavor without a root name. </exception>
        public static NbtFlavor DefaultFlavor {
            get { return Volatile.Read(ref defaults).Flavor; }
            set {
                if (value == null) throw new ArgumentNullException(nameof(value));
                if (!value.HasRootName) {
                    throw new ArgumentException(
                        "The " + value.Name + " flavor has no root name, so it cannot be a default: " +
                        "NbtFile, NbtReader, and NbtWriter all use named roots. " +
                        "Use NbtCodec.For(NbtFlavor." + value.Name + ") instead.", nameof(value));
                }
                DefaultsSnapshot old, updated;
                do {
                    old = Volatile.Read(ref defaults);
                    if (old.Flavor == value) return;
                    updated = new DefaultsSnapshot(value, old.Policy);
                } while (Interlocked.CompareExchange(ref defaults, updated, old) != old);
            }
        }

        /// <summary> Initial <see cref="ValidateOnRead"/> value for new options and for entry
        /// points constructed without options. Initially <c>false</c>. Enabling it process-wide
        /// makes documents that merely bend a flavor's rules throw where they previously loaded,
        /// even in code that did not opt in. </summary>
        public static bool DefaultValidateOnRead {
            get { return Volatile.Read(ref defaults).Policy.ValidateOnRead; }
            set { ReplacePolicy(value, null, null); }
        }

        /// <summary> Initial <see cref="ValidateOnWrite"/> value for new options and for entry
        /// points constructed without options. Initially <c>true</c>. Turning it off process-wide
        /// trades conformance checking away for restricted flavors: writes that relied on the
        /// refusal start producing nonconformant documents silently. </summary>
        public static bool DefaultValidateOnWrite {
            get { return Volatile.Read(ref defaults).Policy.ValidateOnWrite; }
            set { ReplacePolicy(null, value, null); }
        }

        /// <summary> Initial <see cref="MaxAllocation"/> value for new options and for entry
        /// points constructed without options. Initially <see cref="long.MaxValue"/>, meaning no
        /// limit. </summary>
        /// <exception cref="ArgumentOutOfRangeException"> value is zero or negative. </exception>
        public static long DefaultMaxAllocation {
            get { return Volatile.Read(ref defaults).Policy.MaxAllocation; }
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
            DefaultsSnapshot old, updated;
            do {
                old = Volatile.Read(ref defaults);
                PolicySnapshot policy = old.Policy;
                bool newRead = validateOnRead ?? policy.ValidateOnRead;
                bool newWrite = validateOnWrite ?? policy.ValidateOnWrite;
                long newMax = maxAllocation ?? policy.MaxAllocation;
                if (newRead == policy.ValidateOnRead && newWrite == policy.ValidateOnWrite &&
                    newMax == policy.MaxAllocation) {
                    return;
                }
                updated = new DefaultsSnapshot(old.Flavor, new PolicySnapshot(newRead, newWrite, newMax));
            } while (Interlocked.CompareExchange(ref defaults, updated, old) != old);
        }


        internal static PolicySnapshot CurrentPolicy {
            get { return Volatile.Read(ref defaults).Policy; }
        }


        /// <summary> Creates options initialized from the current defaults
        /// (<see cref="DefaultFlavor"/> and the other <c>Default*</c> properties).
        /// Later changes to the defaults do not affect this instance. </summary>
        public NbtOptions() {
            DefaultsSnapshot state = Volatile.Read(ref defaults);
            Flavor = state.Flavor;
            ValidateOnRead = state.Policy.ValidateOnRead;
            ValidateOnWrite = state.Policy.ValidateOnWrite;
            MaxAllocation = state.Policy.MaxAllocation;
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


        // For NbtCodec.For: builds from the captured policy, not a fresh read, so the cached
        // (policy, codec) pair stays coherent.
        internal NbtOptions(NbtFlavor flavor, PolicySnapshot policy) {
            Flavor = flavor;
            ValidateOnRead = policy.ValidateOnRead;
            ValidateOnWrite = policy.ValidateOnWrite;
            MaxAllocation = policy.MaxAllocation;
        }


        /// <summary> The wire encoding to read and write.
        /// Initialized from <see cref="DefaultFlavor"/>. </summary>
        public NbtFlavor Flavor { get; set; }

        /// <summary> Whether reads enforce the flavor's conformance rules (permitted tag types,
        /// string ceilings, and for <see cref="NbtCodec"/> the root tag type) in addition to
        /// parsing. Initialized from <see cref="DefaultValidateOnRead"/>. When off, reads accept
        /// anything parseable, so files that merely bend the rules still load. </summary>
        public bool ValidateOnRead { get; set; }

        /// <summary> Whether writes enforce the flavor's conformance rules, refusing to produce a
        /// document the flavor's own readers would reject. Initialized from
        /// <see cref="DefaultValidateOnWrite"/>. Flavors without restrictions
        /// (like <see cref="NbtFlavor.Java"/>) pay no cost either way. </summary>
        public bool ValidateOnWrite { get; set; }

        /// <summary> Maximum size, in bytes, of any single allocation made on behalf of a length
        /// declared in the input: array payloads, list-as-array reads, and strings. Guards against
        /// tiny corrupt or hostile documents declaring huge lengths, which matters most on
        /// compressed and non-seekable streams where declared lengths cannot be checked against the
        /// bytes actually available. Per-allocation, not a total document quota. Initialized from
        /// <see cref="DefaultMaxAllocation"/>; <see cref="long.MaxValue"/> means no limit, and the
        /// value must be positive when the options are used. </summary>
        public long MaxAllocation { get; set; }


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
        }


        // For the named-root entry points (NbtFile, NbtReader, NbtWriter)
        internal static Resolved ResolveForFile(NbtOptions options, string paramName) {
            Resolved resolved = ResolveForCodec(options, paramName);
            resolved.Flavor.EnsureUsableForFiles(paramName);
            return resolved;
        }


        // For flavor-overload entry points: the given flavor plus the current policy defaults
        internal static Resolved ResolveForFile(NbtFlavor flavor, string paramName) {
            if (flavor == null) throw new ArgumentNullException(paramName);
            flavor.EnsureUsableForFiles(paramName);
            PolicySnapshot policy = CurrentPolicy;
            return new Resolved(flavor, policy.ValidateOnRead, policy.ValidateOnWrite,
                                policy.MaxAllocation);
        }


        // For flavorless entry points. DefaultFlavor is always usable for files, so no check.
        internal static Resolved ResolveDefaults() {
            DefaultsSnapshot state = Volatile.Read(ref defaults);
            return new Resolved(state.Flavor, state.Policy.ValidateOnRead,
                                state.Policy.ValidateOnWrite, state.Policy.MaxAllocation);
        }


        // NbtCodec permits flavors without a root name
        internal static Resolved ResolveForCodec(NbtOptions options, string paramName) {
            if (options == null) throw new ArgumentNullException(paramName);
            NbtFlavor flavor = options.Flavor;
            if (flavor == null) {
                throw new ArgumentNullException(paramName, "Options must name a flavor.");
            }
            long maxAllocation = options.MaxAllocation;
            if (maxAllocation <= 0) {
                throw new ArgumentOutOfRangeException(paramName, maxAllocation,
                                                      "MaxAllocation must be positive.");
            }
            return new Resolved(flavor, options.ValidateOnRead, options.ValidateOnWrite,
                                maxAllocation);
        }
    }
}

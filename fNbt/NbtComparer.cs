using System;
using System.Collections.Generic;

namespace fNbt {
    /// <summary>
    ///   Compares NbtTag for equality by comparing their types, names, and values. Considers compound tags to be equal
    ///   if they contain equal sets of tags. Considered list tags to be equal if their tags are equal and in the same order.
    ///   Name comparisons are case-sensitive.
    /// </summary>
    public sealed class NbtComparer : IEqualityComparer<NbtTag> {
        /// <summary> Gets a singleton instance of the NbtComparer. </summary>
        public static NbtComparer Instance { get; } = new NbtComparer();

        private NbtComparer() { }

        /// <inheritdoc/>
        public bool Equals(NbtTag? x, NbtTag? y) {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            if (x.TagType != y.TagType) return false;
            if (!String.Equals(x.Name, y.Name, StringComparison.Ordinal)) return false; // null names are permitted
            return DeepEquals(x, y);
        }

        /// <inheritdoc/>
        public int GetHashCode(NbtTag tag) {
            if (tag is null) throw new ArgumentNullException(nameof(tag));

            unchecked {
                int hash = tag.TagType.GetHashCode() * 23;
                if (tag.Name != null)
                    hash ^= StringComparer.Ordinal.GetHashCode(tag.Name);

                switch (tag.TagType) {
                    case NbtTagType.ByteArray:
                        var ba = ((NbtByteArray)tag).ByteArrayValue;
                        hash = (hash * 23) ^ ba.Length.GetHashCode();
                        return hash;

                    case NbtTagType.IntArray:
                        var ia = ((NbtIntArray)tag).IntArrayValue;
                        hash = (hash * 23) ^ ia.Length.GetHashCode();
                        return hash;

                    case NbtTagType.LongArray:
                        var la = ((NbtLongArray)tag).LongArrayValue;
                        hash = (hash * 23) ^ la.Length.GetHashCode();
                        return hash;

                    case NbtTagType.List:
                        var list = (NbtList)tag;
                        hash = (hash * 23) ^ list.ListType.GetHashCode();
                        hash = (hash * 23) ^ list.Count.GetHashCode();
                        return hash;

                    case NbtTagType.Compound:
                        var comp = (NbtCompound)tag;
                        hash = (hash * 23) ^ comp.Count.GetHashCode();
                        return hash;

                    default:
                        // primitives and strings
                        var raw = GetRawValue(tag);
                        if (raw != null)
                            return (hash * 23) ^ raw.GetHashCode();

                        // END and unknown
                        throw new ArgumentException("Cannot hash tags of type " + tag.TagType, nameof(tag));
                }
            }
        }

        // Compare detailed attributes of two given tags
        private bool DeepEquals(NbtTag x, NbtTag y) {
            // Assume that tags have same type and are non-null
            switch (x.TagType) {
                case NbtTagType.ByteArray: {
                        var a1 = ((NbtByteArray)x).ByteArrayValue;
                        var a2 = ((NbtByteArray)y).ByteArrayValue;
                        if (a1.Length != a2.Length) return false;
                        for (int i = 0; i < a1.Length; i++)
                            if (a1[i] != a2[i]) return false;
                        return true;
                    }
                case NbtTagType.IntArray: {
                        var a1 = ((NbtIntArray)x).IntArrayValue;
                        var a2 = ((NbtIntArray)y).IntArrayValue;
                        if (a1.Length != a2.Length) return false;
                        for (int i = 0; i < a1.Length; i++)
                            if (a1[i] != a2[i]) return false;
                        return true;
                    }
                case NbtTagType.LongArray: {
                        var a1 = ((NbtLongArray)x).LongArrayValue;
                        var a2 = ((NbtLongArray)y).LongArrayValue;
                        if (a1.Length != a2.Length) return false;
                        for (int i = 0; i < a1.Length; i++)
                            if (a1[i] != a2[i]) return false;
                        return true;
                    }
                case NbtTagType.Compound: {
                        // Compounds are equal if their child-count and contents are equal
                        var xc = (NbtCompound)x;
                        var yc = (NbtCompound)y;
                        if (xc.Count != yc.Count) return false;
                        return new HashSet<NbtTag>(xc, this).SetEquals(yc);
                    }
                case NbtTagType.List: {
                        // Lists are considered equal if their type, count, and contents are equal
                        var xl = (NbtList)x;
                        var yl = (NbtList)y;
                        if (xl.ListType != yl.ListType || xl.Count != yl.Count) return false;
                        for (int i = 0; i < xl.Count; i++)
                            if (!Equals(xl[i], yl[i])) return false;
                        return true;
                    }
                default: {
                        // primitives and strings are equal if their values are equal
                        var rawValue1 = GetRawValue(x);
                        if (rawValue1 != null) {
                            var rawValue2 = GetRawValue(y);
                            return Equals(rawValue1, rawValue2);
                        }
                        // END and unknown
                        throw new ArgumentException("Cannot compare tags of type " + x.TagType);
                    }
            }
        }

        private static object? GetRawValue(NbtTag tag) {
            return tag.TagType switch {
                NbtTagType.Byte => tag.ByteValue,
                NbtTagType.Double => tag.DoubleValue,
                NbtTagType.Float => tag.FloatValue,
                NbtTagType.Int => tag.IntValue,
                NbtTagType.Long => tag.LongValue,
                NbtTagType.Short => tag.ShortValue,
                NbtTagType.String => tag.StringValue,
                _ => null, // End, Unknown, array, and compount tags
            };
        }
    }
}

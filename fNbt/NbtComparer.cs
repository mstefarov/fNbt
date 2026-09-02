using System;
using System.Collections.Generic;

namespace fNbt {
    /// <summary> Compares tags for equality by type, name, and value. Compound tags are equal
    /// when they contain equal sets of tags; list tags when their elements are equal and in the
    /// same order. Name comparisons are case-sensitive. </summary>
    public sealed class NbtComparer : IEqualityComparer<NbtTag> {
        /// <summary> Gets a singleton instance of the NbtComparer. </summary>
        public static NbtComparer Instance { get; } = new NbtComparer();


        NbtComparer() { }

        /// <inheritdoc/>
        /// <exception cref="ArgumentException"> Either tag is nested more than 512 levels deep. </exception>
        public bool Equals(NbtTag? x, NbtTag? y) {
            return Equals(x, y, NbtTag.MaxDepth);
        }


        bool Equals(NbtTag? x, NbtTag? y, int depthBudget) {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            if (x.TagType != y.TagType) return false;
            if (!String.Equals(x.Name, y.Name, StringComparison.Ordinal)) return false; // null names are permitted
            return DeepEquals(x, y, depthBudget);
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
                        byte[] ba = ((NbtByteArray)tag).Value;
                        hash = (hash * 23) ^ ba.Length.GetHashCode();
                        return hash;

                    case NbtTagType.IntArray:
                        int[] ia = ((NbtIntArray)tag).Value;
                        hash = (hash * 23) ^ ia.Length.GetHashCode();
                        return hash;

                    case NbtTagType.LongArray:
                        long[] la = ((NbtLongArray)tag).Value;
                        hash = (hash * 23) ^ la.Length.GetHashCode();
                        return hash;

                    case NbtTagType.List:
                        NbtList list = (NbtList)tag;
                        hash = (hash * 23) ^ list.ListType.GetHashCode();
                        hash = (hash * 23) ^ list.Count.GetHashCode();
                        return hash;

                    case NbtTagType.Compound:
                        NbtCompound comp = (NbtCompound)tag;
                        hash = (hash * 23) ^ comp.Count.GetHashCode();
                        return hash;

                    case NbtTagType.Double: {
                        // All NaNs are Equals-equal so must hash alike, and .NET Framework's
                        // Double.GetHashCode does not normalize NaN payloads the way .NET Core's does.
                        double d = ((NbtDouble)tag).Value;
                        if (Double.IsNaN(d)) d = Double.NaN;
                        return (hash * 23) ^ d.GetHashCode();
                    }

                    case NbtTagType.Float: {
                        float f = ((NbtFloat)tag).Value;
                        if (Single.IsNaN(f)) f = Single.NaN;
                        return (hash * 23) ^ f.GetHashCode();
                    }

                    case NbtTagType.Byte:
                        return (hash * 23) ^ ((NbtByte)tag).Value.GetHashCode();

                    case NbtTagType.Short:
                        return (hash * 23) ^ ((NbtShort)tag).Value.GetHashCode();

                    case NbtTagType.Int:
                        return (hash * 23) ^ ((NbtInt)tag).Value.GetHashCode();

                    case NbtTagType.Long:
                        return (hash * 23) ^ ((NbtLong)tag).Value.GetHashCode();

                    case NbtTagType.String:
                        return (hash * 23) ^ StringComparer.Ordinal.GetHashCode(((NbtString)tag).Value);

                    default:
                        // END and unknown
                        throw new ArgumentException("Cannot hash tags of type " + tag.TagType, nameof(tag));
                }
            }
        }

        // Compare detailed attributes of two given tags
        bool DeepEquals(NbtTag x, NbtTag y, int depthBudget) {
            // Assume that tags have same type and are non-null.
            // Value comparisons stay typed so equal numeric leaves don't box.
            switch (x.TagType) {
                case NbtTagType.Byte:
                    return ((NbtByte)x).Value == ((NbtByte)y).Value;
                case NbtTagType.Short:
                    return ((NbtShort)x).Value == ((NbtShort)y).Value;
                case NbtTagType.Int:
                    return ((NbtInt)x).Value == ((NbtInt)y).Value;
                case NbtTagType.Long:
                    return ((NbtLong)x).Value == ((NbtLong)y).Value;
                case NbtTagType.Float:
                    // Equals, not ==, so NaNs compare equal to each other
                    return ((NbtFloat)x).Value.Equals(((NbtFloat)y).Value);
                case NbtTagType.Double:
                    return ((NbtDouble)x).Value.Equals(((NbtDouble)y).Value);
                case NbtTagType.String:
                    return String.Equals(((NbtString)x).Value, ((NbtString)y).Value, StringComparison.Ordinal);
                case NbtTagType.ByteArray: {
                        byte[] a1 = ((NbtByteArray)x).Value;
                        byte[] a2 = ((NbtByteArray)y).Value;
#if NET8_0_OR_GREATER
                        return a1.AsSpan().SequenceEqual(a2);
#else
                        if (a1.Length != a2.Length) return false;
                        for (int i = 0; i < a1.Length; i++)
                            if (a1[i] != a2[i]) return false;
                        return true;
#endif
                    }
                case NbtTagType.IntArray: {
                        int[] a1 = ((NbtIntArray)x).Value;
                        int[] a2 = ((NbtIntArray)y).Value;
#if NET8_0_OR_GREATER
                        return a1.AsSpan().SequenceEqual(a2);
#else
                        if (a1.Length != a2.Length) return false;
                        for (int i = 0; i < a1.Length; i++)
                            if (a1[i] != a2[i]) return false;
                        return true;
#endif
                    }
                case NbtTagType.LongArray: {
                        long[] a1 = ((NbtLongArray)x).Value;
                        long[] a2 = ((NbtLongArray)y).Value;
#if NET8_0_OR_GREATER
                        return a1.AsSpan().SequenceEqual(a2);
#else
                        if (a1.Length != a2.Length) return false;
                        for (int i = 0; i < a1.Length; i++)
                            if (a1[i] != a2[i]) return false;
                        return true;
#endif
                    }
                case NbtTagType.Compound: {
                        int childDepthBudget = ConsumeDepthBudget(depthBudget, nameof(x));
                        // Child names are unique, so every child of x must have a same-named one in y.
                        // Looking them up beats a HashSet: no reliance on hash quality, and it can carry depth.
                        NbtCompound xc = (NbtCompound)x;
                        NbtCompound yc = (NbtCompound)y;
                        if (xc.Count != yc.Count) return false;
                        NbtTag[] xChildren = xc.ItemArray;
                        for (int i = 0; i < xc.Count; i++) {
                            NbtTag xChild = xChildren[i];
                            NbtTag? yChild = yc.Get(xChild.Name!);
                            if (yChild == null || !Equals(xChild, yChild, childDepthBudget)) return false;
                        }
                        return true;
                    }
                case NbtTagType.List: {
                        int childDepthBudget = ConsumeDepthBudget(depthBudget, nameof(x));
                        // Lists are considered equal if their type, count, and contents are equal
                        NbtList xl = (NbtList)x;
                        NbtList yl = (NbtList)y;
                        if (xl.ListType != yl.ListType || xl.Count != yl.Count) return false;
                        for (int i = 0; i < xl.Count; i++)
                            if (!Equals(xl.tags[i], yl.tags[i], childDepthBudget)) return false;
                        return true;
                    }
                default:
                    // END and unknown
                    throw new ArgumentException("Cannot compare tags of type " + x.TagType);
            }
        }


        static int ConsumeDepthBudget(int depthBudget, string paramName) {
            if (depthBudget <= 0) {
                throw new ArgumentException(
                    "Tags are nested deeper than " + NbtTag.MaxDepth + " levels.", paramName);
            }
            return depthBudget - 1;
        }

    }
}

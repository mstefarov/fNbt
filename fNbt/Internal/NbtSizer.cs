using System;

namespace fNbt {
    // Exact encoded size of a tree, byte for byte what NbtBinaryWriter will emit. With validation
    // on, the same walk enforces the flavor's tag-type and string ceilings in ValidateTree's
    // order. Validation rides along as a struct type argument: the JIT compiles a checking and a
    // check-free copy of the walk from this one source, so the unvalidated walk pays nothing.
    internal static class NbtSizer {
        struct Validated { }

        struct Unvalidated { }


        // Size of one complete document: type byte, optional root name, payload.
        public static long SizeDocument(NbtTag tag, bool withName, NbtFlavor flavor, bool validate) {
            return validate
                ? SizeDocument<Validated>(tag, withName, flavor)
                : SizeDocument<Unvalidated>(tag, withName, flavor);
        }


        // Folds to a constant in each instantiation
        static bool Validates<TMode>() where TMode : struct {
            return typeof(TMode) == typeof(Validated);
        }


        static long SizeDocument<TMode>(NbtTag tag, bool withName, NbtFlavor flavor) where TMode : struct {
            NbtTagType type = tag.TagType;
            if (Validates<TMode>() && type > flavor.MaxTagType) {
                throw NbtFormatException.NotPermitted(flavor, type);
            }
            long size = 1;
            if (withName) {
                size += SizeString<TMode>(tag.Name ?? "", flavor);
            } else if (Validates<TMode>() && tag.Name != null) {
                flavor.ValidateString(tag.Name);
            }
            return size + SizePayload<TMode>(tag, flavor, NbtTag.MaxDepth);
        }


        // The switch reads the tag's own type: handing a caller-read type in defeats the JIT's
        // guarded devirtualization, which folds the switch for the hottest tag class here.
        static long SizePayload<TMode>(NbtTag tag, NbtFlavor flavor, int depthBudget) where TMode : struct {
            switch (tag.TagType) {
                case NbtTagType.Byte:
                    return 1;

                case NbtTagType.Short:
                    return 2;

                case NbtTagType.Int:
                    return flavor.UsesVarInts ? ZigZagLength(((NbtInt)tag).Value) : 4;

                case NbtTagType.Long:
                    return flavor.UsesVarInts ? ZigZagLength(((NbtLong)tag).Value) : 8;

                case NbtTagType.Float:
                    return 4;

                case NbtTagType.Double:
                    return 8;

                case NbtTagType.String:
                    return SizeString<TMode>(((NbtString)tag).Value, flavor);

                case NbtTagType.ByteArray: {
                    byte[] value = ((NbtByteArray)tag).Value;
                    return SizeCount(value.Length, flavor) + value.Length;
                }

                case NbtTagType.IntArray: {
                    int[] value = ((NbtIntArray)tag).Value;
                    long size = SizeCount(value.Length, flavor);
                    if (flavor.UsesVarInts) {
                        foreach (int element in value) size += ZigZagLength(element);
                    } else {
                        size += (long)value.Length * 4;
                    }
                    return size;
                }

                case NbtTagType.LongArray: {
                    long[] value = ((NbtLongArray)tag).Value;
                    long size = SizeCount(value.Length, flavor);
                    if (flavor.UsesVarInts) {
                        foreach (long element in value) size += ZigZagLength(element);
                    } else {
                        size += (long)value.Length * 8;
                    }
                    return size;
                }

                case NbtTagType.List: {
                    int childDepthBudget = NbtTag.ConsumeDepthBudget(depthBudget);
                    NbtList list = (NbtList)tag;
                    NbtTagType listType = list.ListType;
                    if (listType == NbtTagType.Unknown) {
                        // The write pass would refuse this list; fail the same way before it
                        throw NbtFormatException.UnknownListType();
                    }
                    if (Validates<TMode>() && listType > flavor.MaxTagType) {
                        throw NbtFormatException.NotPermitted(flavor, listType);
                    }
                    long size = 1 + SizeCount(list.tags.Count, flavor);
                    foreach (NbtTag child in list.tags) {
                        size += SizePayload<TMode>(child, flavor, childDepthBudget);
                    }
                    return size;
                }

                case NbtTagType.Compound: {
                    int childDepthBudget = NbtTag.ConsumeDepthBudget(depthBudget);
                    NbtCompound compound = (NbtCompound)tag;
                    NbtTag[] children = compound.ItemArray;
                    long size = 1; // the closing TAG_End
                    for (int i = 0; i < compound.Count; i++) {
                        NbtTag child = children[i];
                        if (Validates<TMode>() && child.TagType > flavor.MaxTagType) {
                            throw NbtFormatException.NotPermitted(flavor, child.TagType);
                        }
                        if (child.Name == null) {
                            throw NbtFormatException.UnnamedChild();
                        }
                        size += 1 + SizeString<TMode>(child.Name, flavor)
                                  + SizePayload<TMode>(child, flavor, childDepthBudget);
                    }
                    return size;
                }

                default:
                    throw new NbtFormatException("Cannot size tags of type " + tag.TagType);
            }
        }


        static long SizeString<TMode>(string value, NbtFlavor flavor) where TMode : struct {
            long bytes = NbtStringCodec.IsAsciiNoNul(value)
                ? value.Length
                : NbtStringCodec.GetByteCount(value, flavor.UsesModifiedUtf8);
            if (Validates<TMode>() && bytes > flavor.MaxStringBytes) {
                throw flavor.StringTooLong(bytes);
            }
            if (flavor.UsesVarInts) {
                return UnsignedVarIntLength((ulong)bytes) + bytes;
            }
            return 2 + bytes;
        }


        // Container and array lengths are zigzag varints under BedrockNetwork, fixed 4 bytes elsewhere
        static long SizeCount(int count, NbtFlavor flavor) {
            return flavor.UsesVarInts ? ZigZagLength(count) : 4;
        }


        static int ZigZagLength(int value) {
            return UnsignedVarIntLength((uint)((value << 1) ^ (value >> 31)));
        }


        static int ZigZagLength(long value) {
            return UnsignedVarIntLength((ulong)((value << 1) ^ (value >> 63)));
        }


        static int UnsignedVarIntLength(ulong value) {
            int length = 1;
            while (value >= 0x80) {
                value >>= 7;
                length++;
            }
            return length;
        }
    }
}

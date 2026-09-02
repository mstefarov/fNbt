using System;

namespace fNbt {
    // Exact encoded size of a tree, byte for byte what NbtBinaryWriter will emit.
    // Flavor conformance is left to the write pass that follows.
    internal static class NbtSizer {
        // Size of one complete document: type byte, optional root name, payload.
        public static long SizeDocument(NbtTag tag, bool withName, NbtFlavor flavor) {
            long size = 1;
            if (withName) {
                size += SizeString(tag.Name ?? "", flavor);
            }
            return size + SizePayload(tag, flavor, NbtTag.MaxDepth);
        }


        static long SizePayload(NbtTag tag, NbtFlavor flavor, int depthBudget) {
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
                    return SizeString(((NbtString)tag).Value, flavor);

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
                    if (list.ListType == NbtTagType.Unknown) {
                        // The write pass would refuse this list; fail the same way before it
                        throw new NbtFormatException(NbtList.UnknownListTypeError);
                    }
                    long size = 1 + SizeCount(list.tags.Count, flavor);
                    foreach (NbtTag child in list.tags) {
                        size += SizePayload(child, flavor, childDepthBudget);
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
                        if (child.Name == null) {
                            throw new NbtFormatException("Name is null");
                        }
                        size += 1 + SizeString(child.Name, flavor)
                                  + SizePayload(child, flavor, childDepthBudget);
                    }
                    return size;
                }

                default:
                    throw new NbtFormatException("Cannot size tags of type " + tag.TagType);
            }
        }


        static long SizeString(string value, NbtFlavor flavor) {
            long bytes = NbtStringCodec.IsAsciiNoNul(value)
                ? value.Length
                : NbtStringCodec.GetByteCount(value, flavor.UsesModifiedUtf8);
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

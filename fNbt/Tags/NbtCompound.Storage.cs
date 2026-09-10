using System;
using System.Runtime.CompilerServices;

namespace fNbt {
    partial class NbtCompound {
        // Children in insertion order. Real compounds are small, 0-5 children in the Bedrock
        // block palette and 13 in ClassicWorld's schema, so anything below IndexThreshold just
        // scans and compares names. An empty compound shares the empty array.
        NbtTag[] items = Array.Empty<NbtTag>();
        int count;
        int version;

        // Index for larger compounds: open-addressed slots of (hash << 32) | (position + 2),
        // where 0 is empty and 1 a tombstone. Storing positions instead of references keeps
        // names out of the table, so growth re-buckets without touching a single string.
        // Load stays at or below one half.
        ulong[]? table;
        int tombstones;

        const int IndexThreshold = 16;
        const int InitialCapacity = 4;
        const int InitialTableSize = 64;

        // Lazily created by ICollection.SyncRoot.
        object? syncRoot;


        static uint NameHash(string tagName) {
            // The runtime's seeded string hash, so crafted names can't force long probe runs
            return (uint)tagName.GetHashCode();
        }


        // Direct view for internal walkers. Slots at Count and beyond are unspecified.
        internal NbtTag[] ItemArray {
            get { return items; }
        }


        NbtTag? Find(string tagName) {
            int position = FindPosition(tagName);
            return position < 0 ? null : items[position];
        }


        int FindPosition(string tagName) {
            ulong[]? t = table;
            if (t == null) return FindLinearPosition(tagName);
            uint hash = NameHash(tagName);
            int mask = t.Length - 1;
            int i = (int)(hash & (uint)mask);
            while (true) {
                ulong slot = t[i];
                if (slot == 0) return -1;
                if (slot != 1 && (uint)(slot >> 32) == hash) {
                    int position = (int)(uint)slot - 2;
                    if (items[position].name == tagName) return position;
                }
                i = (i + 1) & mask;
            }
        }


        int FindLinearPosition(string tagName) {
#if NET8_0_OR_GREATER
            // The span gets its bounds check hoisted where a loop over the count field cannot
            ReadOnlySpan<NbtTag> children = items.AsSpan(0, count);
            for (int i = 0; i < children.Length; i++) {
                // == starts with a reference check, which parsed names usually pass: readers
                // hand out one shared string per repeated name
                if (children[i].name == tagName) return i;
            }
#else
            NbtTag[] local = items;
            for (int i = 0; i < count; i++) {
                if (local[i].name == tagName) return i;
            }
#endif
            return -1;
        }


        // Inserts unless the name is taken. One probe pass serves both the duplicate check
        // and the slot search. Callers set Parent.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool TryInsert(NbtTag tag) {
            string tagName = tag.name!;
            ulong[]? t = table;
            if (t == null) {
                if (FindLinearPosition(tagName) >= 0) return false;
                Append(tag);
                if (table == null && count > IndexThreshold) BuildTable();
                return true;
            }
            uint hash = NameHash(tagName);
            int mask = t.Length - 1;
            int i = (int)(hash & (uint)mask);
            int insertAt = -1;
            while (true) {
                ulong slot = t[i];
                if (slot == 0) {
                    if (insertAt < 0) insertAt = i;
                    break;
                }
                if (slot == 1) {
                    if (insertAt < 0) insertAt = i;
                } else if ((uint)(slot >> 32) == hash && items[(int)(uint)slot - 2].name == tagName) {
                    return false;
                }
                i = (i + 1) & mask;
            }
            if (t[insertAt] == 1) tombstones--;
            t[insertAt] = ((ulong)hash << 32) | ((uint)count + 2);
            Append(tag);
            if ((count + tombstones) * 2 >= t.Length) GrowTable();
            return true;
        }


        // Shaped like List<T>.Add: the bounds check doubles as the capacity check, and resizing
        // stays out of line so this inlines. The ref-based store skips the covariant-store
        // helper, safe here because items is always exactly NbtTag[].
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void Append(NbtTag tag) {
            NbtTag[] local = items;
            int c = count;
            if ((uint)c < (uint)local.Length) {
#if NET8_0_OR_GREATER
                Unsafe.Add(ref System.Runtime.InteropServices.MemoryMarshal.GetArrayDataReference(local), c) = tag;
#else
                local[c] = tag;
#endif
                count = c + 1;
                version++;
            } else {
                AppendWithResize(tag);
            }
        }


        [MethodImpl(MethodImplOptions.NoInlining)]
        void AppendWithResize(NbtTag tag) {
            EnsureCapacity(count + 1);
            items[count++] = tag;
            version++;
        }


        void EnsureCapacity(int neededTotal) {
            if (items.Length < neededTotal) {
                // Quadrupling past 64 saves large build-by-Add compounds several copies,
                // while small ones keep the tight sizes
                int newSize = items.Length < 64 ? items.Length * 2 : items.Length * 4;
                if (newSize < InitialCapacity) newSize = InitialCapacity;
                if (newSize < neededTotal) newSize = neededTotal;
                Array.Resize(ref items, newSize);
            }
        }


        // Appends a child whose name is known to be unique here. Callers set Parent.
        void AppendVerified(NbtTag tag) {
            ulong[]? t = table;
            if (t != null) {
                AddTableEntry(t, NameHash(tag.name!), count);
                Append(tag);
                if ((count + tombstones) * 2 >= t.Length) GrowTable();
                return;
            }
            Append(tag);
            if (count > IndexThreshold) BuildTable();
        }


        static void AddTableEntry(ulong[] t, uint hash, int position) {
            int mask = t.Length - 1;
            int i = (int)(hash & (uint)mask);
            while (t[i] > 1) {
                i = (i + 1) & mask;
            }
            t[i] = ((ulong)hash << 32) | ((uint)position + 2);
        }


        // Smallest table that keeps this many entries at or below half load, quadrupling the way
        // GrowTable does
        static int TableSizeFor(int count) {
            int size = InitialTableSize;
            while (count * 2 >= size) size *= 4;
            return size;
        }


        void BuildTable() {
            ulong[] t = new ulong[TableSizeFor(count)];
            NbtTag[] local = items;
            for (int position = 0; position < count; position++) {
                AddTableEntry(t, NameHash(local[position].name!), position);
            }
            table = t;
            tombstones = 0;
        }


        // Rebuilds at quadruple size when really full, or at the same size to purge tombstones.
        void GrowTable() {
            ulong[] old = table!;
            int newSize = count * 2 >= old.Length ? old.Length * 4 : old.Length;
            ulong[] t = new ulong[newSize];
            int mask = newSize - 1;
            foreach (ulong slot in old) {
                if (slot <= 1) continue;
                int i = (int)((uint)(slot >> 32) & (uint)mask);
                while (t[i] != 0) {
                    i = (i + 1) & mask;
                }
                t[i] = slot;
            }
            table = t;
            tombstones = 0;
        }


        void RemoveAt(int position, NbtTag tag) {
            NbtTag[] local = items;
            count--;
            if (position < count) {
                Array.Copy(local, position + 1, local, position, count - position);
            }
            local[count] = null!;
            version++;
            ulong[]? t = table;
            if (t != null) {
                TombstoneEntry(t, tag.name!, position);
                // Later children just shifted down one, so their stored positions follow.
                // Position lives in the low bits, offset by 2, so decrementing the whole slot
                // never borrows into the hash half.
                for (int s = 0; s < t.Length; s++) {
                    ulong slot = t[s];
                    if (slot > 1 && (int)(uint)slot - 2 > position) {
                        t[s] = slot - 1;
                    }
                }
                if (++tombstones * 4 >= t.Length) GrowTable();
            }
            tag.Parent = null;
        }


        static void TombstoneEntry(ulong[] t, string tagName, int position) {
            uint hash = NameHash(tagName);
            int mask = t.Length - 1;
            int i = (int)(hash & (uint)mask);
            // The entry exists and positions are unique, so the position alone identifies it.
            // Empty and tombstone slots decode to -2 and -1, which never match.
            while ((int)(uint)t[i] - 2 != position) {
                i = (i + 1) & mask;
            }
            t[i] = 1;
        }


        // Position of this exact instance. Names are unique, so an indexed compound can settle
        // it with one probe for the tag's name.
        int PositionOfExact(NbtTag tag) {
            string? tagName = tag.name;
            if (table != null && tagName != null) {
                int position = FindPosition(tagName);
                return position >= 0 && ReferenceEquals(items[position], tag) ? position : -1;
            }
            NbtTag[] local = items;
            for (int i = 0; i < count; i++) {
                if (ReferenceEquals(local[i], tag)) return i;
            }
            return -1;
        }


        // Re-key one entry after a rename: same position, new hash. The tombstone may push
        // load past the threshold.
        void RekeyEntry(string oldName, string newName) {
            ulong[]? t = table;
            if (t == null) return;
            int position = FindPosition(oldName);
            TombstoneEntry(t, oldName, position);
            tombstones++;
            AddTableEntry(t, NameHash(newName), position);
            if ((count + tombstones) * 2 >= t.Length) GrowTable();
        }
    }
}

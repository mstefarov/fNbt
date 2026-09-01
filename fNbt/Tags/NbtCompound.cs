using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace fNbt {
    /// <summary> A tag containing a collection of other named tags. Children are kept in the
    /// order they were added, and stay in it after removals: enumerating, saving, or printing
    /// this compound always lists them in insertion order. </summary>
    public sealed class NbtCompound : NbtTag, ICollection<NbtTag>, ICollection {
        /// <summary> Type of this tag (Compound). </summary>
        public override NbtTagType TagType {
            get { return NbtTagType.Compound; }
        }

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


        static uint NameHash(string tagName) {
            // The runtime's seeded string hash, so crafted names can't force long probe runs
            return (uint)tagName.GetHashCode();
        }


        /// <summary> Creates an empty unnamed NbtCompound tag. </summary>
        public NbtCompound() { }


        /// <summary> Creates an empty NbtCompound tag with the given name. </summary>
        /// <param name="tagName"> Name to assign to this tag. May be <c>null</c>. </param>
        public NbtCompound(string? tagName) {
            name = tagName;
        }


        /// <summary> Creates an unnamed NbtCompound tag, containing the given tags. </summary>
        /// <param name="tags"> Collection of tags to assign to this tag's Value. May not be null </param>
        /// <exception cref="ArgumentNullException"> <paramref name="tags"/> is <c>null</c>, or one of the tags is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> If some of the given tags were not named, or two tags with the same name were given. </exception>
        public NbtCompound(IEnumerable<NbtTag> tags)
            : this(null, tags) { }


        /// <summary> Creates an NbtCompound tag with the given name, containing the given tags. </summary>
        /// <param name="tagName"> Name to assign to this tag. May be <c>null</c>. </param>
        /// <param name="tags"> Collection of tags to assign to this tag's Value. May not be null </param>
        /// <exception cref="ArgumentNullException"> <paramref name="tags"/> is <c>null</c>, or one of the tags is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> If some of the given tags were not named, or two tags with the same name were given,
        /// or a tag already has a Parent. </exception>
        public NbtCompound(string? tagName, IEnumerable<NbtTag> tags) {
            if (tags == null) throw new ArgumentNullException(nameof(tags));
            name = tagName;
            var toAdd = new List<NbtTag>(tags);
            // Validate first, so a bad batch doesn't leave any tags pointing to a half-constructed parent.
            ValidateForAdd(toAdd, nameof(tags));
            EnsureCapacity(toAdd.Count);
            foreach (NbtTag tag in toAdd) {
                AppendVerified(tag);
                tag.Parent = this;
            }
        }


        /// <summary> Creates a deep copy of given NbtCompound. </summary>
        /// <param name="other"> An existing NbtCompound to copy. May not be <c>null</c>. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="other"/> is <c>null</c>. </exception>
        /// <exception cref="NbtFormatException"> <paramref name="other"/> is nested deeper than 512 levels. </exception>
        public NbtCompound(NbtCompound other)
            : this(other, MaxDepth) { }


        NbtCompound(NbtCompound other, int depthBudget) {
            if (other == null) throw new ArgumentNullException(nameof(other));
            int childDepthBudget = ConsumeDepthBudget(depthBudget);
            name = other.name;
            // Sized up front, and fresh clones can't be duplicate keys or cycles: no revalidation.
            EnsureCapacity(other.count);
            NbtTag[]? otherItems = other.items;
            for (int i = 0; i < other.count; i++) {
                NbtTag childClone = otherItems![i].Clone(childDepthBudget);
                AppendVerified(childClone);
                childClone.Parent = this;
            }
        }


        #region Storage

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
            items![count++] = tag;
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


        void BuildTable() {
            var t = new ulong[InitialTableSize];
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
            var t = new ulong[newSize];
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

        #endregion


        /// <summary> Gets or sets the tag with the specified name. May return <c>null</c>. </summary>
        /// <returns> The tag with the specified key. Null if tag with the given name was not found. </returns>
        /// <param name="tagName"> The name of the tag to get or set. Must match tag's actual name. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="tagName"/> is <c>null</c>; or if trying to assign null value. </exception>
        /// <exception cref="ArgumentException"> <paramref name="tagName"/> does not match the given tag's actual name;
        /// or given tag already has a Parent; or it is this compound or one of its ancestors. </exception>
        public override NbtTag? this[string tagName] {
            get { return Get<NbtTag>(tagName); }
            set {
                if (tagName == null) {
                    throw new ArgumentNullException(nameof(tagName));
                } else if (value == null) {
                    throw new ArgumentNullException(nameof(value));
                } else if (value.Name != tagName) {
                    throw new ArgumentException("Given tag name must match tag's actual name.");
                }
                ValidateCanAttach(value, nameof(value));
                int position = FindPosition(tagName);
                if (position < 0) {
                    AppendVerified(value);
                } else {
                    NbtTag displaced = items[position];
                    if (!ReferenceEquals(displaced, value)) {
                        // Same name and same position, so the table entry already fits the
                        // replacement
                        items[position] = value;
                        version++;
                        displaced.Parent = null;
                    }
                }
                value.Parent = this;
            }
        }


        /// <summary> Gets the tag with the specified name. May return <c>null</c>. </summary>
        /// <param name="tagName"> The name of the tag to get. </param>
        /// <typeparam name="T"> Type to cast the result to. Must derive from NbtTag. </typeparam>
        /// <returns> The tag with the specified key. Null if tag with the given name was not found. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="tagName"/> is <c>null</c>. </exception>
        /// <exception cref="InvalidCastException"> If tag could not be cast to the desired tag. </exception>
        public T? Get<T>(string tagName) where T : NbtTag {
            if (tagName == null) throw new ArgumentNullException(nameof(tagName));
            return (T?)Find(tagName);
        }


        /// <summary> Gets the tag with the specified name. May return <c>null</c>. </summary>
        /// <param name="tagName"> The name of the tag to get. </param>
        /// <returns> The tag with the specified key. Null if tag with the given name was not found. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="tagName"/> is <c>null</c>. </exception>
        public NbtTag? Get(string tagName) {
            if (tagName == null) throw new ArgumentNullException(nameof(tagName));
            return Find(tagName);
        }


        /// <summary> Gets the tag with the specified name. </summary>
        /// <param name="tagName"> The name of the tag to get. </param>
        /// <param name="result"> When this method returns, contains the tag associated with the specified name, if the tag is found;
        /// otherwise, null. This parameter is passed uninitialized. </param>
        /// <typeparam name="T"> Type to cast the result to. Must derive from NbtTag. </typeparam>
        /// <returns> true if the NbtCompound contains a tag with the specified name; otherwise, false. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="tagName"/> is <c>null</c>. </exception>
        /// <exception cref="InvalidCastException"> If tag could not be cast to the desired tag. </exception>
        public bool TryGet<T>(string tagName, out T? result) where T : NbtTag {
            if (tagName == null) throw new ArgumentNullException(nameof(tagName));
            NbtTag? found = Find(tagName);
            if (found != null) {
                result = (T)found;
                return true;
            }
            result = null;
            return false;
        }


        /// <summary> Gets the tag with the specified name. </summary>
        /// <param name="tagName"> The name of the tag to get. </param>
        /// <param name="result"> When this method returns, contains the tag associated with the specified name, if the tag is found;
        /// otherwise, null. This parameter is passed uninitialized. </param>
        /// <returns> true if the NbtCompound contains a tag with the specified name; otherwise, false. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="tagName"/> is <c>null</c>. </exception>
        public bool TryGet(string tagName, out NbtTag? result) {
            if (tagName == null) throw new ArgumentNullException(nameof(tagName));
            result = Find(tagName);
            return result != null;
        }


        /// <summary> Adds all tags from the specified collection to this NbtCompound. </summary>
        /// <param name="newTags"> The collection whose elements should be added to this NbtCompound. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="newTags"/> is <c>null</c>, or one of the tags in newTags is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> If one of the given tags was unnamed,
        /// or a tag with the same name already exists or appears twice in the batch,
        /// or a tag already has a Parent, or is this compound or one of its ancestors. </exception>
        public void AddRange(IEnumerable<NbtTag> newTags) {
            if (newTags == null) throw new ArgumentNullException(nameof(newTags));
            // Validate the whole batch first, so we don't partially add/reparent some tags on exception.
            var toAdd = new List<NbtTag>(newTags);
            ValidateForAdd(toAdd, nameof(newTags));
            EnsureCapacity(count + toAdd.Count);
            foreach (NbtTag tag in toAdd) {
                AppendVerified(tag);
                tag.Parent = this;
            }
        }


        // One checklist for every path that attaches a tag, so no mutation path can skip or
        // reorder the guards. The duplicate-name check stays with callers, fused into the
        // index probe.
        void ValidateCanAttach(NbtTag tag, string paramName) {
            if (tag == null) {
                throw new ArgumentNullException(paramName);
            } else if (tag == this) {
                throw new ArgumentException("Cannot add tag to itself");
            } else if (tag.Name == null) {
                throw new ArgumentException("Only named tags are allowed in compound tags.");
            } else if (tag.Parent != null) {
                throw new ArgumentException("A tag may only be added to one compound/list at a time.");
            } else if (IsDescendantOf(tag)) {
                throw new ArgumentException("A tag may not be added to one of its own descendants.");
            }
        }


        // Checks that every tag in the batch can be added, without changing any fields.
        void ValidateForAdd(List<NbtTag> toAdd, string paramName) {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (NbtTag tag in toAdd) {
                if (tag == null) {
                    throw new ArgumentNullException(paramName, "A tag in the collection is null.");
                }
                ValidateCanAttach(tag, paramName);
                if (Find(tag.Name!) != null || !seen.Add(tag.Name!)) {
                    throw new ArgumentException("A tag with the name '" + tag.Name + "' already exists.");
                }
            }
        }


        /// <summary> Determines whether this NbtCompound contains a tag with a specific name. </summary>
        /// <param name="tagName"> Tag name to search for. May not be <c>null</c>. </param>
        /// <returns> true if a tag with given name was found; otherwise, false. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="tagName"/> is <c>null</c>. </exception>
        public bool Contains(string tagName) {
            if (tagName == null) throw new ArgumentNullException(nameof(tagName));
            return Find(tagName) != null;
        }


        /// <summary> Removes the tag with the specified name from this NbtCompound. </summary>
        /// <remarks> Removing shifts every later child to preserve insertion order. When removing
        /// many tags from a large compound, rebuilding it without them is faster. </remarks>
        /// <param name="tagName"> The name of the tag to remove. </param>
        /// <returns> true if the tag is successfully found and removed; otherwise, false.
        /// This method returns false if name is not found in the NbtCompound. </returns>
        /// <exception cref="ArgumentNullException"> <paramref name="tagName"/> is <c>null</c>. </exception>
        public bool Remove(string tagName) {
            if (tagName == null) throw new ArgumentNullException(nameof(tagName));
            int position = FindPosition(tagName);
            if (position < 0) return false;
            RemoveAt(position, items[position]);
            return true;
        }


        internal void RenameTag(NbtTag tag, string oldName, string newName) {
            NullableSupport.Assert(oldName != null);
            NullableSupport.Assert(newName != null);
            NullableSupport.Assert(newName != oldName);
            if (Find(newName) != null) {
                throw new ArgumentException(
                    "Cannot rename tag '" + oldName + "' to '" + newName +
                    "': the compound already contains a tag with that name.");
            }
            // Verify this compound actually holds the tag under oldName.
            // A stale Parent link must not re-key whatever now lives at that key.
            NbtTag? existing = Find(oldName);
            if (existing == null || !ReferenceEquals(existing, tag)) {
                throw new InvalidOperationException(
                    "Cannot rename tag '" + oldName + "': it is missing from its parent compound. " +
                    "The Parent link is out of sync, most likely because the compound was modified " +
                    "from another thread.");
            }
            RekeyEntry(oldName, newName);
        }


        /// <summary> Gets a collection containing all tag names in this NbtCompound, in insertion order. </summary>
        public IEnumerable<string> Names {
            get {
                for (int i = 0; i < count; i++) {
                    yield return items![i].name!;
                }
            }
        }

        /// <summary> Gets a collection containing all tags in this NbtCompound, in insertion order. </summary>
        public IEnumerable<NbtTag> Tags {
            get {
                for (int i = 0; i < count; i++) {
                    yield return items![i];
                }
            }
        }


        #region Reading / Writing

        internal override bool ReadTag(NbtBinaryReader readStream, int depthBudget) {
            if (Parent != null && readStream.Selector != null && !readStream.Selector(this)) {
                readStream.SkipPayload(NbtTagType.Compound, depthBudget);
                return false;
            }

            int childDepthBudget = ConsumeDepthBudget(depthBudget);
            while (true) {
                NbtTagType nextTag = readStream.ReadTagType();
                NbtTag newTag;
                switch (nextTag) {
                    case NbtTagType.End:
                        return true;

                    case NbtTagType.Byte:
                        newTag = new NbtByte();
                        break;

                    case NbtTagType.Short:
                        newTag = new NbtShort();
                        break;

                    case NbtTagType.Int:
                        newTag = new NbtInt();
                        break;

                    case NbtTagType.Long:
                        newTag = new NbtLong();
                        break;

                    case NbtTagType.Float:
                        newTag = new NbtFloat();
                        break;

                    case NbtTagType.Double:
                        newTag = new NbtDouble();
                        break;

                    case NbtTagType.ByteArray:
                        newTag = new NbtByteArray();
                        break;

                    case NbtTagType.String:
                        newTag = new NbtString();
                        break;

                    case NbtTagType.List:
                        newTag = new NbtList();
                        break;

                    case NbtTagType.Compound:
                        newTag = new NbtCompound();
                        break;

                    case NbtTagType.IntArray:
                        newTag = new NbtIntArray();
                        break;

                    case NbtTagType.LongArray:
                        newTag = new NbtLongArray();
                        break;

                    default:
                        throw new NbtFormatException("Unsupported tag type found in NBT_Compound: " + nextTag);
                }
                newTag.Parent = this;
                // Assigned to the field: the tag has no name yet, so the property's rename path is dead weight.
                string tagName = readStream.ReadTagName();
                newTag.name = tagName;
                if (newTag.ReadTag(readStream, childDepthBudget)) {
                    if (!TryInsert(newTag)) {
                        throw new NbtFormatException("Duplicate tag name in compound: " + tagName);
                    }
                }
            }
        }


        internal override void WriteTag(NbtBinaryWriter writeStream, int depthBudget) {
            int childDepthBudget = ConsumeDepthBudget(depthBudget);
            if (Name == null) throw new NbtFormatException("Name is null");
            writeStream.Write(NbtTagType.Compound);
            writeStream.Write(Name);
            WritePayload(writeStream, childDepthBudget);
        }


        internal override void WriteData(NbtBinaryWriter writeStream, int depthBudget) {
            WritePayload(writeStream, ConsumeDepthBudget(depthBudget));
        }


        void WritePayload(NbtBinaryWriter writeStream, int childDepthBudget) {
            NbtTag[]? local = items;
            for (int i = 0; i < count; i++) {
                local![i].WriteTag(writeStream, childDepthBudget);
            }
            writeStream.Write(NbtTagType.End);
        }

        #endregion


        #region Implementation of IEnumerable<NbtTag>

        /// <summary> Returns an enumerator that iterates through all tags in this NbtCompound,
        /// in insertion order. </summary>
        /// <returns> An IEnumerator&gt;NbtTag&lt; that can be used to iterate through the collection. </returns>
        public IEnumerator<NbtTag> GetEnumerator() {
            return Enumerate();
        }


        IEnumerator IEnumerable.GetEnumerator() {
            return Enumerate();
        }


        IEnumerator<NbtTag> Enumerate() {
            int startVersion = version;
            for (int i = 0; i < count; i++) {
                if (version != startVersion) {
                    throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");
                }
                yield return items![i];
            }
        }

        #endregion


        #region Implementation of ICollection<NbtTag>

        /// <summary> Adds a tag to this NbtCompound. </summary>
        /// <param name="newTag"> The object to add to this NbtCompound. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="newTag"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> If the given tag is unnamed;
        /// or if a tag with the given name already exists in this NbtCompound;
        /// or it already has a Parent; or it is this compound or one of its ancestors. </exception>
        public void Add(NbtTag newTag) {
            ValidateCanAttach(newTag, nameof(newTag));
            if (!TryInsert(newTag)) {
                throw new ArgumentException("A tag with the name '" + newTag.Name + "' already exists.");
            }
            newTag.Parent = this;
        }


        /// <summary> Removes all tags from this NbtCompound. </summary>
        public void Clear() {
            NbtTag[] local = items;
            for (int i = 0; i < count; i++) {
                local[i].Parent = null;
                local[i] = null!;
            }
            count = 0;
            table = null;
            tombstones = 0;
            version++;
        }


        /// <summary> Determines whether this NbtCompound contains a specific NbtTag.
        /// Looks for exact object matches, not name matches. </summary>
        /// <returns> true if tag is found; otherwise, false. </returns>
        /// <param name="tag"> The object to locate in this NbtCompound. May not be <c>null</c>. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="tag"/> is <c>null</c>. </exception>
        public bool Contains(NbtTag tag) {
            if (tag == null) throw new ArgumentNullException(nameof(tag));
            return PositionOfExact(tag) >= 0;
        }


        /// <summary> Copies the tags of the NbtCompound to an array, starting at a particular array index. </summary>
        /// <param name="array"> The one-dimensional array that is the destination of the tag copied from NbtCompound.
        /// The array must have zero-based indexing. </param>
        /// <param name="arrayIndex"> The zero-based index in array at which copying begins. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="array"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> arrayIndex is less than 0. </exception>
        /// <exception cref="ArgumentException"> Given array is multidimensional; arrayIndex is equal to or greater than the length of array;
        /// the number of tags in this NbtCompound is greater than the available space from arrayIndex to the end of the destination array;
        /// or type NbtTag cannot be cast automatically to the type of the destination array. </exception>
        public void CopyTo(NbtTag[] array, int arrayIndex) {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (arrayIndex < 0) throw new ArgumentOutOfRangeException(nameof(arrayIndex));
            if (array.Length - arrayIndex < count) {
                throw new ArgumentException("Not enough space in the destination array.");
            }
            Array.Copy(items, 0, array, arrayIndex, count);
        }


        /// <summary> Removes the first occurrence of a specific NbtTag from the NbtCompound.
        /// Looks for exact object matches, not name matches. </summary>
        /// <remarks> Removing shifts every later child to preserve insertion order. When removing
        /// many tags from a large compound, rebuilding it without them is faster. </remarks>
        /// <returns> true if tag was successfully removed from the NbtCompound; otherwise, false.
        /// This method also returns false if tag is not found. </returns>
        /// <param name="tag"> The tag to remove from the NbtCompound. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="tag"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> If the given tag is unnamed </exception>
        public bool Remove(NbtTag tag) {
            if (tag == null) throw new ArgumentNullException(nameof(tag));
            if (tag.Name == null) throw new ArgumentException("Trying to remove an unnamed tag.");
            int position = PositionOfExact(tag);
            if (position < 0) return false;
            RemoveAt(position, tag);
            return true;
        }


        /// <summary> Gets the number of tags contained in the NbtCompound. </summary>
        /// <returns> The number of tags contained in the NbtCompound. </returns>
        public int Count {
            get { return count; }
        }

        bool ICollection<NbtTag>.IsReadOnly {
            get { return false; }
        }

        #endregion


        #region Implementation of ICollection

        void ICollection.CopyTo(Array array, int index) {
            CopyTo((NbtTag[])array, index);
        }


        object ICollection.SyncRoot {
            get {
                if (syncRoot == null) {
                    System.Threading.Interlocked.CompareExchange(ref syncRoot, new object(), null);
                }
                return syncRoot;
            }
        }

        object? syncRoot;

        bool ICollection.IsSynchronized {
            get { return false; }
        }

        #endregion


        /// <inheritdoc />
        /// <exception cref="NbtFormatException"> This tag is nested deeper than 512 levels. </exception>
        public override object Clone() {
            return new NbtCompound(this, MaxDepth);
        }


        internal override NbtTag Clone(int depthBudget) {
            return new NbtCompound(this, depthBudget);
        }

        internal override void PrettyPrint(StringBuilder sb, string indentString, int indentLevel, int depthBudget) {
            int childDepthBudget = ConsumeDepthBudget(depthBudget);
            PrettyPrintHeader(sb, indentString, indentLevel);
            sb.AppendFormat(CultureInfo.InvariantCulture, ": {0} entries {{", count);

            if (count > 0) {
                sb.Append('\n');
                for (int i = 0; i < count; i++) {
                    items![i].PrettyPrint(sb, indentString, indentLevel + 1, childDepthBudget);
                    sb.Append('\n');
                }
                for (int i = 0; i < indentLevel; i++) {
                    sb.Append(indentString);
                }
            }
            sb.Append('}');
        }
    }
}



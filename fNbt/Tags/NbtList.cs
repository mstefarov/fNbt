using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace fNbt {
    /// <summary> A tag containing a list of unnamed tags, all of the same kind. </summary>
    public sealed class NbtList : NbtTag, IList<NbtTag>, IList {
        /// <summary> Type of this tag (List). </summary>
        public override NbtTagType TagType {
            get { return NbtTagType.List; }
        }

        internal readonly List<NbtTag> tags = new List<NbtTag>();

        // Real lists are small, and a 5-byte list header should not be able to force a large allocation out
        // of a corrupt length. Longer lists grow as they go, unless the input can vouch for the count.
        const int MaxPresizedCapacity = 16;

        /// <summary> Gets or sets the tag type of this list. All tags in this NbtTag must be of the same type.
        /// An empty list may have the type <c>Unknown</c> or <c>End</c>; either way, the first tag added to
        /// it sets the type. </summary>
        /// <exception cref="ArgumentException"> If the given NbtTagType does not match the type of existing list items (for non-empty lists). </exception>
        /// <exception cref="ArgumentOutOfRangeException"> If the given NbtTagType is not a recognized tag type. </exception>
        public NbtTagType ListType {
            get { return listType; }
            set {
                if (value == NbtTagType.End) {
                    // Empty lists may have type "End", see: https://github.com/fragmer/fNbt/issues/12
                    if (tags.Count > 0) {
                        throw new ArgumentException("Only empty list tags may have TagType of End.");
                    }
                } else if (value < NbtTagType.Byte || (value > NbtTagType.LongArray && value != NbtTagType.Unknown)) {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                if (tags.Count > 0) {
                    NbtTagType actualType = tags[0].TagType;
                    // We can safely assume that ALL tags have the same TagType as the first tag.
                    if (actualType != value) {
                        string msg = String.Format(CultureInfo.InvariantCulture,
                                                   "Given NbtTagType ({0}) does not match actual element type ({1})",
                                                   value, actualType);
                        throw new ArgumentException(msg);
                    }
                }
                listType = value;
            }
        }

        NbtTagType listType;


        /// <summary> Creates an unnamed NbtList with empty contents and undefined ListType. </summary>
        public NbtList()
            : this(null, null, NbtTagType.Unknown) { }


        /// <summary> Creates an NbtList with given name, empty contents, and undefined ListType. </summary>
        /// <param name="tagName"> Name to assign to this tag. May be <c>null</c>. </param>
        public NbtList(string? tagName)
            : this(tagName, null, NbtTagType.Unknown) { }


        /// <summary> Creates an unnamed NbtList with the given contents, and inferred ListType. 
        /// If given tag array is empty, NbtTagType remains Unknown. </summary>
        /// <param name="tags"> Collection of tags to insert into the list. All tags are expected to be of the same type.
        /// ListType is inferred from the first tag. List may be empty, but may not be <c>null</c>. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="tags"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> If given tags are of mixed types. </exception>
        public NbtList(IEnumerable<NbtTag> tags)
            : this(null, tags, NbtTagType.Unknown) {
            // the base constructor will allow null "tags," but we don't want that in this constructor
            if (tags == null) throw new ArgumentNullException(nameof(tags));
        }


        /// <summary> Creates an unnamed NbtList with empty contents and an explicitly specified ListType.
        /// If ListType is Unknown, it will be inferred from the type of the first added tag.
        /// Otherwise, all tags added to this list are expected to be of the given type. </summary>
        /// <param name="givenListType"> Type of the list elements. May be Unknown. </param>
        /// <exception cref="ArgumentOutOfRangeException"> <paramref name="givenListType"/> is not a recognized tag type. </exception>
        public NbtList(NbtTagType givenListType)
            : this(null, null, givenListType) { }


        /// <summary> Creates an NbtList with the given name and contents, and inferred ListType. 
        /// If given tag array is empty, NbtTagType remains Unknown. </summary>
        /// <param name="tagName"> Name to assign to this tag. May be <c>null</c>. </param>
        /// <param name="tags"> Collection of tags to insert into the list. All tags are expected to be of the same type.
        /// ListType is inferred from the first tag. List may be empty, but may not be <c>null</c>. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="tags"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> If given tags are of mixed types. </exception>
        public NbtList(string? tagName, IEnumerable<NbtTag> tags)
            : this(tagName, tags, NbtTagType.Unknown) {
            // the base constructor will allow null "tags," but we don't want that in this constructor
            if (tags == null) throw new ArgumentNullException(nameof(tags));
        }


        /// <summary> Creates an unnamed NbtList with the given contents, and an explicitly specified ListType. </summary>
        /// <param name="tags"> Collection of tags to insert into the list.
        /// All tags are expected to be of the same type (matching givenListType).
        /// List may be empty, but may not be <c>null</c>. </param>
        /// <param name="givenListType"> Type of the list elements. May be Unknown (to infer type from the first element of tags). </param>
        /// <exception cref="ArgumentNullException"> <paramref name="tags"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> <paramref name="givenListType"/> is not a valid tag type. </exception>
        /// <exception cref="ArgumentException"> If given tags do not match <paramref name="givenListType"/> or are of mixed types;
        /// or a tag is named, already has a Parent, or appears more than once. </exception>
        public NbtList(IEnumerable<NbtTag> tags, NbtTagType givenListType)
            : this(null, tags, givenListType) {
            // the base constructor will allow null "tags," but we don't want that in this constructor
            if (tags == null) throw new ArgumentNullException(nameof(tags));
        }


        /// <summary> Creates an NbtList with the given name, empty contents, and an explicitly specified ListType. </summary>
        /// <param name="tagName"> Name to assign to this tag. May be <c>null</c>. </param>
        /// <param name="givenListType"> Type of the list elements.
        /// If givenListType is Unknown, ListType will be inferred from the first tag added to this NbtList. </param>
        /// <exception cref="ArgumentOutOfRangeException"> <paramref name="givenListType"/> is not a valid tag type. </exception>
        public NbtList(string? tagName, NbtTagType givenListType)
            : this(tagName, null, givenListType) { }


        /// <summary> Creates an NbtList with the given name and contents, and an explicitly specified ListType. </summary>
        /// <param name="tagName"> Name to assign to this tag. May be <c>null</c>. </param>
        /// <param name="tags"> Collection of tags to insert into the list.
        /// All tags are expected to be of the same type (matching givenListType). May be empty or <c>null</c>. </param>
        /// <param name="givenListType"> Type of the list elements. May be Unknown (to infer type from the first element of tags). </param>
        /// <exception cref="ArgumentOutOfRangeException"> <paramref name="givenListType"/> is not a valid tag type. </exception>
        /// <exception cref="ArgumentException"> If given tags do not match <paramref name="givenListType"/> or are of mixed types;
        /// or a tag is named, already has a Parent, or appears more than once. </exception>
        public NbtList(string? tagName, IEnumerable<NbtTag>? tags, NbtTagType givenListType) {
            name = tagName;
            ListType = givenListType;

            if (tags == null) return;
            // Validate first, so a bad batch doesn't leave any tags pointing to a half-constructed parent.
            List<NbtTag> toAdd = new List<NbtTag>(tags);
            this.tags.Capacity = toAdd.Count;
            NbtTagType effectiveType = ValidateForAdd(toAdd, nameof(tags));
            foreach (NbtTag tag in toAdd) {
                this.tags.Add(tag);
                tag.Parent = this;
            }
            listType = effectiveType;
        }


        /// <summary> Creates a deep copy of given NbtList. </summary>
        /// <param name="other"> An existing NbtList to copy. May not be <c>null</c>. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="other"/> is <c>null</c>. </exception>
        /// <exception cref="NbtFormatException"> <paramref name="other"/> is nested deeper than 512 levels. </exception>
        public NbtList(NbtList other)
            : this(other, MaxDepth) { }


        NbtList(NbtList other, int depthBudget) {
            if (other == null) throw new ArgumentNullException(nameof(other));
            int childDepthBudget = ConsumeDepthBudget(depthBudget);
            name = other.name;
            listType = other.listType;
            tags.Capacity = other.tags.Count;
            foreach (NbtTag tag in other.tags) {
                NbtTag childClone = tag.Clone(childDepthBudget);
                tags.Add(childClone);
                childClone.Parent = this;
            }
        }


        /// <summary> Gets or sets the tag at the specified index. </summary>
        /// <returns> The tag at the specified index. </returns>
        /// <param name="tagIndex"> The zero-based index of the tag to get or set. </param>
        /// <exception cref="ArgumentOutOfRangeException"> <paramref name="tagIndex"/> is not a valid index in the NbtList. </exception>
        /// <exception cref="ArgumentNullException"> <paramref name="value"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> Given tag's type does not match the ListType of a non-empty list;
        /// or it already has a Parent; or it is this list or one of its ancestors; or it is named. </exception>
        public override NbtTag this[int tagIndex] {
            get { return tags[tagIndex]; }
            set {
                ValidateCanAttach(value, listType, nameof(value));
                // Clear the displaced tag's Parent so it doesn't keep pointing at this list
                NbtTag displaced = tags[tagIndex];
                if (!ReferenceEquals(displaced, value)) {
                    displaced.Parent = null;
                }
                tags[tagIndex] = value;
                value.Parent = this;
            }
        }


        /// <summary> Gets the tag at the specified index, cast to the requested type. </summary>
        /// <param name="tagIndex"> The zero-based index of the tag to get. </param>
        /// <typeparam name="T"> Type to cast the result to. Must derive from NbtTag. </typeparam>
        /// <returns> The tag at the specified index. </returns>
        /// <exception cref="ArgumentOutOfRangeException"> <paramref name="tagIndex"/> is not a valid index in the NbtList. </exception>
        /// <exception cref="InvalidCastException"> If tag could not be cast to the desired tag. </exception>
        public T Get<T>(int tagIndex) where T : NbtTag {
            return (T)tags[tagIndex];
        }


        /// <summary> Adds all tags from the specified collection to the end of this NbtList. </summary>
        /// <param name="newTags"> The collection whose elements should be added to this NbtList. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="newTags"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> If given tags do not match the ListType of a non-empty list or are of mixed types;
        /// or a tag is named, already has a Parent, is this list or one of its ancestors,
        /// or appears more than once. </exception>
        public void AddRange(IEnumerable<NbtTag> newTags) {
            if (newTags == null) throw new ArgumentNullException(nameof(newTags));
            // Validate the whole batch first, so we don't partially add/reparent some tags,
            // or pin ListType on exception.
            List<NbtTag> toAdd = new List<NbtTag>(newTags);
            NbtTagType effectiveType = ValidateForAdd(toAdd, nameof(newTags));
            foreach (NbtTag tag in toAdd) {
                tags.Add(tag);
                tag.Parent = this;
            }
            // An empty batch leaves a tolerated End type in place
            if (toAdd.Count > 0) {
                listType = effectiveType;
            }
        }


        // The type the next added tag is checked against. An empty list has not committed to a
        // type: End is only the wire's spelling of "no elements", so it counts as Unknown here and
        // the first tag added decides.
        NbtTagType TypeForAdd {
            get { return tags.Count == 0 && listType == NbtTagType.End ? NbtTagType.Unknown : listType; }
        }


        // One checklist for every path that attaches a tag, so no mutation path can skip or
        // reorder the guards. Takes the type the list would have, so batch validation threads
        // its running type through the same checks.
        void ValidateCanAttach(NbtTag tag, NbtTagType effectiveType, string paramName) {
            if (tag == null) {
                throw new ArgumentNullException(paramName);
            } else if (tag.Parent != null) {
                throw new ArgumentException("A tag may only be added to one compound/list at a time.");
            } else if (IsDescendantOf(tag)) {
                throw new ArgumentException(tag == this || tag == Parent
                    ? "A list tag may not be added to itself or to its child tag."
                    : "A tag may not be added to one of its own descendants.");
            } else if (tag.Name != null) {
                throw new ArgumentException("Named tag given. A list may only contain unnamed tags.");
            } else if (effectiveType != NbtTagType.Unknown && tag.TagType != effectiveType) {
                throw new ArgumentException("Items in this list must be of type " + effectiveType +
                                            ". Given type: " + tag.TagType);
            }
        }


        // Checks that every tag in the batch can be added, without changing any fields.
        // Returns the ListType the list would have after a successful add.
        NbtTagType ValidateForAdd(List<NbtTag> toAdd, string paramName) {
            NbtTagType effectiveType = TypeForAdd;
            HashSet<NbtTag> seen = new HashSet<NbtTag>();
            foreach (NbtTag tag in toAdd) {
                if (tag == null) {
                    throw new ArgumentNullException(paramName, "A tag in the collection is null.");
                } else if (!seen.Add(tag)) {
                    throw new ArgumentException("The same tag instance was given more than once.", paramName);
                }
                ValidateCanAttach(tag, effectiveType, paramName);
                effectiveType = tag.TagType;
            }
            return effectiveType;
        }


        /// <summary> Copies all tags in this NbtList to an array. </summary>
        /// <returns> Array of NbtTags. </returns>
        public NbtTag[] ToArray() {
            return tags.ToArray();
        }


        /// <summary> Copies all tags in this NbtList to an array, and casts it to the desired type. </summary>
        /// <typeparam name="T"> Type to cast every member of NbtList to. Must derive from NbtTag. </typeparam>
        /// <returns> Array of NbtTags cast to the desired type. </returns>
        /// <exception cref="InvalidCastException"> If contents of this list cannot be cast to the given type. </exception>
        public T[] ToArray<T>() where T : NbtTag {
            T[] result = new T[tags.Count];
            for (int i = 0; i < result.Length; i++) {
                result[i] = (T)tags[i];
            }
            return result;
        }


        #region Reading / Writing

        // Past the cap, a complete seekable input can vouch for the count. The reference array may
        // take up front only as many bytes as the stream still holds, so a corrupt count costs at
        // most the input's own size. A selector may drop elements, so it keeps the bounded growth.
        static int ReadCapacity(NbtBinaryReader readStream, int length) {
            if (length <= MaxPresizedCapacity) return length;
            if (readStream.Selector == null && readStream.TryGetRemaining(out long remaining)) {
                return (int)Math.Min(length, remaining / IntPtr.Size);
            }
            return MaxPresizedCapacity;
        }


        internal override bool ReadTag(NbtBinaryReader readStream, int depthBudget) {
            if (readStream.Selector != null && !readStream.Selector(this)) {
                readStream.SkipPayload(NbtTagType.List, depthBudget);
                return false;
            }

            int childDepthBudget = ConsumeDepthBudget(depthBudget);
            NbtTagType newListType = readStream.ReadListHeader(out int length);
            if (length == 0) {
                // Still counts as a nesting level, like a non-empty list would. The field
                // assignment keeps a tolerated End type without the property's checks.
                listType = newListType;
                return true;
            }
            ListType = newListType;

            // The reference array grows to hold every element, so it counts against the cap the
            // way an array payload does. The element objects themselves are not counted.
            readStream.EnsureAllocation((long)length * IntPtr.Size);
            tags.Capacity = ReadCapacity(readStream, length);

            for (int i = 0; i < length; i++) {
                NbtTag newTag = NbtTag.Create(newListType);
                newTag.Parent = this;
                if (newTag.ReadTag(readStream, childDepthBudget)) {
                    tags.Add(newTag);
                }
            }
            return true;
        }


        internal override void WriteTag(NbtBinaryWriter writeStream, int depthBudget) {
            int childDepthBudget = ConsumeDepthBudget(depthBudget);
            EnsureListType();
            writeStream.WriteTagHeader(NbtTagType.List, Name);
            WritePayload(writeStream, childDepthBudget);
        }


        internal override void WriteData(NbtBinaryWriter writeStream, int depthBudget) {
            int childDepthBudget = ConsumeDepthBudget(depthBudget);
            EnsureListType();
            WritePayload(writeStream, childDepthBudget);
        }


        void EnsureListType() {
            if (ListType == NbtTagType.Unknown) {
                throw NbtFormatException.UnknownListType();
            }
        }


        void WritePayload(NbtBinaryWriter writeStream, int childDepthBudget) {
            writeStream.Write(ListType);
            writeStream.Write(tags.Count);
            foreach (NbtTag tag in tags) {
                tag.WriteData(writeStream, childDepthBudget);
            }
        }

        #endregion


        #region Implementation of IEnumerable<NBtTag> and IEnumerable

        /// <summary> Returns an enumerator that iterates through all tags in this NbtList. </summary>
        /// <returns> An IEnumerator&lt;NbtTag&gt; that can be used to iterate through the list. </returns>
        public IEnumerator<NbtTag> GetEnumerator() {
            return tags.GetEnumerator();
        }


        IEnumerator IEnumerable.GetEnumerator() {
            return tags.GetEnumerator();
        }

        #endregion


        #region Implementation of IList<NbtTag> and ICollection<NbtTag>

        /// <summary> Determines the index of a specific tag in this NbtList </summary>
        /// <returns> The index of tag if found in the list; otherwise, -1. </returns>
        /// <param name="tag"> The tag to locate in this NbtList. </param>
        public int IndexOf(NbtTag? tag) {
            if (tag == null) return -1;
            return tags.IndexOf(tag);
        }


        /// <summary> Inserts an item to this NbtList at the specified index. </summary>
        /// <param name="tagIndex"> The zero-based index at which newTag should be inserted. </param>
        /// <param name="newTag"> The tag to insert into this NbtList. </param>
        /// <exception cref="ArgumentOutOfRangeException"> <paramref name="tagIndex"/> is not a valid index in this NbtList. </exception>
        /// <exception cref="ArgumentNullException"> <paramref name="newTag"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> <paramref name="newTag"/> does not match the ListType of a non-empty list;
        /// or it already has a Parent; or it is this list or one of its ancestors; or it is named. </exception>
        public void Insert(int tagIndex, NbtTag newTag) {
            NbtTagType effectiveType = TypeForAdd;
            ValidateCanAttach(newTag, effectiveType, nameof(newTag));
            tags.Insert(tagIndex, newTag);
            newTag.Parent = this;
            if (effectiveType == NbtTagType.Unknown) {
                listType = newTag.TagType;
            }
        }


        /// <summary> Removes a tag at the specified index from this NbtList. </summary>
        /// <param name="index"> The zero-based index of the item to remove. </param>
        /// <exception cref="ArgumentOutOfRangeException"> <paramref name="index"/> is not a valid index in the NbtList. </exception>
        public void RemoveAt(int index) {
            NbtTag tag = this[index];
            tags.RemoveAt(index);
            tag.Parent = null;
        }


        /// <summary> Adds a tag to this NbtList. </summary>
        /// <param name="newTag"> The tag to add to this NbtList. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="newTag"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> If <paramref name="newTag"/> does not match the ListType of a non-empty list;
        /// or it already has a Parent; or it is this list or one of its ancestors; or it is named. </exception>
        public void Add(NbtTag newTag) {
            NbtTagType effectiveType = TypeForAdd;
            ValidateCanAttach(newTag, effectiveType, nameof(newTag));
            tags.Add(newTag);
            newTag.Parent = this;
            if (effectiveType == NbtTagType.Unknown) {
                listType = newTag.TagType;
            }
        }


        /// <summary> Removes all tags from this NbtList. </summary>
        public void Clear() {
            for (int i = 0; i < tags.Count; i++) {
                tags[i].Parent = null;
            }
            tags.Clear();
        }


        /// <summary> Determines whether this NbtList contains a specific tag. </summary>
        /// <returns> true if given tag is found in this NbtList; otherwise, false. </returns>
        /// <param name="item"> The tag to locate in this NbtList. </param>
        public bool Contains(NbtTag item) {
            return tags.Contains(item);
        }


        /// <summary> Copies the tags of this NbtList to an array, starting at a particular array index. </summary>
        /// <param name="array"> The one-dimensional array that is the destination of the tag copied from NbtList.
        /// The array must have zero-based indexing. </param>
        /// <param name="arrayIndex"> The zero-based index in array at which copying begins. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="array"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> arrayIndex is less than 0. </exception>
        /// <exception cref="ArgumentException"> Given array is multidimensional; arrayIndex is equal to or greater than the length of array;
        /// the number of tags in this NbtList is greater than the available space from arrayIndex to the end of the destination array;
        /// or type NbtTag cannot be cast automatically to the type of the destination array. </exception>
        public void CopyTo(NbtTag[] array, int arrayIndex) {
            tags.CopyTo(array, arrayIndex);
        }


        /// <summary> Removes the first occurrence of a specific NbtTag from this NbtList.
        /// Looks for exact object matches, not name matches. </summary>
        /// <returns> true if tag was successfully removed from this NbtList; otherwise, false.
        /// This method also returns false if tag is not found. </returns>
        /// <param name="tag"> The tag to remove from this NbtList. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="tag"/> is <c>null</c>. </exception>
        public bool Remove(NbtTag tag) {
            if (tag == null) throw new ArgumentNullException(nameof(tag));
            if (!tags.Remove(tag)) {
                return false;
            }
            tag.Parent = null;
            return true;
        }


        /// <summary> Gets the number of tags contained in the NbtList. </summary>
        /// <returns> The number of tags contained in the NbtList. </returns>
        public int Count {
            get { return tags.Count; }
        }

        bool ICollection<NbtTag>.IsReadOnly {
            get { return false; }
        }

        #endregion


        #region Implementation of IList and ICollection

        void IList.Remove(object? value) {
            Remove((NbtTag)value!);
        }


        object? IList.this[int tagIndex] {
            get { return tags[tagIndex]; }
            set { this[tagIndex] = (NbtTag)value!; }
        }


        int IList.Add(object? value) {
            Add((NbtTag)value!);
            return (tags.Count - 1);
        }


        bool IList.Contains(object? value) {
            return tags.Contains((NbtTag)value!);
        }


        int IList.IndexOf(object? value) {
            return tags.IndexOf((NbtTag)value!);
        }


        void IList.Insert(int index, object? value) {
            Insert(index, (NbtTag)value!);
        }


        bool IList.IsFixedSize {
            get { return false; }
        }


        void ICollection.CopyTo(Array array, int index) {
            CopyTo((NbtTag[])array, index);
        }


        object ICollection.SyncRoot {
            get { return (tags as ICollection).SyncRoot; }
        }

        bool ICollection.IsSynchronized {
            get { return false; }
        }

        bool IList.IsReadOnly {
            get { return false; }
        }

        #endregion


        internal override NbtTag Clone(int depthBudget) {
            return new NbtList(this, depthBudget);
        }

        internal override void PrettyPrint(StringBuilder sb, string indentString, int indentLevel, int depthBudget) {
            PrettyPrintContainer(sb, indentString, indentLevel, depthBudget, this);
        }
    }
}

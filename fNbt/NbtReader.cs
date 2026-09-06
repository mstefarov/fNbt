using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace fNbt {
    /// <summary> Represents a reader that provides fast, non-cached, forward-only access to NBT data.
    /// Each instance of NbtReader reads one complete file. </summary>
    public class NbtReader {
        enum ParseState {
            AtStreamBeginning,
            AtCompoundBeginning,
            InCompound,
            AtCompoundEnd,
            AtListBeginning,
            InList,
            AtStreamEnd,
            Error
        }

        // Traversal state for one entered compound or list, kept in a grow-only array so that
        // entering a container allocates nothing once the array exists
        struct Node {
            public string? ParentName;
            public NbtTagType ParentTagType;
            public NbtTagType ListType;
            public int ParentTagLength;
            public int ListIndex;
        }

        ParseState state = ParseState.AtStreamBeginning;
        readonly NbtBinaryReader reader;
        Node[]? nodes;
        int nodeCount;
        readonly long streamStartOffset;
        bool atValue;
        object? valueCache;
        readonly bool canSeekStream;


        /// <summary> Initializes a new instance of the NbtReader class with the current defaults
        /// (<see cref="NbtOptions.DefaultFlavor"/> and the other <c>NbtOptions</c> defaults). </summary>
        /// <param name="stream"> Stream to read from. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="stream"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> <paramref name="stream"/> is not readable. </exception>
        public NbtReader(Stream stream)
            : this(stream, NbtOptions.ResolveDefaults()) { }


        /// <summary> Initializes a new instance of the NbtReader class. </summary>
        /// <param name="stream"> Stream to read from. </param>
        /// <param name="bigEndian"> Whether NBT data is in Big-Endian encoding. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="stream"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> <paramref name="stream"/> is not readable. </exception>
        [Obsolete("Use NbtReader(Stream, NbtFlavor) instead. true corresponds to NbtFlavor.Java, false to NbtFlavor.Bedrock.")]
        public NbtReader(Stream stream, bool bigEndian)
            : this(stream, bigEndian ? NbtFlavor.Java : NbtFlavor.Bedrock) { }


        /// <summary> Initializes a new instance of the NbtReader class for the given flavor,
        /// with the current default policy settings. </summary>
        /// <param name="stream"> Stream to read from. </param>
        /// <param name="flavor"> Encoding to read with. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="stream"/> or <paramref name="flavor"/> is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> <paramref name="stream"/> is not readable;
        /// or the flavor has no root name (use <see cref="NbtCodec"/> for those). </exception>
        public NbtReader(Stream stream, NbtFlavor flavor)
            : this(stream, NbtOptions.ResolveForFile(flavor, nameof(flavor))) { }


        /// <summary> Initializes a new instance of the NbtReader class with the given options.
        /// When read validation is on, the flavor's tag-type range and string ceiling are enforced;
        /// <c>MaxAllocation</c> caps declared-length allocations either way. </summary>
        /// <param name="stream"> Stream to read from. </param>
        /// <param name="options"> Settings to use, resolved here. May not be <c>null</c>. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="stream"/>, <paramref name="options"/>,
        /// or the options' <c>Flavor</c> is <c>null</c>. </exception>
        /// <exception cref="ArgumentException"> <paramref name="stream"/> is not readable;
        /// or the options' flavor has no root name (use <see cref="NbtCodec"/> for those). </exception>
        /// <exception cref="ArgumentOutOfRangeException"> <c>MaxAllocation</c> is zero or negative. </exception>
        public NbtReader(Stream stream, NbtOptions options)
            : this(stream, NbtOptions.ResolveForFile(options, nameof(options))) { }


        NbtReader(Stream stream, NbtOptions.Resolved resolved) {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            NbtFlavor flavor = resolved.Flavor;
            Flavor = flavor;
            SkipEndTags = true;
            ParentTagType = NbtTagType.Unknown;
            TagType = NbtTagType.Unknown;

            canSeekStream = stream.CanSeek;
            if (canSeekStream) {
                streamStartOffset = stream.Position;
            }

            reader = new NbtBinaryReader(stream, flavor, resolved.MaxAllocation, resolved.ValidateOnRead);
        }


        /// <summary> The flavor this reader decodes with, fixed at construction. </summary>
        public NbtFlavor Flavor { get; }

        /// <summary> Gets the name of the root tag of this NBT stream. </summary>
        public string? RootName { get; private set; }

        /// <summary> Gets the name of the parent tag. May be null (for root tags and descendants of list elements). </summary>
        public string? ParentName { get; private set; }

        /// <summary> Gets the name of the current tag. May be null (for list elements and end tags). </summary>
        public string? TagName { get; private set; }

        /// <summary> Gets the type of the parent tag. Returns TagType.Unknown if there is no parent tag. </summary>
        public NbtTagType ParentTagType { get; private set; }

        /// <summary> Gets the type of the current tag. </summary>
        public NbtTagType TagType { get; private set; }

        /// <summary> Whether tag that we are currently on is a list element. </summary>
        public bool IsListElement {
            get { return (ParentTagType == NbtTagType.List); }
        }

        /// <summary> Whether current tag has a value to read. </summary>
        public bool HasValue {
            get {
                switch (TagType) {
                    case NbtTagType.Compound:
                    case NbtTagType.End:
                    case NbtTagType.List:
                    case NbtTagType.Unknown:
                        return false;
                    default:
                        return true;
                }
            }
        }

        /// <summary> Whether current tag has a name. </summary>
        public bool HasName {
            get { return (TagName != null); }
        }

        /// <summary> Whether this reader has reached the end of stream. </summary>
        public bool IsAtStreamEnd {
            get { return state == ParseState.AtStreamEnd; }
        }

        /// <summary> Whether the current tag is a Compound. </summary>
        public bool IsCompound {
            get { return (TagType == NbtTagType.Compound); }
        }

        /// <summary> Whether the current tag is a List. </summary>
        public bool IsList {
            get { return (TagType == NbtTagType.List); }
        }

        /// <summary> Whether the current tag has length (Lists, ByteArrays, IntArrays, and LongArrays have length).
        /// Compound tags also have length, technically, but it is not known until all child tags are read. </summary>
        public bool HasLength {
            get {
                switch (TagType) {
                    case NbtTagType.List:
                    case NbtTagType.ByteArray:
                    case NbtTagType.IntArray:
                    case NbtTagType.LongArray:
                        return true;
                    default:
                        return false;
                }
            }
        }

        /// <summary> Gets the Stream from which data is being read. </summary>
        public Stream BaseStream {
            get { return reader.BaseStream; }
        }

        /// <summary> Gets the offset of the current tag's first byte from the stream position at which
        /// this NbtReader was created, which is the start of the document. Always 0 if the stream is
        /// not seekable. </summary>
        /// <exception cref="OverflowException"> The offset does not fit in an <c>int</c>;
        /// use <see cref="LongTagStartOffset"/> for documents past 2 GiB. </exception>
        public int TagStartOffset {
            get { return checked((int)LongTagStartOffset); }
        }

        /// <summary> Gets the offset of the current tag's first byte from the stream position at which
        /// this NbtReader was created, which is the start of the document, as a <c>long</c>.
        /// Always 0 if the stream is not seekable. </summary>
        public long LongTagStartOffset { get; private set; }

        /// <summary> Gets the number of tags read from the stream so far
        /// (including the current tag and all skipped tags). 
        /// If <c>SkipEndTags</c> is <c>false</c>, all end tags are also counted. </summary>
        public int TagsRead { get; private set; }

        /// <summary> Gets the depth of the current tag in the hierarchy.
        /// <c>RootTag</c> is at depth 1, its descendant tags are 2, etc. </summary>
        public int Depth { get; private set; }

        /// <summary> If the current tag is TAG_List, returns type of the list elements. </summary>
        public NbtTagType ListType { get; private set; }

        /// <summary> If the current tag is TAG_List, TAG_Byte_Array, TAG_Int_Array, or TAG_Long_Array, returns the number of elements. </summary>
        public int TagLength { get; private set; }

        /// <summary> If the parent tag is TAG_List, returns the number of elements. </summary>
        public int ParentTagLength { get; private set; }

        /// <summary> If the parent tag is TAG_List, returns index of the current tag. </summary>
        public int ListIndex { get; private set; }

        /// <summary> Gets whether this NbtReader instance is in state of error.
        /// No further reading can be done from this instance if a parse error occurred. </summary>
        public bool IsInErrorState {
            get { return (state == ParseState.Error); }
        }


        /// <summary> Reads the next tag from the stream. </summary>
        /// <returns> true if the next tag was read successfully; false if there are no more tags to read. </returns>
        /// <exception cref="NbtFormatException"> If an error occurred while parsing data in NBT format. </exception>
        /// <exception cref="InvalidReaderStateException"> If NbtReader cannot recover from a previous parsing error. </exception>
        public bool ReadToFollowing() {
            switch (state) {
                case ParseState.AtStreamBeginning:
                    // set state to error in case reader.ReadTagType throws.
                    state = ParseState.Error;
                    // read first tag, make sure it's a compound
                    if (reader.ReadTagType() != NbtTagType.Compound) {
                        throw NbtFormatException.NotCompoundRoot();
                    }
                    Depth = 1;
                    TagType = NbtTagType.Compound;
                    // Read root name. Advance to the first inside tag.
                    ReadTagHeader(true);
                    RootName = TagName;
                    return true;

                case ParseState.AtCompoundBeginning:
                    GoDown();
                    state = ParseState.InCompound;
                    goto case ParseState.InCompound;

                case ParseState.InCompound:
                    state = ParseState.Error;
                    if (atValue) {
                        SkipValue();
                    }
                    // Read next tag, check if we've hit the end
                    if (canSeekStream) {
                        LongTagStartOffset = reader.BaseStream.Position - streamStartOffset;
                    }

                    TagType = reader.ReadTagType();
                    state = ParseState.InCompound;

                    if (TagType == NbtTagType.End) {
                        TagName = null;
                        valueCache = null;
                        TagsRead++;
                        state = ParseState.AtCompoundEnd;
                        if (SkipEndTags) {
                            TagsRead--;
                            goto case ParseState.AtCompoundEnd;
                        } else {
                            return true;
                        }
                    } else {
                        ReadTagHeader(true);
                        return true;
                    }

                case ParseState.AtListBeginning:
                    GoDown();
                    ListIndex = -1;
                    TagType = ListType;
                    state = ParseState.InList;
                    goto case ParseState.InList;

                case ParseState.InList:
                    state = ParseState.Error;
                    if (atValue) {
                        SkipValue();
                    }
                    ListIndex++;
                    if (ListIndex >= ParentTagLength) {
                        GoUp();
                        if (ParentTagType == NbtTagType.List) {
                            state = ParseState.InList;
                            TagType = NbtTagType.List;
                            goto case ParseState.InList;
                        } else if (ParentTagType == NbtTagType.Compound) {
                            state = ParseState.InCompound;
                            goto case ParseState.InCompound;
                        } else {
                            // This should not happen unless NbtReader is bugged
                            throw new NbtFormatException(InvalidParentTagError);
                        }
                    } else {
                        if (canSeekStream) {
                            LongTagStartOffset = reader.BaseStream.Position - streamStartOffset;
                        }
                        state = ParseState.InList;
                        ReadTagHeader(false);
                    }
                    return true;

                case ParseState.AtCompoundEnd:
                    GoUp();
                    if (ParentTagType == NbtTagType.List) {
                        state = ParseState.InList;
                        TagType = NbtTagType.Compound;
                        goto case ParseState.InList;
                    } else if (ParentTagType == NbtTagType.Compound) {
                        state = ParseState.InCompound;
                        goto case ParseState.InCompound;
                    } else if (ParentTagType == NbtTagType.Unknown) {
                        state = ParseState.AtStreamEnd;
                        return false;
                    } else {
                        // This should not happen unless NbtReader is bugged
                        state = ParseState.Error;
                        throw new NbtFormatException(InvalidParentTagError);
                    }

                case ParseState.AtStreamEnd:
                    // nothing left to read!
                    return false;

                default:
                    // Parsing error, or unexpected state.
                    throw new InvalidReaderStateException(ErroneousStateError);
            }
        }


        void ReadTagHeader(bool readName) {
            // Setting state to error in case reader throws
            ParseState oldState = state;
            state = ParseState.Error;
            TagsRead++;
            TagName = (readName ? reader.ReadTagName() : null);

            valueCache = null;
            TagLength = 0;
            atValue = false;
            ListType = NbtTagType.Unknown;

            switch (TagType) {
                case NbtTagType.Byte:
                case NbtTagType.Short:
                case NbtTagType.Int:
                case NbtTagType.Long:
                case NbtTagType.Float:
                case NbtTagType.Double:
                case NbtTagType.String:
                    atValue = true;
                    state = oldState;
                    break;

                case NbtTagType.IntArray:
                case NbtTagType.ByteArray:
                case NbtTagType.LongArray:
                    // Negative lengths are tolerated as empty, exceeding Minecraft's own readers on purpose
                    TagLength = Math.Max(0, reader.ReadInt32());
                    atValue = true;
                    state = oldState;
                    break;

                case NbtTagType.List:
                    ListType = reader.ReadListHeader(out int listLength);
                    TagLength = listLength;
                    state = ParseState.AtListBeginning;
                    break;

                case NbtTagType.Compound:
                    state = ParseState.AtCompoundBeginning;
                    break;

                default:
                    // This should not happen unless NbtBinaryReader is bugged
                    throw new NbtFormatException("Trying to read tag of unknown type.");
            }
        }


        // Goes one step down the NBT file's hierarchy, preserving current state
        void GoDown() {
            if (Depth > NbtTag.MaxDepth) {
                state = ParseState.Error;
                throw new NbtFormatException(NbtTag.DepthLimitMessage);
            }
            if (nodes == null) {
                nodes = new Node[4];
            } else if (nodeCount == nodes.Length) {
                Array.Resize(ref nodes, nodes.Length * 2);
            }
            ref Node newNode = ref nodes[nodeCount++];
            newNode.ListIndex = ListIndex;
            newNode.ParentTagLength = ParentTagLength;
            newNode.ParentName = ParentName;
            newNode.ParentTagType = ParentTagType;
            newNode.ListType = ListType;

            ParentName = TagName;
            ParentTagType = TagType;
            ParentTagLength = TagLength;
            ListIndex = 0;
            TagLength = 0;

            Depth++;
        }


        // Goes one step up the NBT file's hierarchy, restoring previous state
        void GoUp() {
            NullableSupport.Assert(nodes != null);
            ref Node oldNode = ref nodes[--nodeCount];

            ParentName = oldNode.ParentName;
            ParentTagType = oldNode.ParentTagType;
            ParentTagLength = oldNode.ParentTagLength;
            ListIndex = oldNode.ListIndex;
            ListType = oldNode.ListType;
            // The array is reused, so drop the popped frame's name reference by hand
            oldNode.ParentName = null;
            TagLength = 0;

            Depth--;
        }


        void SkipValue() {
            // Make sure to check for "atValue" before calling this method. Routing the scalars
            // through SkipPayload instead costs an extra dispatch per skipped value, measured
            // at 5% on the Bedrock palette walk.
            switch (TagType) {
                case NbtTagType.Byte:
                    reader.ReadByte();
                    break;

                case NbtTagType.Short:
                    reader.ReadInt16();
                    break;

                case NbtTagType.Float:
                    reader.ReadSingle();
                    break;

                case NbtTagType.Int:
                    reader.ReadInt32();
                    break;

                case NbtTagType.Double:
                    reader.ReadDouble();
                    break;

                case NbtTagType.Long:
                    reader.ReadInt64();
                    break;

                case NbtTagType.ByteArray:
                    reader.Skip<byte>(TagLength);
                    break;

                case NbtTagType.IntArray:
                    reader.Skip<int>(TagLength);
                    break;

                case NbtTagType.LongArray:
                    reader.Skip<long>(TagLength);
                    break;

                case NbtTagType.String:
                    reader.SkipString();
                    break;

                default:
                    throw new InvalidOperationException(NonValueTagError);
            }
            atValue = false;
            valueCache = null;
        }


        /// <summary> Reads until a tag with the specified name is found. 
        /// Returns false if are no more tags to read (end of stream is reached). </summary>
        /// <param name="tagName"> Name of the tag. May be null (to look for next unnamed tag). </param>
        /// <returns> <c>true</c> if a matching tag is found; otherwise <c>false</c>. </returns>
        /// <exception cref="NbtFormatException"> If an error occurred while parsing data in NBT format. </exception>
        /// <exception cref="InvalidReaderStateException"> If NbtReader cannot recover from a previous parsing error. </exception>
        public bool ReadToFollowing(string? tagName) {
            while (ReadToFollowing()) {
                if (TagName == tagName) {
                    return true;
                }
            }
            return false;
        }


        /// <summary> Advances the NbtReader to the next descendant tag with the specified name.
        /// If none matches, the reader is left on the first tag outside the current tag's subtree:
        /// its next sibling, an enclosing container's End tag when <see cref="SkipEndTags"/> is
        /// <c>false</c>, or the end of the stream. </summary>
        /// <param name="tagName"> Name of the tag you wish to move to. May be null (to look for next unnamed tag). </param>
        /// <returns> <c>true</c> if a matching descendant tag is found; otherwise <c>false</c>. </returns>
        /// <exception cref="NbtFormatException"> If an error occurred while parsing data in NBT format. </exception>
        /// <exception cref="InvalidReaderStateException"> If NbtReader cannot recover from a previous parsing error. </exception>
        public bool ReadToDescendant(string? tagName) {
            if (state == ParseState.Error) {
                throw new InvalidReaderStateException(ErroneousStateError);
            } else if (state == ParseState.AtStreamEnd) {
                return false;
            }
            int currentDepth = Depth;
            while (ReadToFollowing()) {
                if (Depth <= currentDepth) {
                    return false;
                } else if (TagName == tagName) {
                    return true;
                }
            }
            return false;
        }


        /// <summary> Advances the NbtReader to the next sibling tag, skipping any child tags.
        /// If there are no more siblings, NbtReader is positioned on the tag following the last of this tag's descendants. </summary>
        /// <returns> <c>true</c> if a sibling element is found; otherwise <c>false</c>. </returns>
        /// <exception cref="NbtFormatException"> If an error occurred while parsing data in NBT format. </exception>
        /// <exception cref="InvalidReaderStateException"> If NbtReader cannot recover from a previous parsing error. </exception>
        public bool ReadToNextSibling() {
            if (state == ParseState.Error) {
                throw new InvalidReaderStateException(ErroneousStateError);
            } else if (state == ParseState.AtStreamEnd) {
                return false;
            }
            int currentDepth = Depth;
            while (true) {
                // A container the cursor has not entered can be discarded wholesale
                if (state == ParseState.AtCompoundBeginning || state == ParseState.AtListBeginning) {
                    SkipUnenteredContainer();
                    if (state == ParseState.AtStreamEnd) return false;
                    continue;
                }
                if (!ReadToFollowing()) return false;
                if (Depth == currentDepth) return true;
                if (Depth < currentDepth) return false;
            }
        }


        /// <summary> Advances the NbtReader to the next sibling tag with the specified name.
        /// If a matching sibling tag is not found, NbtReader is positioned on the tag following the last siblings. </summary>
        /// <param name="tagName"> The name of the sibling tag you wish to move to. </param>
        /// <returns> <c>true</c> if a matching sibling element is found; otherwise <c>false</c>. </returns>
        /// <exception cref="NbtFormatException"> If an error occurred while parsing data in NBT format. </exception>
        /// <exception cref="InvalidReaderStateException"> If NbtReader cannot recover from a previous parsing error. </exception>
        public bool ReadToNextSibling(string? tagName) {
            while (ReadToNextSibling()) {
                if (TagName == tagName) {
                    return true;
                }
            }
            return false;
        }


        /// <summary> Skips current tag, its value/descendants, and any following siblings.
        /// In other words, reads until parent tag's sibling. </summary>
        /// <returns> Total number of tags that were skipped. Returns 0 if end of the stream is reached. </returns>
        /// <exception cref="NbtFormatException"> If an error occurred while parsing data in NBT format. </exception>
        /// <exception cref="InvalidReaderStateException"> If NbtReader cannot recover from a previous parsing error. </exception>
        public int Skip() {
            if (state == ParseState.Error) {
                throw new InvalidReaderStateException(ErroneousStateError);
            } else if (state == ParseState.AtStreamEnd) {
                return 0;
            }
            int startDepth = Depth;
            int skipped = 0;
            while (true) {
                // A container the cursor has not entered can be discarded wholesale
                if (state == ParseState.AtCompoundBeginning || state == ParseState.AtListBeginning) {
                    skipped += SkipUnenteredContainer();
                    if (state == ParseState.AtStreamEnd) return skipped;
                    continue;
                }
                if (!ReadToFollowing() || Depth < startDepth) return skipped;
                skipped++;
            }
        }


        // Discards the current unentered container through the binary layer, building no tags
        // or cursor states for its descendants. Leaves the cursor where a ReadToFollowing walk
        // past the last descendant would, and counts the tags it passed the same way.
        int SkipUnenteredContainer() {
            NbtTagType resumeParent = ParentTagType;
            int tags = 0, endTags = 0;
            // What an equivalent tree walk would have left at this depth. GoDown refuses to open
            // a container past MaxDepth, so this one gets MaxDepth - Depth + 1.
            int depthBudget = NbtTag.MaxDepth - Depth + 1;
            state = ParseState.Error;
            if (TagType == NbtTagType.Compound) {
                reader.SkipPayload(NbtTagType.Compound, depthBudget, ref tags, ref endTags);
            } else {
                // The list's type and length were consumed with its header
                reader.SkipListElements(ListType, TagLength, NbtTag.ConsumeDepthBudget(depthBudget),
                                        ref tags, ref endTags);
            }
            int newlyRead = tags + (SkipEndTags ? 0 : endTags);
            TagsRead += newlyRead;
            if (resumeParent == NbtTagType.List) {
                state = ParseState.InList;
            } else if (resumeParent == NbtTagType.Compound) {
                state = ParseState.InCompound;
            } else {
                // The root itself was skipped, so the document is over. Land on the same
                // cursor state an orderly walk ends with.
                state = ParseState.AtStreamEnd;
                TagType = NbtTagType.End;
                TagName = null;
                valueCache = null;
                TagLength = 0;
            }
            return newlyRead;
        }


        /// <summary> Reads the entirety of the current tag, including any descendants,
        /// and constructs an NbtTag object of the appropriate type. Cannot be called on an End tag,
        /// which no NbtTag represents; the reader stays usable after that refusal. </summary>
        /// <returns> Constructed NbtTag object. </returns>
        /// <exception cref="NbtFormatException"> If an error occurred while parsing data in NBT format. </exception>
        /// <exception cref="InvalidReaderStateException"> If NbtReader cannot recover from a previous parsing error. </exception>
        /// <exception cref="EndOfStreamException"> End of stream has been reached (no more tags can be read). </exception>
        /// <exception cref="InvalidOperationException"> The reader is on an End tag, or the tag's value has
        /// already been read and CacheTagValues is false. </exception>
        public NbtTag ReadAsTag() {
            switch (state) {
                case ParseState.Error:
                    throw new InvalidReaderStateException(ErroneousStateError);

                case ParseState.AtStreamEnd:
                    throw new EndOfStreamException();

                case ParseState.AtStreamBeginning:
                    ReadToFollowing();
                    break;
            }

            // get this tag
            NbtTag parent;
            if (TagType == NbtTagType.Compound) {
                parent = new NbtCompound(TagName);
            } else if (TagType == NbtTagType.List) {
                parent = new NbtList(TagName, ListType);
            } else if (atValue || valueCache != null) {
                // A value tag has no children. Its value comes from the stream, or from the
                // cache when the caller already read it with CacheTagValues on.
                NbtTag result = atValue ? ReadValueAsTag() : TagFromCachedValue();
                ReadToFollowing();
                return result;
            } else {
                // An End tag has no NbtTag counterpart, and a consumed value cannot be reread.
                // Either way the reader stays where it is.
                throw new InvalidOperationException(NoValueToReadError);
            }

            int startingDepth = Depth;
            int parentDepth = Depth;

            do {
                ReadToFollowing();
                // Going up the file tree, or end of document: wrap up
                while (Depth <= parentDepth && parent.Parent != null) {
                    parent = parent.Parent;
                    parentDepth--;
                }
                if (Depth <= startingDepth) break;

                NbtTag thisTag;
                if (TagType == NbtTagType.Compound) {
                    thisTag = new NbtCompound(TagName);
                    AddToParent(thisTag, parent);
                    parent = thisTag;
                    parentDepth = Depth;
                } else if (TagType == NbtTagType.List) {
                    thisTag = new NbtList(TagName, ListType);
                    AddToParent(thisTag, parent);
                    parent = thisTag;
                    parentDepth = Depth;
                } else if (TagType != NbtTagType.End) {
                    thisTag = ReadValueAsTag();
                    AddToParent(thisTag, parent);
                }
            } while (true);

            return parent;
        }


        void AddToParent(NbtTag thisTag, NbtTag parent) {
            if (parent is NbtList parentAsList) {
                parentAsList.Add(thisTag);
            } else if (parent is NbtCompound parentAsCompound) {
                try {
                    parentAsCompound.Add(thisTag);
                } catch (ArgumentException) {
                    // A duplicate name is malformed input, not a caller error.
                    state = ParseState.Error;
                    throw new NbtFormatException("Duplicate tag name in compound: " + thisTag.Name);
                }
            } else {
                // cannot happen unless NbtReader is bugged
                throw new NbtFormatException(InvalidParentTagError);
            }
        }


        NbtTag ReadValueAsTag() {
            if (!atValue) {
                // Should never happen
                throw new InvalidOperationException(NoValueToReadError);
            }
            atValue = false;
            try {
                return ReadValueAsTagInternal();
            } catch {
                // A failed payload read leaves the stream desynchronised
                state = ParseState.Error;
                throw;
            }
        }


        NbtTag ReadValueAsTagInternal() {
            switch (TagType) {
                case NbtTagType.Byte:
                    return new NbtByte(TagName, reader.ReadByte());

                case NbtTagType.Short:
                    return new NbtShort(TagName, reader.ReadInt16());

                case NbtTagType.Int:
                    return new NbtInt(TagName, reader.ReadInt32());

                case NbtTagType.Long:
                    return new NbtLong(TagName, reader.ReadInt64());

                case NbtTagType.Float:
                    return new NbtFloat(TagName, reader.ReadSingle());

                case NbtTagType.Double:
                    return new NbtDouble(TagName, reader.ReadDouble());

                case NbtTagType.String:
                    return new NbtString(TagName, reader.ReadString());

                case NbtTagType.ByteArray:
                    return new NbtByteArray(TagName, reader.ReadByteArray(TagLength));

                case NbtTagType.IntArray:
                    return new NbtIntArray(TagName, reader.ReadInt32Array(TagLength));

                case NbtTagType.LongArray:
                    return new NbtLongArray(TagName, reader.ReadInt64Array(TagLength));

                default:
                    throw new InvalidOperationException(NonValueTagError);
            }
        }


        // Builds the tag for a value that ReadValue already consumed and CacheTagValues kept
        NbtTag TagFromCachedValue() {
            NullableSupport.Assert(valueCache != null);
            switch (TagType) {
                case NbtTagType.Byte:
                    return new NbtByte(TagName, (byte)valueCache);
                case NbtTagType.Short:
                    return new NbtShort(TagName, (short)valueCache);
                case NbtTagType.Int:
                    return new NbtInt(TagName, (int)valueCache);
                case NbtTagType.Long:
                    return new NbtLong(TagName, (long)valueCache);
                case NbtTagType.Float:
                    return new NbtFloat(TagName, (float)valueCache);
                case NbtTagType.Double:
                    return new NbtDouble(TagName, (double)valueCache);
                case NbtTagType.String:
                    return new NbtString(TagName, (string)valueCache);
                case NbtTagType.ByteArray:
                    return new NbtByteArray(TagName, (byte[])valueCache);
                case NbtTagType.IntArray:
                    return new NbtIntArray(TagName, (int[])valueCache);
                case NbtTagType.LongArray:
                    return new NbtLongArray(TagName, (long[])valueCache);
                default:
                    throw new InvalidOperationException(NonValueTagError);
            }
        }


        /// <summary> Reads the value as the type specified: the tag's own value type directly, any
        /// type <see cref="Convert.ChangeType(object, Type)"/> can reach from it, such as a wider
        /// numeric type or a string, or an enum type from an integral value or a member name.
        /// Array values are returned as their own array type only. </summary>
        /// <remarks> A failed conversion happens after the value was read from the stream, so the value
        /// is then gone unless <see cref="CacheTagValues"/> is on; the reader stays usable. </remarks>
        /// <typeparam name="T"> The type of the value to be returned. </typeparam>
        /// <returns> Tag value converted to the requested type. </returns>
        /// <exception cref="EndOfStreamException"> End of stream has been reached (no more tags can be read). </exception>
        /// <exception cref="NbtFormatException"> If an error occurred while parsing data in NBT format. </exception>
        /// <exception cref="InvalidOperationException"> Value has already been read, or there is no value to read. </exception>
        /// <exception cref="InvalidReaderStateException"> If NbtReader cannot recover from a previous parsing error. </exception>
        /// <exception cref="InvalidCastException"> Tag value cannot be converted to the requested type. </exception>
        /// <exception cref="FormatException"> A string value is not in a format the requested type accepts,
        /// or names no member of the requested enum type. </exception>
        /// <exception cref="OverflowException"> The value does not fit in the requested type. </exception>
        public T ReadValueAs<T>() {
            object value = ReadValue();
            if (value is T exact) return exact;
            if (typeof(T).IsEnum) return (T)ConvertToEnum(value, typeof(T), Type.GetTypeCode(typeof(T)));
            return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
        }


        // Enum.ToObject can silently wrap numbers. Convert first to check that the value fits.
        // Typed overloads avoid an extra allocation.
        static object ConvertToEnum(object value, Type enumType, TypeCode underlyingCode) {
            if (value is string name) {
                try {
                    return Enum.Parse(enumType, name);
                } catch (ArgumentException ex) {
                    throw new FormatException("\"" + name + "\" is not a member of " + enumType + ".", ex);
                }
            }
            if (value is byte || value is short || value is int || value is long) {
                switch (underlyingCode) {
                    case TypeCode.SByte:
                        return Enum.ToObject(enumType, Convert.ToSByte(value, CultureInfo.InvariantCulture));
                    case TypeCode.Byte:
                        return Enum.ToObject(enumType, Convert.ToByte(value, CultureInfo.InvariantCulture));
                    case TypeCode.Int16:
                        return Enum.ToObject(enumType, Convert.ToInt16(value, CultureInfo.InvariantCulture));
                    case TypeCode.UInt16:
                        return Enum.ToObject(enumType, Convert.ToUInt16(value, CultureInfo.InvariantCulture));
                    case TypeCode.Int32:
                        return Enum.ToObject(enumType, Convert.ToInt32(value, CultureInfo.InvariantCulture));
                    case TypeCode.UInt32:
                        return Enum.ToObject(enumType, Convert.ToUInt32(value, CultureInfo.InvariantCulture));
                    case TypeCode.Int64:
                        return Enum.ToObject(enumType, Convert.ToInt64(value, CultureInfo.InvariantCulture));
                    case TypeCode.UInt64:
                        return Enum.ToObject(enumType, Convert.ToUInt64(value, CultureInfo.InvariantCulture));
                }
            }
            throw new InvalidCastException("Cannot convert a " + value.GetType() + " value to " + enumType + ".");
        }


        /// <summary> Reads the value as an object of the correct type, boxed.
        /// Cannot be called for tags that do not have a single-object value (compound, list, and end tags). </summary>
        /// <returns> Tag value converted to the requested type. </returns>
        /// <exception cref="EndOfStreamException"> End of stream has been reached (no more tags can be read). </exception>
        /// <exception cref="NbtFormatException"> If an error occurred while parsing data in NBT format. </exception>
        /// <exception cref="InvalidOperationException"> Value has already been read, or there is no value to read. </exception>
        /// <exception cref="InvalidReaderStateException"> If NbtReader cannot recover from a previous parsing error. </exception>
        public object ReadValue() {
            if (state == ParseState.Error) {
                throw new InvalidReaderStateException(ErroneousStateError);
            }
            if (state == ParseState.AtStreamEnd) {
                throw new EndOfStreamException();
            }
            if (!atValue) {
                // Only CacheTagValues leaves a value behind, and only the current tag's
                if (valueCache != null) return valueCache;
                throw new InvalidOperationException(NoValueToReadError);
            }
            valueCache = null;
            atValue = false;
            object value;
            try {
                value = ReadBoxedValue(TagType);
            } catch {
                // A failed payload read leaves the stream desynchronised
                state = ParseState.Error;
                throw;
            }
            if (cacheTagValues) {
                valueCache = value;
            }
            return value;
        }


        // One value of the given type, boxed. Array types take their length from TagLength.
        object ReadBoxedValue(NbtTagType type) {
            switch (type) {
                case NbtTagType.Byte:
                    return reader.ReadByte();

                case NbtTagType.Short:
                    return reader.ReadInt16();

                case NbtTagType.Float:
                    return reader.ReadSingle();

                case NbtTagType.Int:
                    return reader.ReadInt32();

                case NbtTagType.Double:
                    return reader.ReadDouble();

                case NbtTagType.Long:
                    return reader.ReadInt64();

                case NbtTagType.ByteArray:
                    return reader.ReadByteArray(TagLength);

                case NbtTagType.IntArray:
                    return reader.ReadInt32Array(TagLength);

                case NbtTagType.LongArray:
                    return reader.ReadInt64Array(TagLength);

                case NbtTagType.String:
                    return reader.ReadString();

                default:
                    throw new InvalidOperationException(NonValueTagError);
            }
        }


        /// <summary> If the current tag is a List, or a value element of one, reads the list's
        /// remaining values as an array. A current element whose value has not been read yet is
        /// included; one whose value was already read is not. The element type must be byte, short,
        /// int, long, float, double, or string. Stops reading after the last list element; an
        /// empty list is not entered, so the reader stays on the list tag. </summary>
        /// <remarks> The refusals below are checked before anything is read, so after one of them the
        /// reader is where it was. A failure once reading has begun, a conversion included, leaves the
        /// reader in its error state, since the stream position is then unknown. After a successful
        /// call no value is available to <see cref="ReadValue"/>, even with <see cref="CacheTagValues"/>. </remarks>
        /// <typeparam name="T"> Element type of the array to be returned: the list's own element type,
        /// any primitive or string type <see cref="Convert.ChangeType(object, Type)"/> can reach from it,
        /// or an enum type over a list of integral values or member names. </typeparam>
        /// <returns> List contents converted to an array of the requested type. </returns>
        /// <exception cref="EndOfStreamException"> End of stream has been reached, or the list's
        /// declared length does not fit in the remaining stream. </exception>
        /// <exception cref="InvalidOperationException"> The reader is not on a List or one of its
        /// value elements, the list's element type is not supported by this method, or
        /// <typeparamref name="T"/> is not a type values can be converted to. </exception>
        /// <exception cref="FormatException"> A value could not be converted to <typeparamref name="T"/>. </exception>
        /// <exception cref="OverflowException"> A value does not fit in <typeparamref name="T"/>. </exception>
        /// <exception cref="InvalidReaderStateException"> If NbtReader cannot recover from a previous parsing error. </exception>
        /// <exception cref="NbtFormatException"> If an error occurred while parsing data in NBT format. </exception>
        public T[] ReadListAsArray<T>() {
            NbtTagType elementType;
            int elementsToRead;
            // Elements whose headers the caller has not seen, which is what TagsRead counts
            int unpublished;
            switch (state) {
                case ParseState.AtStreamEnd:
                    throw new EndOfStreamException();
                case ParseState.Error:
                    throw new InvalidReaderStateException(ErroneousStateError);
                case ParseState.AtListBeginning:
                    // Validate before changing any state
                    elementType = ListType;
                    EnsureTypesAreConvertible(elementType, typeof(T));
                    if (TagLength == 0) {
                        // Nothing to enter, so the cursor stays on the list and the next step
                        // treats it like any other list tag
                        return Array.Empty<T>();
                    }
                    GoDown();
                    ListIndex = 0;
                    TagType = elementType;
                    state = ParseState.InList;
                    elementsToRead = ParentTagLength;
                    unpublished = elementsToRead;
                    break;
                case ParseState.InList:
                    // The public ListType describes the current element, so the list's own
                    // element type comes from the node that entered it
                    NullableSupport.Assert(nodes != null);
                    elementType = nodes[nodeCount - 1].ListType;
                    EnsureTypesAreConvertible(elementType, typeof(T));
                    if (ListIndex >= ParentTagLength) {
                        // An earlier bulk read consumed everything
                        unpublished = 0;
                        elementsToRead = 0;
                    } else {
                        // The current element's header is out already; its value is read here
                        // unless the caller consumed it
                        unpublished = ParentTagLength - ListIndex - 1;
                        elementsToRead = unpublished + (atValue ? 1 : 0);
                    }
                    break;
                default:
                    throw new InvalidOperationException("ReadListAsArray may only be used on List tags.");
            }

            try {
                // Check if declared length is plausible (fits into remaining stream) before allocating huge buffers.
                // Conversion can change the size of each element, so use the result type for the allocation limit.
                reader.EnsureAllocation((long)elementsToRead * ManagedElementSize<T>());
                reader.EnsureCanRead((long)elementsToRead * MinElementSize(elementType, reader.UsesVarInt));

                // Exact-type matches skip boxing and conversion dispatch, and ints and longs
                // reach the bulk readers. Real conversions fall through to ChangeType below.
                if (typeof(T) == typeof(byte) && elementType == NbtTagType.Byte) {
                    T[] val = (T[])(object)reader.ReadByteArray(elementsToRead);
                    FinishListRead(unpublished);
                    return val;
                }
                if (typeof(T) == typeof(int) && elementType == NbtTagType.Int) {
                    T[] val = (T[])(object)reader.ReadInt32Array(elementsToRead);
                    FinishListRead(unpublished);
                    return val;
                }
                if (typeof(T) == typeof(long) && elementType == NbtTagType.Long) {
                    T[] val = (T[])(object)reader.ReadInt64Array(elementsToRead);
                    FinishListRead(unpublished);
                    return val;
                }

                if (typeof(T).IsEnum) {
                    T[] enums = ReadListAsEnums<T>(elementType, elementsToRead);
                    FinishListRead(unpublished);
                    return enums;
                }

                T[] result = new T[elementsToRead];
                if (typeof(T) == typeof(short) && elementType == NbtTagType.Short) {
                    short[] typed = (short[])(object)result;
                    for (int i = 0; i < elementsToRead; i++) typed[i] = reader.ReadInt16();
                } else if (typeof(T) == typeof(float) && elementType == NbtTagType.Float) {
                    float[] typed = (float[])(object)result;
                    for (int i = 0; i < elementsToRead; i++) typed[i] = reader.ReadSingle();
                } else if (typeof(T) == typeof(double) && elementType == NbtTagType.Double) {
                    double[] typed = (double[])(object)result;
                    for (int i = 0; i < elementsToRead; i++) typed[i] = reader.ReadDouble();
                } else if (typeof(T) == typeof(string) && elementType == NbtTagType.String) {
                    string[] typed = (string[])(object)result;
                    for (int i = 0; i < elementsToRead; i++) typed[i] = reader.ReadString();
                } else {
                    // Real conversions box each element and go through Convert
                    for (int i = 0; i < elementsToRead; i++) {
                        result[i] = (T)Convert.ChangeType(ReadBoxedValue(elementType), typeof(T), CultureInfo.InvariantCulture);
                    }
                }
                FinishListRead(unpublished);
                return result;
            } catch {
                // A failed read or conversion leaves the stream desynchronised
                state = ParseState.Error;
                throw;
            }
        }


        // Marks every element consumed, leaving the reader ready to step out of the list.
        // Only elements whose headers the caller never saw count as newly read.
        void FinishListRead(int unpublished) {
            TagsRead += unpublished;
            ListIndex = ParentTagLength;
            atValue = false;
            valueCache = null;
        }


        // Reject unsupported types before consuming any elements. Check enums first because
        // GetTypeCode reports their underlying type, but they need different conversion rules.
        static void EnsureTypesAreConvertible(NbtTagType elementType, Type targetType) {
            if (targetType.IsEnum) {
                switch (elementType) {
                    case NbtTagType.Byte:
                    case NbtTagType.Short:
                    case NbtTagType.Int:
                    case NbtTagType.Long:
                    case NbtTagType.String:
                        return;
                    default:
                        throw new InvalidOperationException("ReadListAsArray may only be used on lists of integral types or strings when reading into an enum type.");
                }
            }

            switch (elementType) {
                case NbtTagType.Byte:
                case NbtTagType.Short:
                case NbtTagType.Int:
                case NbtTagType.Long:
                case NbtTagType.Float:
                case NbtTagType.Double:
                case NbtTagType.String:
                    break;
                default:
                    throw new InvalidOperationException("ReadListAsArray may only be used on lists of value types.");
            }
            
            switch (Type.GetTypeCode(targetType)) {
                case TypeCode.Object:
                case TypeCode.DateTime:
                case TypeCode.DBNull:
                case TypeCode.Empty:
                    throw new InvalidOperationException("ReadListAsArray cannot convert list values to " + targetType + ".");
                default:
                    return;
            }
        }


        // On .NET Core, a list whose element type is the enum's own underlying type is read as
        // that primitive array and block-copied into the enum array by Array.Copy. Casting the
        // primitive array instead would keep its runtime type, so enumerating it non-generically
        // would yield boxed integers. .NET Framework refuses both the copy and pinning an enum
        // array, so the netstandard2.0 build converts element by element, as every other pairing does.
        T[] ReadListAsEnums<T>(NbtTagType elementType, int count) {
            T[] result = new T[count];
#if NETCOREAPP
            Type underlying = Enum.GetUnderlyingType(typeof(T));
            Array? primitives = null;
            if (underlying == typeof(int) && elementType == NbtTagType.Int) {
                primitives = reader.ReadInt32Array(count);
            } else if (underlying == typeof(long) && elementType == NbtTagType.Long) {
                primitives = reader.ReadInt64Array(count);
            } else if (underlying == typeof(byte) && elementType == NbtTagType.Byte) {
                primitives = reader.ReadByteArray(count);
            } else if (underlying == typeof(short) && elementType == NbtTagType.Short) {
                short[] shorts = new short[count];
                for (int i = 0; i < count; i++) shorts[i] = reader.ReadInt16();
                primitives = shorts;
            }
            if (primitives != null) {
                Array.Copy(primitives, result, count);
                return result;
            }
#endif
            TypeCode underlyingCode = Type.GetTypeCode(typeof(T));
            for (int i = 0; i < count; i++) {
                result[i] = (T)ConvertToEnum(ReadBoxedValue(elementType), typeof(T), underlyingCode);
            }
            return result;
        }


        // Smallest serialized size (lower bound) for an element of the given type.
        static int MinElementSize(NbtTagType type, bool varInt) {
            switch (type) {
                case NbtTagType.Byte:
                    return 1;
                case NbtTagType.Short:
                    return 2;
                case NbtTagType.String: // empty string is just its length prefix
                    return varInt ? 1 : 2;
                case NbtTagType.Int:
                    return varInt ? 1 : 4;
                case NbtTagType.Float:
                    return 4;
                case NbtTagType.Long:
                    return varInt ? 1 : 8;
                default: // Double
                    return 8;
            }
        }


        static int ManagedElementSize<T>() {
            Type target = typeof(T);
            if (target == typeof(byte) || target == typeof(sbyte) || target == typeof(bool)) return 1;
            if (target == typeof(short) || target == typeof(ushort) || target == typeof(char)) return 2;
            if (target == typeof(int) || target == typeof(uint) || target == typeof(float)) return 4;
            if (target == typeof(long) || target == typeof(ulong) || target == typeof(double)) return 8;
            if (target == typeof(decimal)) return sizeof(decimal);
            // Array slots for reference types like string hold pointers
            if (!target.IsValueType) return IntPtr.Size;
            // Only enums remain; TypeCode gives their underlying type.
            switch (Type.GetTypeCode(target)) {
                case TypeCode.SByte:
                case TypeCode.Byte:
                    return 1;
                case TypeCode.Int16:
                case TypeCode.UInt16:
                    return 2;
                case TypeCode.Int32:
                case TypeCode.UInt32:
                    return 4;
                default:
                    return 8;
            }
        }


        /// <summary> Parsing option: Whether NbtReader should skip End tags in ReadToFollowing() automatically while parsing.
        /// Default is <c>true</c>. </summary>
        public bool SkipEndTags { get; set; }

        /// <summary> Parsing option: Whether NbtReader should save a copy of the most recently read tag's value.
        /// Unless CacheTagValues is <c>true</c>, tag values can only be read once. Default is <c>false</c>. </summary>
        public bool CacheTagValues {
            get { return cacheTagValues; }
            set {
                cacheTagValues = value;
                if (!cacheTagValues) {
                    valueCache = null;
                }
            }
        }

        bool cacheTagValues;


        /// <summary> Returns a String that represents the tag currently being read by this NbtReader instance.
        /// Prints current tag's depth, ordinal number, type, name, and size (for arrays and lists). Does not print value.
        /// Indents the tag according default indentation (NbtTag.DefaultIndentString). </summary>
        public override string ToString() {
            return ToString(false, NbtTag.DefaultIndentString);
        }


        /// <summary> Returns a String that represents the tag currently being read by this NbtReader instance.
        /// Prints current tag's depth, ordinal number, type, name, size (for arrays and lists), and optionally value.
        /// Indents the tag according default indentation (NbtTag.DefaultIndentString). </summary>
        /// <param name="includeValue"> If set to <c>true</c>, also reads and prints the current tag's value. 
        /// Note that unless CacheTagValues is set to <c>true</c>, you can only read every tag's value ONCE. </param>
        public string ToString(bool includeValue) {
            return ToString(includeValue, NbtTag.DefaultIndentString);
        }


        /// <summary> Returns a String that represents the current NbtReader object.
        /// Prints current tag's depth, ordinal number, type, name, size (for arrays and lists), and optionally value. </summary>
        /// <param name="indentString"> String to be used for indentation. May be empty string, but may not be <c>null</c>. </param>
        /// <param name="includeValue"> If set to <c>true</c>, also reads and prints the current tag's value. </param>
        public string ToString(bool includeValue, string indentString) {
            if (indentString == null) throw new ArgumentNullException(nameof(indentString));
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < Depth; i++) {
                sb.Append(indentString);
            }
            sb.Append('#').Append(TagsRead).Append(". ").Append(TagType);
            if (IsList) {
                sb.Append('<').Append(ListType).Append('>');
            }
            if (HasLength) {
                sb.Append('[').Append(TagLength).Append(']');
            }
            sb.Append(' ').Append(TagName);
            if (includeValue && (atValue || valueCache != null) && TagType != NbtTagType.IntArray &&
                TagType != NbtTagType.ByteArray && TagType != NbtTagType.LongArray) {
                sb.Append(" = ").Append(ReadValue());
            }
            return sb.ToString();
        }


        const string NoValueToReadError = "Value already read, or no value to read.",
            NonValueTagError = "Trying to read value of a non-value tag.",
            InvalidParentTagError = "Parent tag is neither a Compound nor a List.",
            ErroneousStateError = "NbtReader is in an erroneous state!";
    }
}

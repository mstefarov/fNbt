namespace fNbt {
    // Traversal state for one entered compound or list. NbtReader keeps these in a grow-only
    // array, so entering a container allocates nothing once the array exists.
    internal struct NbtReaderNode {
        public string? ParentName;
        public NbtTagType ParentTagType;
        public NbtTagType ListType;
        public int ParentTagLength;
        public int ListIndex;
    }
}

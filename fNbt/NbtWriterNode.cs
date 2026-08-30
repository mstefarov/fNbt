namespace fNbt {
    // Traversal state for one entered compound or list. NbtWriter keeps the hot parent
    // context in a field and stacks only its ancestors, without per-level object allocations.
    internal struct NbtWriterNode {
        public NbtTagType Type;
        public NbtTagType ElementType;
        public int Length;
        // Index of the next list element to be committed.
        public int NextIndex;


        public NbtWriterNode(NbtTagType type, NbtTagType elementType = NbtTagType.Unknown,
                             int length = 0) {
            Type = type;
            ElementType = elementType;
            Length = length;
            NextIndex = 0;
        }
    }
}

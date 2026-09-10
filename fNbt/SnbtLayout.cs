namespace fNbt {
    /// <summary> Text layout produced by <see cref="NbtTag.ToSnbt()"/>. The layouts differ only in
    /// whitespace, which every SNBT reader skips, so each reads wherever the compact form reads;
    /// <see cref="NbtTag.ToSnbt()"/> says what that form asks of its reader. </summary>
    public enum SnbtLayout {
        /// <summary> One line, no whitespace: <c>{a:1,b:[1,2],c:[B;1B,2B]}</c>. What Minecraft
        /// itself prints (<c>Tag.toString()</c>) and what commands, logs and command storage use.
        /// The default. </summary>
        Compact,

        /// <summary> One line with a space after every comma and colon:
        /// <c>{a: 1, b: [1, 2], c: [B; 1B, 2B]}</c>. What <c>/data get</c> prints in chat and the
        /// default display of NBT Studio, nbtlib, Amulet, and mecha; the readable single-line form
        /// for logs and user interfaces. </summary>
        Spaced,

        /// <summary> Multiple lines, four-space indentation, one member per line as
        /// <c>key: value</c>; compounds and lists of containers expand, while arrays and lists of
        /// scalars stay on one line. The readable form for files and editors, as NBT Studio's
        /// expanded view and nbtlib's indented output write it. Minecraft's own <c>.snbt</c> files
        /// differ only in putting every list element on its own line. </summary>
        Indented,

        // TODO: add Expanded (every list element on its own line, as Minecraft's .snbt files,
        // deepslate and Amulet write) if someone asks for it.
    }
}

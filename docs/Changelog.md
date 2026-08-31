## 2.0.0 (fNbt, unreleased)
- Add NbtFlavor, naming the NBT wire encodings: Java (the default), JavaAnvil,
    JavaLegacy, JavaNetwork, Bedrock, BedrockNetwork, and ClassiCube. NbtFile,
    NbtReader, and NbtWriter take a flavor or an NbtOptions, and NbtFile also
    has a Flavor property and a static DefaultFlavor. The BigEndian
    properties, the bool constructors, and the bigEndian ReadRootTagName
    overloads still work (true is Java, false is Bedrock) and are now obsolete.
- Add BedrockNetwork, the varint encoding Bedrock Edition uses on the network,
    to every reader and writer. Its validation rejects TAG_Long_Array, which
    Bedrock's own reader cannot read; pass ValidateOnWrite = false to write it
    anyway.
- Add NbtCodec, for NBT documents without file framing: packet payloads,
    LevelDB values, and NBT embedded in other formats. It handles unnamed and
    non-compound roots (JavaNetwork), absent documents (a lone TAG_End byte),
    and back-to-back documents, and its reads stop exactly at the end of a
    document. Input is a Stream, a byte array, or on .NET 8 a
    ReadOnlySpan<byte>; output is a Stream, a new byte array, or on .NET 8 an
    IBufferWriter<byte>.
- Add NbtOptions, carrying the flavor, ValidateOnWrite (default on),
    ValidateOnRead (default off), and MaxAllocation, an opt-in cap on any
    single allocation a document can demand. Options are copied at
    construction, so changing an instance later does not affect readers or
    writers already created from it.
- Validation refuses what a flavor's own readers would reject: tag types the
    flavor predates, strings over its ceiling (256 bytes for ClassiCube,
    32,767 for Bedrock), and for NbtCodec a non-compound root. Reads stay
    generous unless ValidateOnRead is set. Bedrock-flavor saves that include
    TAG_Long_Array therefore now throw unless ValidateOnWrite = false.
- Java flavors write modified UTF-8 like Minecraft Java, so astral characters
    such as emoji round-trip instead of loading as U+FFFD or producing files
    Minecraft refuses. Reads accept both standard and modified UTF-8 under
    every flavor. Malformed strings throw NbtFormatException, as does a lone
    surrogate written under a Bedrock flavor.
- NbtCompound now keeps its children in insertion order: enumeration,
    ToString, and saved files list tags in the order they were added, even
    after removals.
- Much faster and leaner on real-world documents. Parsing allocates 44-54%
    less (repeated tag names are shared, and small compounds skip the
    dictionary), skipping unwanted data no longer builds throwaway tags
    (NbtReader.Skip runs about 4x faster with almost no allocation, and
    selector skips up to 7x faster), NbtComparer compares without boxing (2-12x faster,
    zero allocation), int/long arrays read and write in bulk on .NET 8 (up to
    25x faster), exact-size buffer writes serialize once instead of twice
    (about 1.4x faster), and compressed SaveToBuffer reuses pooled memory
    (67-73% less allocation).
- Negative list and array lengths now load as empty, and an empty list accepts
    any element type byte; both are more tolerant than Minecraft.
- NbtWriter refuses a bad write before it changes anything: a rejected string
    or name no longer consumes a list slot or leaves a partial tag behind,
    streaming and WriteTag nesting share one 512-level limit, and a write that
    fails partway through, an I/O error for example, leaves the writer in a
    failed state where every later call throws.
- Fixed NbtWriter.WriteIntArray and WriteLongArray writing the wrong elements
    when given a non-zero offset.
- Fixed NbtReader.ReadListAsArray throwing when called on an element of the
    list instead of the list itself.
- Compressed loads always validate the checksum, on .NET Standard 2.0 too (for
    ZLib, when the stream is seekable), and leave a seekable stream at its
    end. ZLib saves are about 1.3x faster on .NET 6 and later.

## 1.1.1 (fNbt)
- Every code path now rejects tags nested more than 512 levels deep, matching
    Minecraft's own limit, instead of crashing the process with an uncatchable
    stack overflow.
- Fixed several ways a corrupt document could load silently wrong: 32-bit
    overflow in list/array skip math, skipped lists of TAG_Long_Array consuming
    zero bytes, and huge declared lengths allocating before validation (#24).
    Malformed files now fail with helpful exceptions.
- String length prefixes now parse as unsigned, matching Minecraft Java.
    Strings of 32,768 to 65,535 bytes load correctly, and trying to write
    anything longer throws instead of corrupting output.
- NbtWriter.BeginList now accepts TAG_End as an empty list's element type,
    matching Minecraft Java and Bedrock.
- NbtWriter.WriteByteArray(Stream, ...) now throws on an undersized source
    instead of spinning in a read loop forever.
- Fixed parent-tracking bugs that could cause problems when moving tags between
    containers: cloned NbtList children came out detached, indexer setters kept
    the displaced tag's Parent, Insert skipped some of Add's guards, and
    it was possible to accidentally create a reference cycle. Also, AddRange
    and the collection constructors now validate the whole batch before
    changing any tags' parents.
- NbtReader's ReadListAsArray now validates up front. Reading corrupt data now
    toggles its error state instead of making it desync.
- Fixed valid ZLib streams getting rejected with InvalidDataException if they
    did not start with exactly 0x78.

## 1.1.0 (fNbt)
- Add NbtComparer, for comparing tags and whole trees by structure and value.
- Add a .NET 8 target alongside .NET Standard 2.0.
- Performance improvements in ZLib compression (2.5x faster), SaveToBuffer
    (up to 5x less memory allocation), and NbtCompound/NbtList creation
    (up to 25% less memory allocation).
- Fixed ReadRootTagName decompressing more of a file than it needs.
    Its bufferSize parameter turned out to be useless and is now ignored.
- When targeting .NET 8+, ZLib-compressed files now have their checksums
    validated.
- Fix missing Intellisense/XML documentation for the NuGet package.

## 1.0.0 (fNbt)
- Library now targets .NET Standard 2.0 instead of .NET Framework, which
    allows fNbt to be used in more types of projects (e.g. .NET 8 or UWP).
- Support TAG_Long_Array.
- Fix some edge-cases related to reading corrupted NBT files.
- Switch from JetBrains' annotations to .NET's built-in annotations.

## 0.6.4 (fNbt)
- Fixed a case where NbtBinaryReader.ReadString read too many bytes (#26).
- Fixed NbtList.Contains(null) throwing exception instead of returning false.
- Reduced NbtBinaryReader's maximum chunk size to 4 MB. This reduces peak
    memory use when reading huge files without affecting performance.

## 0.6.3 (fNbt)
- Empty NbtLists now allow "TAG_End" as its ListType (#12).

## 0.6.2 (fNbt)
- NbtTags now implement ICloneable and provide copy constructors (#10).
- fNbt is now compatible with /checked compiler option.
- Fixed an OverflowException in .NET 4.0+ when writing arrays of size 1 GiB
	(or larger) to a BufferedStream.
- Fixed a few edge cases in NbtReader when reading corrupt files.
- Minor optimizations and documentation fixes.

## 0.6.1 (fNbt)
- NbtReader now supports non-seekable streams.
- Fixed issues loading from/saving to non-seekable steams in NbtFile.
- NbtFile.LoadFromStream/SaveToStream now accurately report bytes read/written
    for NBT data over 2 GiB in size.
- API change:
    All NbtFile loading/saving methods now return long instead of int.

## 0.6.0 (fNbt)
- Raised .NET framework requirements from 2.0+ to 3.5+
- Added NbtWriter, for linearly writing NBT streams, similarly to XmlWriter.
    It enables high-performance writing, without creating temp NbtTag objects.
- Fixed handling of lists-of-lists and lists-of-compound-tags in NbtReader.
- Fixed being able to add an NbtList to itself.
- API changes:
    Removed NbtCompound.ToArray(), use NbtCompound.Tags.ToArray() instead.
    Removed NbtCompound.ToNameArray(), use NbtCompound.Names.ToArray() instead.
- Improved tag reading and writing performance.
- Expanded unit test coverage.

## 0.5.1 (fNbt)
- Fixed ToString() methods of NbtReader and some NbtTags not respecting the
    NbtTag.DefaultIndentString setting.
- Fixed being able to add a Compound tag to itself.
- Fixed NbtString value defaulting to null, instead of an empty string.
- Fixed a number of bugs in NbtReader.ReadListAsArray<T>().
- API additions:
    New NbtReader property:     bool IsAtStreamEnd
    New NbtReader overload:     string ToString(bool,string)
- Expanded unit test coverage.

## 0.5.0 (fNbt)
- Added NbtReader, for linearly reading NBT streams, similarly to XmlReader.
- API additions:
    New NbtCompound method:     bool TryGet(string,out NbtTag)
    New NbtCompound overload:   NbtTag Get(string)
    New NbtTag property:        bool HasValue
- License changed from LGPL to to 3-Clause BSD, since none of the original
    libnbt source code remains.

## 0.4.1 (LibNbt2012)
- Added a way to set up default indent for NbtTag.ToString() methods, using
    NbtTag.DefaultIndentString static property.
- Added a way to control/disable buffering when reading tags, using properties
    NbtFile.DefaultBufferSize (static) and "nbtFile.BufferSize" (instance).
- Simplified renaming tags. Instead of using NbtFile.RenameRootTag or
    NbtCompound.RenameTag, you can now set tag's Name property directly. It
    will check parent tag automatically, and throw ArgumentException or
    ArgumentNullException is renaming is not possible.
- NbtFile() constructor now initializes RootTag to an empty NbtCompound("").
- Added LoadFro* overloads that do not require a TagSelector parameter.

## 0.4.0 (LibNbt2012)
- Changed the way NbtFiles are constructed. Data is not loaded in the
    constructor itself any more, use LoadFrom* method.
- Added a way to load NBT data directly from byte arrays, and to save them to
    byte arrays.
- All LoadFrom-/SaveTo- methods now return an int, indicating the number of
    bytes read/written.
- Updated NbtFile to override ToString.
- Added a way to control endianness when reading/writing NBT files.

## 0.3.4 (LibNbt2012)
- Added a way to rename tags inside NbtCompound and NbtFile.

## 0.3.3 (LibNbt2012)
- Added a way to skip certain tags at load-time, using a TagSelector callback.

## 0.3.2 (LibNbt2012)
- Added a way to easily identify files, using static NbtFile.ReadRootTagName.
- Added NbtTag.Parent (automatically set/reset by NbtList and NbtCompound).
- Added NbtTag.Path (which includes parents' names, and list indices).
- Added NbtCompound.Names and NbtCompound.Values enumerators.

## 0.3.1 (LibNbt2012)
- Added indexers to NbtTag base class, to make nested compound/list tags easier
    to work with.
- Added shortcut properties for getting tag values.
- Added a ToArray<T>() overload to NbtList, to automate casting to a specific
    tag type.
- Improved .ToString() pretty-printing, now with consistent and configurable
    indentation.

## 0.3.0 (LibNbt2012)
- Auto-detection of NBT file compression.
- Loading and saving of ZLib (RFC-1950) compresessed NBT files.
- Reduced loading/saving CPU use by 15%, and memory use by 40%
- Full support for TAG_Int_Array
- NbtCompound now implements ICollection and ICollection<NbtTag>
- NbtList now implements IList and IList<NbtTag>
- More constraint checks to tag loading, modification, and saving.
- Replaced getter/setter methods with properties, wherever possible.
- Expanded unit test coverage.
- Fully documented everything.
- Made tag names immutable.
- Removed tag queries.

## 0.2.0 (libnbt)
- Implemented tag queries.
- Created unit tests for the larger portions of the code.
- Marked tag constructors that take only tag values as obsolete, use the
    constructor that has name and value instead.

## 0.1.2 (libnbt)
- Added a GetTagType() function to the tag classes.
- Fixed saving NbtList tags.

## 0.1.1 (libnbt)
- Initial release.
- Modified the tag constructors to be consistant with each other.
- Changed NbtFile to allow some functions to be overridden.

## 2.1.0 (fNbt)
- Add support for SNBT (stringified NBT), the text form Minecraft Java uses
    in commands and .snbt files. Use NbtTag.ParseSnbt to read, NbtTag.ToSnbt
    to write, and SnbtOptions to configure. Parser accepts older and modern
    Minecraft Java syntax, except \N{name} escapes. Writer uses syntax
    compatible with older Minecraft versions where possible. Mixed-type lists
    require Minecraft Java 1.21.5+ or a tool that supports them. NaN, infinity,
    and empty keys also print, but Minecraft does not read them back as the
    same values.
- Add SnbtParseException (extends NbtFormatException).
- Add NbtList.CreateMixed and UnwrapMixed for lists of mixed types, stored as
    Minecraft 1.21.5 stores them (list of compounds).
- Add NbtByte.SignedValue to get/set value as a Java-style signed byte.
- Compatibility: When loading an NBT compound with a repeated tag name, fNbt
    now uses the last loaded value in the first tag's place, matching Minecraft
    behavior. Earlier versions of fNbt rejected duplicates among loaded
    members. NbtOptions.ValidateOnRead still rejects those duplicates.
- Compatibility: You can now add a tag of any type to an empty NbtList with
    'Unknown' or 'End' ListType. First tag added sets its type; explicitly
    typed lists still enforce their type. NbtComparer treats empty lists with
    the same name as equal regardless of element type. An untyped empty
    list serializes with TAG_End as its element type instead of throwing.
- NbtReader.ReadValueAs and ReadListAsArray now throw OverflowException when
    a value does not fit the requested enum's underlying type.
- Performance: comparing parsed or cloned metadata trees is about 2x faster,
    large long arrays read about 1.4x faster on .NET 8, and fixed-width array
    reads and writes up to 2x faster on .NET Framework 4.8 in benchmarks
    against 2.0.0.

## 2.0.0 (fNbt)
- Add NbtFlavor support for Java (the default), JavaAnvil,
    JavaLegacy, JavaNetwork, Bedrock, BedrockNetwork, and ClassiCube. A flavor
    determines tag types, encodings, and limits. NbtFile, NbtReader, and
    NbtWriter accept named-root flavors; NbtCodec also accepts JavaNetwork.
    BedrockNetwork uses network varints.
- Java flavors and ClassiCube now write modified UTF-8, matching Minecraft
    Java. Every flavor reads modified and standard UTF-8. Malformed string bytes
    fail with NbtFormatException instead of decoding to U+FFFD.
- Add NbtOptions for flavor, ValidateOnWrite (default on), ValidateOnRead
    (default off), and MaxAllocation, an optional cap on any single load
    allocation. Write validation rejects documents the flavor cannot represent,
    including Bedrock documents with TAG_Long_Array or strings over 32,767
    bytes. Set ValidateOnWrite to false for permissive output. Static
    NbtOptions.Default* properties change process-wide defaults without
    affecting existing objects.
- Add NbtCodec for raw or embedded NBT. It reads one document without
    consuming trailing data and handles unnamed, non-compound, absent, and
    concatenated documents. It reads and writes streams and byte arrays, plus
    ReadOnlySpan<byte> and IBufferWriter<byte> on .NET 8.
- NbtReader.ReadValueAs<T> now does the same numeric and string conversions
    as ReadListAsArray<T>, and both read integral values or member names
    as an enum type.
- NbtCompound now guarantees insertion order. Enumeration, Names, Tags,
    ToString, and saved documents all list tags in the order they were added.
- Improve performance: repeated-name parsing allocates about half as much,
    NbtReader.Skip runs 2.5x faster with almost no allocation, selector skips
    allocate 99% less, and NbtComparer compares arrays 4x to 30x faster
    without allocating.
- Readers now accept any element type for empty lists and treat negative list
    and array lengths as empty.
- Most invalid NbtWriter writes are rejected before emitting data, leaving the
    writer usable. If a write fails after output begins, IsInErrorState reports
    that the output is incomplete and should be discarded. Every later call
    throws. The underlying stream remains accessible through BaseStream.
- Fix NbtWriter.WriteIntArray and WriteLongArray with a non-zero offset.
- Compressed loads now verify checksums for every stream type and target,
    including .NET Standard 2.0. Seekable compressed loads now leave the source
    at its end. The returned byte count covers everything read.
- Fix NbtReader.ReadListAsArray when called on a list element, stale ReadValue
    results, and ReadAsTag after a cached read.
- Add NbtReader.LongTagStartOffset for documents past 2 GiB. TagStartOffset now
    throws OverflowException instead of wrapping.
- Add NbtFile constructors that take NbtFlavor or NbtOptions. Flavor is fixed at
    construction. To re-save under another flavor, create a new NbtFile over
    the same RootTag. BigEndian and BigEndianByDefault are now obsolete,
    read-only properties.
- The bool NbtReader and NbtWriter constructors and bool ReadRootTagName
    overloads are now obsolete but still work. Their parameters map true to
    Java and false to Bedrock.
- Set AssemblyVersion to 2.0.0.0 for all 2.x releases.

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
- Fixed issues loading from/saving to non-seekable streams in NbtFile.
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
- License changed from LGPL to 3-Clause BSD, since none of the original
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
- Loading and saving of ZLib (RFC-1950) compressed NBT files.
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
- Modified the tag constructors to be consistent with each other.
- Changed NbtFile to allow some functions to be overridden.

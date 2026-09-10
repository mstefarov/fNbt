# fNbt

![Build Status](https://github.com/mstefarov/fNbt/actions/workflows/dotnet.yml/badge.svg)

[Named Binary Tag (NBT)](https://minecraft.gamepedia.com/NBT_format) is a structured binary file format used by Minecraft.
fNbt is a small library, written in C#. It provides functionality
to create, load, traverse, modify, and save NBT files and streams, in every encoding that
Minecraft Java, Minecraft Bedrock, and ClassiCube use.
The library provides a choice of convenient high-level APIs (NbtFile/NbtTag) that present an object model,
or lower-level higher-performance APIs (NbtReader/NbtWriter) that read/write data directly to/from streams.

Current released version is 2.1.0 (9 September 2026).

fNbt is based in part on Erik Davidson's (aphistic's) original LibNbt library,
now completely rewritten by Matvei Stefarov (fragmer).


## FEATURES
- Load and save uncompressed, GZip-, and ZLib-compressed files/streams.
- Easily create, traverse, and modify NBT documents.
- Simple indexer-based syntax for accessing compound, list, and nested tags.
- Shortcut properties to access tags' values without unnecessary type casts.
- Compound tags implement `ICollection<NbtTag>` and List tags implement `IList<NbtTag>`, for easy traversal and LINQ integration.
- Good performance and low memory overhead.
- Built-in pretty-printing of individual tags or whole files.
- Converts to and from SNBT, the text form Minecraft Java uses in commands and `.snbt` files.
- Every class and method is fully documented, annotated, and unit-tested.
- Supports every NBT flavor: Java Edition files and network packets, Bedrock Edition files and
  network packets (varint encoding), and ClassiCube maps.
- Works with raw NBT that is not a file (packet payloads, LevelDB values, embedded blobs) via NbtCodec.
- Validates on write, so a document the target flavor cannot represent is refused instead of written.
- Optional high-performance reader/writer for working with streams directly.


## DOWNLOAD
Latest version of fNbt targets [.NET Standard 2.0](https://learn.microsoft.com/en-us/dotnet/standard/net-standard?tabs=net-standard-2-0)
and .NET 8. The .NET Standard build works on .NET Framework 4.6.1+, .NET Core 2.0+, Mono 5.4+, and more.
Projects on .NET 8 or newer automatically get a build with extra performance optimizations.

- **Package @ NuGet:**  https://www.nuget.org/packages/fNbt/

- **Release notes:**  https://github.com/mstefarov/fNbt/releases


## EXAMPLES
#### Loading a gzipped file
```cs
    var myFile = new NbtFile();
    myFile.LoadFromFile("somefile.nbt.gz");
    var myCompoundTag = myFile.RootTag;
```

#### Loading a Bedrock Edition file
```cs
    var structure = new NbtFile(NbtFlavor.Bedrock);
    structure.LoadFromFile("house.mcstructure");
```

#### Reading raw NBT (packet payloads, LevelDB values, embedded blobs)
```cs
    NbtCodec codec = NbtCodec.For(NbtFlavor.JavaNetwork);
    NbtTag root = codec.ReadTag(payload, 0, payload.Length, out int bytesConsumed);
```

#### Accessing tags (long/strongly-typed style)
```cs
    int intVal = myCompoundTag.Get<NbtInt>("intTagsName")!.Value;
    string listItem = myStringList.Get<NbtString>(0).Value;
    byte nestedVal = myCompTag.Get<NbtCompound>("nestedTag")!
                              .Get<NbtByte>("someByteTag")!
                              .Value;
```

#### Accessing tags (shortcut style)
```cs
    int intVal = myCompoundTag["intTagsName"]!.IntValue;
    string listItem = myStringList[0].StringValue;
    byte nestedVal = myCompTag["nestedTag"]!["someByteTag"]!.ByteValue;
```
Looking a tag up by name returns `null` when it is missing, hence the `!` above. `TryGet` is the checked
alternative, and casts at the same time.

#### Iterating over all tags in a compound/list
```cs
    foreach( NbtTag tag in myCompoundTag.Tags ){
        Console.WriteLine( tag.Name + " = " + tag.TagType );
    }
    foreach( string tagName in myCompoundTag.Names ){
        Console.WriteLine( tagName );
    }
    for( int i = 0; i < myListTag.Count; i++ ){
        Console.WriteLine( myListTag[i] );
    }
    foreach( NbtInt intItem in myIntList.ToArray<NbtInt>() ){
        Console.WriteLine( intItem.Value );
    }
```

#### Constructing a new document
```cs
    var serverInfo = new NbtCompound("Server");
    serverInfo.Add( new NbtString("Name", "BestServerEver") );
    serverInfo.Add( new NbtInt("Players", 15) );
    serverInfo.Add( new NbtInt("MaxPlayers", 20) );
    var serverFile = new NbtFile(serverInfo);
    serverFile.SaveToFile( "server.nbt", NbtCompression.None );
```

### Writing to stream directly using NbtWriter
```cs
using (var fileStream = File.Create("foo.nbt", bufferSize: 4 * 1024)) {
    var writer = new NbtWriter(fileStream, "Server");
    writer.WriteString("Name", "BestServerEver");
    writer.WriteInt("Players", 15);
    writer.WriteInt("MaxPlayers", 20);
    writer.EndCompound();
    writer.Finish();
}
```

#### Constructing using collection initializer notation
```cs
    var compound = new NbtCompound("root"){
        new NbtInt("someInt", 123),
        new NbtList("byteList") {
            new NbtByte(1),
            new NbtByte(2),
            new NbtByte(3)
        },
        new NbtCompound("nestedCompound") {
            new NbtDouble("pi", 3.14)
        }
    };
```

#### Pretty-printing file structure
```cs
    Console.WriteLine( myFile.ToString("\t") ); // tabs
    Console.WriteLine( myRandomTag.ToString("    ") ); // spaces
```

#### Converting to and from SNBT (stringified NBT)
```cs
    NbtTag tag = NbtTag.ParseSnbt("{Name:\"Steve\",Health:20.0f,Tags:[\"a\",\"b\"]}");
    string compact = tag.ToSnbt();   // {Name:"Steve",Health:20.0f,Tags:["a","b"]}
    string indented = tag.ToSnbt(new SnbtOptions { WriteLayout = SnbtLayout.Indented });

    // An .snbt file holds one compound; give it a name to save it as a regular NBT file
    var root = (NbtCompound)NbtTag.ParseSnbt(File.ReadAllText("structure.snbt"));
    root.Name = "";
    new NbtFile(root).SaveToFile("structure.nbt", NbtCompression.GZip);

    // Minecraft stores a list of mixed types as compounds with each value under an empty key.
    // CreateMixed builds that form and UnwrapMixed reads through it.
    NbtList line = NbtList.CreateMixed(new NbtString("Hello "), new NbtCompound { new NbtString("text", "world") });
    string snbt = line.ToSnbt();                                      // ["Hello ",{text:"world"}]
    NbtTag[] parts = ((NbtList)NbtTag.ParseSnbt(snbt)).UnwrapMixed(); // NbtString, NbtCompound
```

#### Check out unit tests in fNbt.Test for more examples.


## API REFERENCE
Online reference can be found at https://fcraft.net/fnbt/v2.1.0/


## LICENSING
fNbt v0.5.0+ is licensed under 3-Clause BSD license; see [docs/LICENSE.txt](https://github.com/mstefarov/fNbt/blob/master/docs/LICENSE.txt).
LibNbt2012 up to and including v0.4.1 kept LibNbt's original license (LGPLv3).


## VERSION HISTORY
See [docs/Changelog.md](https://github.com/mstefarov/fNbt/blob/master/docs/Changelog.md)

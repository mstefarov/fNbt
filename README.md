[![Build status](https://ci.appveyor.com/api/projects/status/vcdkhya4u6h26qr2/branch/master?svg=true)](https://ci.appveyor.com/project/fragmer/fnbt/branch/master)

[Named Binary Tag (NBT)](https://minecraft.gamepedia.com/NBT_format) is a structured binary file format used by Minecraft.
fNbt is a small library, written in C#. It provides functionality
to create, load, traverse, modify, and save NBT files and streams.
The library provides a choice of convenient high-level APIs (NbtFile/NbtTag) that present an object model,
or lower-level higher-performance APIs (NbtReader/NbtWriter) that read/write data directly to/from streams.

Current released version is 1.0.0 (3 July 2025).

fNbt is based in part on Erik Davidson's (aphistic's) original LibNbt library,
now completely rewritten by Matvei Stefarov (fragmer).


## FEATURES
- Load and save uncompressed, GZip-, and ZLib-compressed files/streams.
- Easily create, traverse, and modify NBT documents.
- Simple indexer-based syntax for accessing compound, list, and nested tags.
- Shortcut properties to access tags' values without unnecessary type casts.
- Compound tags implement `ICollection<T>` and List tags implement `IList<T>`, for easy traversal and LINQ integration.
- Good performance and low memory overhead.
- Built-in pretty-printing of individual tags or whole files.
- Every class and method are fully documented, annotated, and unit-tested.
- Can work with both big-endian and little-endian NBT data and systems.
- Optional high-performance reader/writer for working with streams directly.


## DOWNLOAD
Latest version of fNbt targets [.NET Standard 2.0](https://learn.microsoft.com/en-us/dotnet/standard/net-standard?tabs=net-standard-2-0),
which means it can be used in .NET Framework 4.6.1+, .NET Core 2.0+, Mono 5.4+, and more.

- **Package @ NuGet:**  https://www.nuget.org/packages/fNbt/

- **Compiled binaries and single-source-file amalgamations:**  https://github.com/mstefarov/fNbt/releases


## EXAMPLES
#### Loading a gzipped file
```cs
    var myFile = new NbtFile();
    myFile.LoadFromFile("somefile.nbt.gz");
    var myCompoundTag = myFile.RootTag;
```

#### Accessing tags (long/strongly-typed style)
```cs
    int intVal = myCompoundTag.Get<NbtInt>("intTagsName").Value;
    string listItem = myStringList.Get<NbtString>(0).Value;
    byte nestedVal = myCompTag.Get<NbtCompound>("nestedTag")
                              .Get<NbtByte>("someByteTag")
                              .Value;
```

#### Accessing tags (shortcut style)
```cs
    int intVal = myCompoundTag["intTagsName"].IntValue;
    string listItem = myStringList[0].StringValue;
    byte nestedVal = myCompTag["nestedTag"]["someByteTag"].ByteValue;
```

#### Iterating over all tags in a compound/list
```cs
    foreach( NbtTag tag in myCompoundTag.Values ){
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
```
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

#### Check out unit tests in fNbt.Test for more examples.


## API REFERENCE
Online reference can be found at http://www.fcraft.net/fnbt/v1.0.0/


## LICENSING
fNbt v0.5.0+ is licensed under 3-Clause BSD license; see [docs/LICENSE.txt](docs/LICENSE.txt).
LibNbt2012 up to and including v0.4.1 kept LibNbt's original license (LGPLv3).


## VERSION HISTORY
See [docs/Changelog.md](docs/Changelog.md)

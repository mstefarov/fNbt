# Real-world SNBT documents

Each `<name>.snbt` is a document as published by its project. Its `<name>.game.snbt` twin is
Minecraft Java 26.2's own compact printout of that document (`Tag.toString()` after
`TagParser.parseFully`), produced by the research harness in `tools/snbt-harness` on 2026-09-06;
characters outside printable ASCII in a printout are written as `\uXXXX` escapes, which SNBT reads.
A document without a twin is one that every Minecraft version refuses.

| Files | Source | Licence |
|---|---|---|
| `adventure-bigtest.snbt` | KyoriPowered/adventure, `nbt/src/test/resources/bigtest.snbt` | MIT, Copyright (c) 2017-2025 KyoriPowered |
| `gomc-dimension_codec.snbt`, `gomc-level.dat.snbt`, `gomc-player.dat.snbt` | Tnze/go-mc, `nbt/testdata/` | MIT, Copyright (c) 2019 Tnze |
| `nbtify-*.snbt` | Offroaders123/NBTify, `test/nbt/` | MIT, Copyright (c) 2024 Brandon Bennett |
| `renderer-*.snbt` | ptlthg/MinecraftRenderer, `snbt-test-debug/` | MIT, Copyright (c) 2025 Kaeso |
| `fnbt-bigtest.snbt` | fNbt's own compact printout of `../bigtest.nbt` | this repository |

MIT terms for the four projects above: permission is hereby granted, free of charge, to any person
obtaining a copy of this software and associated documentation files, to deal in the software
without restriction, subject to including the above copyright notices and this permission notice;
the software is provided "as is", without warranty of any kind.

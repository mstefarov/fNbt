using System;
using System.IO;
using System.Linq;

namespace fNbt.Test {
    // Java flavors write modified UTF-8 (CESU-8 astral pairs, overlong NUL, lone surrogates
    // preserved); Bedrock flavors write standard UTF-8. Reads are lenient on every flavor:
    // both encodings are accepted, and truly malformed data throws instead of silently
    // turning into U+FFFD.
    [TestClass]
    public class ModifiedUtf8Tests {
        const string Emoji = "\U0001F600"; // U+1F600 GRINNING FACE, UTF-16 surrogates D83D DE00

        static readonly byte[] EmojiCesu8 = { 0xED, 0xA0, 0xBD, 0xED, 0xB8, 0x80 };
        static readonly byte[] EmojiUtf8 = { 0xF0, 0x9F, 0x98, 0x80 };


        // Document: TAG_Compound "" { TAG_String "s" = value }
        static byte[] StringDoc(NbtFlavor flavor, string value) {
            var codec = new NbtCodec(new NbtOptions { Flavor = flavor, ValidateOnWrite = false });
            return codec.WriteTag(new NbtCompound("") { new NbtString("s", value) });
        }


        // Layout: 0A <nameLen:2><> 08 <nameLen:2> 's' <strLen:2> <payload> 00
        static byte[] StringPayload(byte[] doc, bool bigEndian) {
            int length = bigEndian
                ? (doc[7] << 8) | doc[8]
                : doc[7] | (doc[8] << 8);
            return doc.Skip(9).Take(length).ToArray();
        }


        // Hand-builds the same document shape with an arbitrary payload, big-endian
        static byte[] MakeJavaStringDoc(byte[] payload) {
            using (var ms = new MemoryStream()) {
                ms.WriteByte(0x0A);
                TestFiles.WriteBEShort(ms, 0);
                ms.WriteByte(0x08);
                TestFiles.WriteBEShort(ms, 1);
                ms.WriteByte((byte)'s');
                TestFiles.WriteBEShort(ms, (short)payload.Length);
                ms.Write(payload, 0, payload.Length);
                ms.WriteByte(0x00);
                return ms.ToArray();
            }
        }


        static string ReadStringDoc(NbtFlavor flavor, byte[] doc) {
            return NbtCodec.For(flavor).ReadTag(doc, 0, doc.Length, out _)["s"].StringValue;
        }


        [TestMethod]
        public void FlavorsDeclareStringEncoding() {
            Assert.IsTrue(NbtFlavor.Java.UsesModifiedUtf8);
            Assert.IsTrue(NbtFlavor.JavaAnvil.UsesModifiedUtf8);
            Assert.IsTrue(NbtFlavor.JavaLegacy.UsesModifiedUtf8);
            Assert.IsTrue(NbtFlavor.JavaNetwork.UsesModifiedUtf8);
            Assert.IsTrue(NbtFlavor.ClassiCube.UsesModifiedUtf8);
            Assert.IsFalse(NbtFlavor.Bedrock.UsesModifiedUtf8);
            Assert.IsFalse(NbtFlavor.BedrockNetwork.UsesModifiedUtf8);
        }


        [TestMethod]
        public void JavaWritesAstralAsCesu8() {
            byte[] doc = StringDoc(NbtFlavor.Java, Emoji);
            CollectionAssert.AreEqual(EmojiCesu8, StringPayload(doc, true));
            Assert.AreEqual(Emoji, ReadStringDoc(NbtFlavor.Java, doc));
        }


        [TestMethod]
        public void BedrockWritesAstralAsUtf8() {
            byte[] doc = StringDoc(NbtFlavor.Bedrock, Emoji);
            CollectionAssert.AreEqual(EmojiUtf8, StringPayload(doc, false));
            Assert.AreEqual(Emoji, ReadStringDoc(NbtFlavor.Bedrock, doc));
        }


        [TestMethod]
        public void JavaReadAcceptsStandardUtf8Astral() {
            // Not something vanilla Java writes, but reads are generous
            byte[] doc = MakeJavaStringDoc(EmojiUtf8);
            Assert.AreEqual(Emoji, ReadStringDoc(NbtFlavor.Java, doc));
        }


        [TestMethod]
        public void EmbeddedNulRoundTrips() {
            byte[] javaDoc = StringDoc(NbtFlavor.Java, "a\0b");
            CollectionAssert.AreEqual(new byte[] { 0x61, 0xC0, 0x80, 0x62 }, StringPayload(javaDoc, true));
            Assert.AreEqual("a\0b", ReadStringDoc(NbtFlavor.Java, javaDoc));

            byte[] bedrockDoc = StringDoc(NbtFlavor.Bedrock, "a\0b");
            CollectionAssert.AreEqual(new byte[] { 0x61, 0x00, 0x62 }, StringPayload(bedrockDoc, false));
            Assert.AreEqual("a\0b", ReadStringDoc(NbtFlavor.Bedrock, bedrockDoc));

            // A raw 00 byte inside a Java string also reads fine
            byte[] rawNul = MakeJavaStringDoc(new byte[] { 0x61, 0x00, 0x62 });
            Assert.AreEqual("a\0b", ReadStringDoc(NbtFlavor.Java, rawNul));
        }


        [TestMethod]
        public void LoneSurrogatePerFlavor() {
            // Java preserves it, matching writeUTF
            byte[] javaDoc = StringDoc(NbtFlavor.Java, "\uD800");
            CollectionAssert.AreEqual(new byte[] { 0xED, 0xA0, 0x80 }, StringPayload(javaDoc, true));
            Assert.AreEqual("\uD800", ReadStringDoc(NbtFlavor.Java, javaDoc));

            // Standard UTF-8 cannot represent it; Bedrock refuses with a documented exception
            Assert.Throws<NbtFormatException>(() => StringDoc(NbtFlavor.Bedrock, "\uD800"));
        }


        [TestMethod]
        public void MalformedStringDataThrowsInsteadOfReplacing() {
            // Each of these used to load as U+FFFD replacement characters
            Assert.Throws<NbtFormatException>(
                () => ReadStringDoc(NbtFlavor.Java, MakeJavaStringDoc(new byte[] { 0xFF })));
            Assert.Throws<NbtFormatException>(
                () => ReadStringDoc(NbtFlavor.Java, MakeJavaStringDoc(new byte[] { 0xED, 0xA0 })));
            Assert.Throws<NbtFormatException>(
                () => ReadStringDoc(NbtFlavor.Java, MakeJavaStringDoc(new byte[] { 0xC0, 0x41 })));
            Assert.Throws<NbtFormatException>(
                () => ReadStringDoc(NbtFlavor.Bedrock, MakeJavaStringDoc(new byte[] { 0x80, 0x80 })
                    .Select(FlipDocEndianness).ToArray()));
        }


        // The malformed-payload doc builder writes big-endian; for the Bedrock case just flip
        // the length fields to little-endian (root name 0, tag name 1, string length 2).
        static byte FlipDocEndianness(byte b, int index) {
            return index switch {
                4 => 1, 5 => 0, // tag name length 1, LE
                7 => 2, 8 => 0, // string length 2, LE
                _ => b,
            };
        }


        [TestMethod]
        public void JavaCeilingCountsModifiedUtf8Bytes() {
            // 10,922 astral chars are 65,532 modified-UTF-8 bytes: fits.
            // 10,923 are 65,538: over the u16 prefix, must throw.
            // (Standard UTF-8 would count only 43,692 bytes and let it through, corrupting the stream.)
            string fits = string.Concat(Enumerable.Repeat(Emoji, 10_922));
            string over = string.Concat(Enumerable.Repeat(Emoji, 10_923));

            byte[] doc = StringDoc(NbtFlavor.Java, fits);
            Assert.AreEqual(fits, ReadStringDoc(NbtFlavor.Java, doc));

            Assert.Throws<NbtFormatException>(() => StringDoc(NbtFlavor.Java, over));
        }


        [TestMethod]
        public void StreamingWriterHandlesLongAstralStrings() {
            // 300 emoji = 600 chars: exercises the chunked slow path past the 256-byte buffer
            string longEmoji = string.Concat(Enumerable.Repeat(Emoji, 300));

            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "", NbtFlavor.Java);
                writer.WriteString("s", longEmoji);
                writer.EndCompound();
                writer.Finish();

                byte[] doc = ms.ToArray();
                Assert.AreEqual(longEmoji, ReadStringDoc(NbtFlavor.Java, doc));
            }
        }


        [TestMethod]
        public void CleanStringsAreByteIdenticalToBefore() {
            // ASCII and BMP text ("Hello, Mir!" in Cyrillic plus box-drawing characters)
            // never leaves the old encoding path
            const string cleanBmp = "Hello, \u041C\u0438\u0440! \u250C\u2500\u2510";
            byte[] doc = StringDoc(NbtFlavor.Java, cleanBmp);
            byte[] expected = new UTF8EncodingCheck().GetBytes(cleanBmp);
            CollectionAssert.AreEqual(expected, StringPayload(doc, true));
        }


        [TestMethod]
        public void LenientDecodeHandlesAstralSequences() {
            // The overlong NUL forces the lenient path; the standard 4-byte astral sequence
            // must decode to a surrogate pair alongside it
            byte[] bytes = { 0xC0, 0x80, 0xF0, 0x9F, 0x98, 0x80 };
            Assert.AreEqual("\0" + Emoji, NbtStringCodec.Decode(bytes, 0, bytes.Length));
        }


        [TestMethod]
        public void LenientDecodeRejectsMalformedData() {
            // The overlong NUL forces the lenient path; each tail is invalid there
            byte[][] cases = {
                new byte[] { 0xC0, 0x80, 0xF8 },                   // 5-byte sequence lead
                new byte[] { 0xC0, 0x80, 0xFF },                   // invalid lead byte
                new byte[] { 0xC0, 0x80, 0xF0, 0x80, 0x80, 0x80 }, // overlong 4-byte sequence
                new byte[] { 0xC0, 0x80, 0xF4, 0x90, 0x80, 0x80 }, // above U+10FFFF
                new byte[] { 0xC0, 0x80, 0xF0, 0x9F, 0x98 },       // truncated 4-byte sequence
                new byte[] { 0xC0, 0x80, 0xF0, 0x9F, 0x28, 0x80 }, // broken continuation byte
            };
            foreach (byte[] data in cases) {
                Assert.Throws<NbtFormatException>(
                    () => NbtStringCodec.Decode(data, 0, data.Length),
                    BitConverter.ToString(data));
            }
        }


        // Trivial wrapper so the test does not depend on Encoding.UTF8's replacement settings
        sealed class UTF8EncodingCheck : System.Text.UTF8Encoding {
            public UTF8EncodingCheck() : base(false, true) { }
        }
    }
}

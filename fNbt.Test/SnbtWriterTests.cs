using System;
using System.Globalization;
using System.Threading;

namespace fNbt.Test {
    // NbtTag.ToSnbt: the fixed output shape (Minecraft's own compact form apart from key order
    // and the quote choice),
    // number spelling on both targets, the three layouts, wrapper unwrapping, and the depth cap.
    // Expected texts come from running Minecraft 1.21.4 and 26.2's own printers (tools/snbt-harness).
    [TestClass]
    public class SnbtWriterTests {
        static string Snbt(NbtTag tag, SnbtLayout layout = SnbtLayout.Compact) {
            return tag.ToSnbt(new SnbtOptions { WriteLayout = layout });
        }


        [TestMethod]
        public void IntegersCarryJavaSuffixesAndBytesAreSigned() {
            Assert.AreEqual("0b", Snbt(new NbtByte(0)));
            Assert.AreEqual("127b", Snbt(new NbtByte(127)));
            Assert.AreEqual("-128b", Snbt(new NbtByte(128)));
            Assert.AreEqual("-1b", Snbt(new NbtByte(255)));
            Assert.AreEqual("-106b", Snbt(new NbtByte { SignedValue = -106 }));
            Assert.AreEqual("-32768s", Snbt(new NbtShort(short.MinValue)));
            Assert.AreEqual("32767s", Snbt(new NbtShort(short.MaxValue)));
            Assert.AreEqual("-2147483648", Snbt(new NbtInt(int.MinValue)));
            Assert.AreEqual("2147483647", Snbt(new NbtInt(int.MaxValue)));
            Assert.AreEqual("-9223372036854775808L", Snbt(new NbtLong(long.MinValue)));
            Assert.AreEqual("9223372036854775807L", Snbt(new NbtLong(long.MaxValue)));
        }


        [TestMethod]
        public void FloatsUseJavaLayout() {
            Assert.AreEqual("0.0f", Snbt(new NbtFloat(0)));
            Assert.AreEqual("1.0f", Snbt(new NbtFloat(1)));
            Assert.AreEqual("-1.0f", Snbt(new NbtFloat(-1)));
            Assert.AreEqual("0.1f", Snbt(new NbtFloat(0.1f)));
            Assert.AreEqual("0.5f", Snbt(new NbtFloat(0.5f)));
            Assert.AreEqual("100.0f", Snbt(new NbtFloat(100)));
            Assert.AreEqual("1234567.0f", Snbt(new NbtFloat(1234567)));
            Assert.AreEqual("9999999.0f", Snbt(new NbtFloat(9999999)));
            Assert.AreEqual("1.0E7f", Snbt(new NbtFloat(1e7f)));
            Assert.AreEqual("1.2345678E7f", Snbt(new NbtFloat(12345678)));
            Assert.AreEqual("1.6777216E7f", Snbt(new NbtFloat(16777217)));
            Assert.AreEqual("65536.0f", Snbt(new NbtFloat(65536)));
            Assert.AreEqual("0.001f", Snbt(new NbtFloat(0.001f)));
            Assert.AreEqual("0.002f", Snbt(new NbtFloat(0.002f)));
            Assert.AreEqual("1.0E-4f", Snbt(new NbtFloat(1e-4f)));
            Assert.AreEqual("1.0E-5f", Snbt(new NbtFloat(1e-5f)));
            Assert.AreEqual("123456.79f", Snbt(new NbtFloat(123456.79f)));
            Assert.AreEqual("3.1415927f", Snbt(new NbtFloat(3.1415927f)));
            Assert.AreEqual("1.1f", Snbt(new NbtFloat(1.1f)));
            Assert.AreEqual("0.3f", Snbt(new NbtFloat(0.3f)));
            Assert.AreEqual("1.0E10f", Snbt(new NbtFloat(1e10f)));
            Assert.AreEqual("3.4028235E38f", Snbt(new NbtFloat(float.MaxValue)));
            Assert.AreEqual("-3.4028235E38f", Snbt(new NbtFloat(float.MinValue)));
            Assert.AreEqual("1.4E-45f", Snbt(new NbtFloat(float.Epsilon)));
            // Floats that need nine digits
            Assert.AreEqual("115527.086f", Snbt(new NbtFloat(115527.086f)));
            Assert.AreEqual("-103.217316f", Snbt(new NbtFloat(-103.217316f)));
            Assert.AreEqual("1.00584066E18f", Snbt(new NbtFloat(1.00584066E18f)));
            Assert.AreEqual("NaNf", Snbt(new NbtFloat(float.NaN)));
            Assert.AreEqual("Infinityf", Snbt(new NbtFloat(float.PositiveInfinity)));
            Assert.AreEqual("-Infinityf", Snbt(new NbtFloat(float.NegativeInfinity)));
            Assert.AreEqual("-0.0f", Snbt(new NbtFloat(-0.0f)));
#if NETCOREAPP
            // .NET Framework prints this with a few more digits; both parse back exactly
            Assert.AreEqual("1.1754944E-38f", Snbt(new NbtFloat(1.17549435E-38f)));
#endif
        }


        [TestMethod]
        public void DoublesUseJavaLayout() {
            Assert.AreEqual("0.0d", Snbt(new NbtDouble(0)));
            Assert.AreEqual("1.0d", Snbt(new NbtDouble(1)));
            Assert.AreEqual("0.1d", Snbt(new NbtDouble(0.1)));
            Assert.AreEqual("1.5d", Snbt(new NbtDouble(1.5)));
            Assert.AreEqual("100.0d", Snbt(new NbtDouble(100)));
            Assert.AreEqual("1234567.0d", Snbt(new NbtDouble(1234567)));
            Assert.AreEqual("9999999.0d", Snbt(new NbtDouble(9999999)));
            Assert.AreEqual("1.0E7d", Snbt(new NbtDouble(1e7)));
            Assert.AreEqual("0.001d", Snbt(new NbtDouble(0.001)));
            Assert.AreEqual("1.0E-4d", Snbt(new NbtDouble(1e-4)));
            Assert.AreEqual("1.0E-7d", Snbt(new NbtDouble(1e-7)));
            Assert.AreEqual("2.0E-5d", Snbt(new NbtDouble(2e-5)));
            Assert.AreEqual("1.23456789E8d", Snbt(new NbtDouble(123456789)));
            Assert.AreEqual("1.23456789E7d", Snbt(new NbtDouble(12345678.9)));
            Assert.AreEqual("1.23456789012E11d", Snbt(new NbtDouble(123456789012)));
            Assert.AreEqual("0.30000000000000004d", Snbt(new NbtDouble(0.1 + 0.2)));
            Assert.AreEqual("3.141592653589793d", Snbt(new NbtDouble(Math.PI)));
            Assert.AreEqual("1.0000000000000002d", Snbt(new NbtDouble(1.0000000000000002)));
            Assert.AreEqual("0.3333333333333333d", Snbt(new NbtDouble(1.0 / 3)));
            Assert.AreEqual("1.0E15d", Snbt(new NbtDouble(1e15)));
            Assert.AreEqual("1.0E16d", Snbt(new NbtDouble(1e16)));
            Assert.AreEqual("9.007199254740992E15d", Snbt(new NbtDouble(9007199254740992)));
            Assert.AreEqual("1.0E21d", Snbt(new NbtDouble(1e21)));
            Assert.AreEqual("1.0E23d", Snbt(new NbtDouble(1e23)));
            Assert.AreEqual("1.7976931348623157E308d", Snbt(new NbtDouble(double.MaxValue)));
            Assert.AreEqual("-1.7976931348623157E308d", Snbt(new NbtDouble(double.MinValue)));
            Assert.AreEqual("4.9E-324d", Snbt(new NbtDouble(double.Epsilon)));
            Assert.AreEqual("2.2250738585072014E-308d", Snbt(new NbtDouble(2.2250738585072014E-308)));
            // 17 digits: the 16-digit text would name the value below it
            Assert.AreEqual("0.056526799757396023d", Snbt(new NbtDouble(BitConverter.Int64BitsToDouble(4588307191414157804))));
            Assert.AreEqual("NaNd", Snbt(new NbtDouble(double.NaN)));
            Assert.AreEqual("Infinityd", Snbt(new NbtDouble(double.PositiveInfinity)));
            Assert.AreEqual("-Infinityd", Snbt(new NbtDouble(double.NegativeInfinity)));
            Assert.AreEqual("-0.0d", Snbt(new NbtDouble(-0.0)));
        }


        [TestMethod]
        public void NumberTextIsCultureIndependent() {
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;
            try {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                Assert.AreEqual("{a:1.5f,b:-2.5d,c:-1000000,d:[I;-1,2]}", Snbt(new NbtCompound {
                    new NbtFloat("a", 1.5f),
                    new NbtDouble("b", -2.5),
                    new NbtInt("c", -1000000),
                    new NbtIntArray("d", new[] { -1, 2 })
                }));
            } finally {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }


        [TestMethod]
        public void StringsAreAlwaysDoubleQuoted() {
            Assert.AreEqual("\"\"", Snbt(new NbtString("")));
            Assert.AreEqual("\"a\"", Snbt(new NbtString("a")));
            Assert.AreEqual("\"a b\"", Snbt(new NbtString("a b")));
            // Even where Minecraft's own printer would switch to single quotes, which 1.12 and
            // 1.13 cannot read
            Assert.AreEqual("\"a\\\"b\"", Snbt(new NbtString("a\"b")));
            Assert.AreEqual("\"a'b\"", Snbt(new NbtString("a'b")));
            Assert.AreEqual("\"a\\\"b'c\"", Snbt(new NbtString("a\"b'c")));
            Assert.AreEqual("\"a'b\\\"c\"", Snbt(new NbtString("a'b\"c")));
            Assert.AreEqual("\"\\\"\"", Snbt(new NbtString("\"")));
            Assert.AreEqual("\"'\"", Snbt(new NbtString("'")));
            Assert.AreEqual("\"\\\\\"", Snbt(new NbtString("\\")));
            Assert.AreEqual("\"a\\\\b\"", Snbt(new NbtString("a\\b")));
            // Strings that look like something else stay strings by being quoted
            Assert.AreEqual("\"true\"", Snbt(new NbtString("true")));
            Assert.AreEqual("\"1\"", Snbt(new NbtString("1")));
            Assert.AreEqual("\"1b\"", Snbt(new NbtString("1b")));
            // Control characters and non-ASCII go out raw
            Assert.AreEqual("\"a\nb\tc\u0001d\u007f\"", Snbt(new NbtString("a\nb\tc\u0001d\u007f")));
            Assert.AreEqual("\"\u00e9\u2603\ud83d\ude00\"", Snbt(new NbtString("\u00e9\u2603\ud83d\ude00")));
            Assert.AreEqual("\"\ud800\"", Snbt(new NbtString("\ud800")));
        }


        [TestMethod]
        public void KeysAreBareOnlyWhenNoParserCouldMisreadThem() {
            Assert.AreEqual("{a:1,a1:1,_a:1,a-b:1,a.b:1,a+b:1,.a:1,A:1,Z9:1}", Snbt(new NbtCompound {
                new NbtInt("a", 1), new NbtInt("a1", 1), new NbtInt("_a", 1), new NbtInt("a-b", 1),
                new NbtInt("a.b", 1), new NbtInt("a+b", 1), new NbtInt(".a", 1), new NbtInt("A", 1),
                new NbtInt("Z9", 1)
            }));
            Assert.AreEqual("{\"1\":1,\"1a\":1,\"-a\":1,\"+a\":1,\"0\":1,\"true\":1,\"FALSE\":1,\"\":1}",
                            Snbt(new NbtCompound {
                                new NbtInt("1", 1), new NbtInt("1a", 1), new NbtInt("-a", 1), new NbtInt("+a", 1),
                                new NbtInt("0", 1), new NbtInt("true", 1), new NbtInt("FALSE", 1), new NbtInt("", 1)
                            }));
            Assert.AreEqual("{\"a b\":1,\"a\\\"b\":1,\"a'b\":1,\"\u00e9\":1,\"a:b\":1,\"a\\\\b\":1,\"a\nb\":1}",
                            Snbt(new NbtCompound {
                                new NbtInt("a b", 1), new NbtInt("a\"b", 1), new NbtInt("a'b", 1),
                                new NbtInt("\u00e9", 1), new NbtInt("a:b", 1), new NbtInt("a\\b", 1),
                                new NbtInt("a\nb", 1)
                            }));
        }


        [TestMethod]
        public void ArraysCarryPrefixesAndElementSuffixes() {
            Assert.AreEqual("[B;]", Snbt(new NbtByteArray(new byte[0])));
            Assert.AreEqual("[I;]", Snbt(new NbtIntArray(new int[0])));
            Assert.AreEqual("[L;]", Snbt(new NbtLongArray(new long[0])));
            Assert.AreEqual("[B;-1B,0B,127B]", Snbt(new NbtByteArray(new byte[] { 255, 0, 127 })));
            Assert.AreEqual("[I;-2147483648,1]", Snbt(new NbtIntArray(new[] { int.MinValue, 1 })));
            Assert.AreEqual("[L;-9223372036854775808L,1L]", Snbt(new NbtLongArray(new[] { long.MinValue, 1L })));
            Assert.AreEqual("[B; 1B, 2B]", Snbt(new NbtByteArray(new byte[] { 1, 2 }), SnbtLayout.Spaced));
            Assert.AreEqual("[B; 1B, 2B]", Snbt(new NbtByteArray(new byte[] { 1, 2 }), SnbtLayout.Indented));
            Assert.AreEqual("[B;]", Snbt(new NbtByteArray(new byte[0]), SnbtLayout.Indented));
        }


        static NbtCompound MakeLayoutSample() {
            return new NbtCompound("ignored") {
                new NbtInt("a", 1),
                new NbtList("b", new NbtTag[] { new NbtInt(1), new NbtInt(2) }),
                new NbtByteArray("c", new byte[] { 1, 2 }),
                new NbtCompound("d"),
                new NbtList("e"),
                new NbtList("f", new NbtTag[] {
                    new NbtCompound { new NbtString("k", "v") },
                    new NbtCompound()
                }),
                new NbtString("s", "x y")
            };
        }


        [TestMethod]
        public void CompactLayoutHasNoWhitespaceAndKeepsInsertionOrder() {
            Assert.AreEqual("{a:1,b:[1,2],c:[B;1B,2B],d:{},e:[],f:[{k:\"v\"},{}],s:\"x y\"}",
                            Snbt(MakeLayoutSample()));
            Assert.AreEqual("{zz:1,aa:2,MM:3}", Snbt(new NbtCompound {
                new NbtInt("zz", 1), new NbtInt("aa", 2), new NbtInt("MM", 3)
            }));
        }


        [TestMethod]
        public void SpacedLayoutAddsOneSpaceAfterSeparators() {
            Assert.AreEqual("{a: 1, b: [1, 2], c: [B; 1B, 2B], d: {}, e: [], f: [{k: \"v\"}, {}], s: \"x y\"}",
                            Snbt(MakeLayoutSample(), SnbtLayout.Spaced));
        }


        [TestMethod]
        public void IndentedLayoutExpandsContainersAndKeepsScalarListsInline() {
            string expected =
                "{\n" +
                "    a: 1,\n" +
                "    b: [1, 2],\n" +
                "    c: [B; 1B, 2B],\n" +
                "    d: {},\n" +
                "    e: [],\n" +
                "    f: [\n" +
                "        {\n" +
                "            k: \"v\"\n" +
                "        },\n" +
                "        {}\n" +
                "    ],\n" +
                "    s: \"x y\"\n" +
                "}";
            Assert.AreEqual(expected, Snbt(MakeLayoutSample(), SnbtLayout.Indented));

            // A list of lists expands one level, and the inner scalar lists stay inline
            NbtList nested = new NbtList(new NbtTag[] {
                new NbtList(new NbtTag[] { new NbtInt(1) }),
                new NbtList()
            });
            Assert.AreEqual("[\n    [1],\n    []\n]", Snbt(nested, SnbtLayout.Indented));
            Assert.AreEqual("[\"a\", \"b\"]",
                            Snbt(new NbtList(new NbtTag[] { new NbtString("a"), new NbtString("b") }),
                                 SnbtLayout.Indented));
        }


        [TestMethod]
        public void NameIsNeverWritten() {
            Assert.AreEqual("5", Snbt(new NbtInt("name", 5)));
            Assert.AreEqual("{}", Snbt(new NbtCompound("root")));
            Assert.AreEqual("[]", Snbt(new NbtList("list")));
        }


        [TestMethod]
        public void EmptyListPrintsWhateverItsType() {
            Assert.AreEqual("[]", Snbt(new NbtList()));
            Assert.AreEqual("[]", Snbt(new NbtList(NbtTagType.End)));
            Assert.AreEqual("[]", Snbt(new NbtList(NbtTagType.Compound)));
        }


        [TestMethod]
        public void WrapperCompoundsInsideCompoundListsPrintAsTheirValues() {
            // The on-disk form of a 1.21.5 mixed list prints the way the game prints it
            NbtList mixed = new NbtList(new NbtTag[] {
                new NbtCompound { new NbtInt("", 1) },
                new NbtCompound { new NbtString("", "a") },
                new NbtCompound { new NbtInt("k", 2) },
                new NbtCompound(),
                new NbtCompound { new NbtCompound("", new NbtTag[] { new NbtInt("", 3) }) }
            });
            Assert.AreEqual("[1,\"a\",{k:2},{},{\"\":3}]", Snbt(mixed));
            // Homogeneous wrappers unwrap too, and stay inline when their values are scalars
            NbtList ints = new NbtList(new NbtTag[] {
                new NbtCompound { new NbtInt("", 1) },
                new NbtCompound { new NbtInt("", 2) }
            });
            Assert.AreEqual("[1, 2]", Snbt(ints, SnbtLayout.Indented));
            // Outside a list, and with more than one entry, an empty key is just a key
            Assert.AreEqual("{\"\":1}", Snbt(new NbtCompound { new NbtInt("", 1) }));
            Assert.AreEqual("[{\"\":1,a:2}]", Snbt(new NbtList(new NbtTag[] {
                new NbtCompound { new NbtInt("", 1), new NbtInt("a", 2) }
            })));
        }


        [TestMethod]
        public void NestingBeyondTheDepthLimitIsRefused() {
            NbtList root = new NbtList();
            NbtList current = root;
            for (int i = 1; i < 512; i++) {
                NbtList child = new NbtList();
                current.Add(child);
                current = child;
            }
            Assert.AreEqual(1024, Snbt(root).Length);
            current.Add(new NbtList());
            // Only parsing has a position; the writer's depth failure is the plain exception
            Assert.ThrowsExactly<NbtFormatException>(() => Snbt(root));
        }
    }
}

using System;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace fNbt.Test {
    // NbtTag.ParseSnbt: the generous reading (modern grammar with classic fall-backs), the
    // documented deviations, error positions, the index overload, and round trips through ToSnbt.
    // The game-verdict table is in SnbtGameCasesTests; these are the cases worth naming.
    [TestClass]
    public class SnbtParserTests {
        static NbtTag Parse(string text) {
            return NbtTag.ParseSnbt(text);
        }


        static NbtFormatException Refuses(string text) {
            return Assert.Throws<NbtFormatException>(() => NbtTag.ParseSnbt(text));
        }


        [TestMethod]
        public void ScalarsReadWithTheirTypes() {
            Assert.AreEqual(1, Parse("1").IntValue);
            Assert.AreEqual(NbtTagType.Int, Parse("1").TagType);
            Assert.AreEqual(NbtTagType.Byte, Parse("1b").TagType);
            Assert.AreEqual(NbtTagType.Short, Parse("1S").TagType);
            Assert.AreEqual(NbtTagType.Int, Parse("1i").TagType);
            Assert.AreEqual(NbtTagType.Long, Parse("1l").TagType);
            Assert.AreEqual(NbtTagType.Float, Parse("1f").TagType);
            Assert.AreEqual(NbtTagType.Double, Parse("1d").TagType);
            Assert.AreEqual(NbtTagType.Double, Parse("1.5").TagType);
            Assert.AreEqual(NbtTagType.Double, Parse("1e5").TagType);
            Assert.AreEqual(100000.0, Parse("1E+5").DoubleValue);
            Assert.AreEqual(0.5, Parse(".5").DoubleValue);
            Assert.AreEqual(1.0, Parse("1.").DoubleValue);
            Assert.AreEqual(31, Parse("0x1F").IntValue);
            Assert.AreEqual(5, Parse("0B101").IntValue);
            Assert.AreEqual(1000000L, Parse("1_000_000L").LongValue);
            Assert.AreEqual(255, Parse("255ub").ByteValue);
            Assert.AreEqual(255, Parse("255b").ByteValue);
            Assert.AreEqual(128, Parse("128B").ByteValue);
            Assert.AreEqual(-106, ((NbtByte)Parse("-106b")).SignedValue);
            Assert.AreEqual(240, Parse("-16sb").ByteValue);
            Assert.AreEqual(-1, Parse("0xFFFFFFFF").IntValue);
            Assert.AreEqual(-1L, Parse("18446744073709551615ul").LongValue);
            Assert.AreEqual(4091, Parse("0xFFb").IntValue);
            Assert.AreEqual(0, Parse("0b").ByteValue);
            Assert.AreEqual(1, Parse("true").ByteValue);
            Assert.AreEqual(0, Parse("FALSE").ByteValue);
            Assert.AreEqual("abc", Parse("abc").StringValue);
            Assert.AreEqual("a.b-c+d", Parse("a.b-c+d").StringValue);
            Assert.AreEqual("a b", Parse("\"a b\"").StringValue);
            Assert.AreEqual("a\"b", Parse("'a\"b'").StringValue);
            Assert.AreEqual("it's", Parse("\"it's\"").StringValue);
        }


        [TestMethod]
        public void WhitespaceIsSkippedEverywhereTheModernGameSkipsIt() {
            Assert.AreEqual("{a:1b,b:-5,c:1.5d,d:[B;1B,2B],e:1b}",
                            Parse(" { a : 1 b , b : - 5 , c : 1 .5 , d : [ B ; 1 , 2 ] , e : bool ( 1 ) } ").ToSnbt());
            Assert.AreEqual("[1,2]", Parse("\t[\n1,\r\n2\n]\n").ToSnbt());
            Assert.AreEqual("{}", Parse("\uFEFF{}").ToSnbt());
        }


        [TestMethod]
        public void ClassicReadingsFillInWhereTheModernGrammarRefuses() {
            Assert.AreEqual("1st", Parse("1st").StringValue);
            Assert.AreEqual("-foo", Parse("-foo").StringValue);
            Assert.AreEqual(".5x", Parse(".5x").StringValue);
            Assert.AreEqual("007", Parse("007").StringValue);
            Assert.AreEqual("300b", Parse("300b").StringValue);
            Assert.AreEqual("-129b", Parse("-129b").StringValue);
            Assert.AreEqual("255sb", Parse("255sb").StringValue);
            Assert.AreEqual("4294967296", Parse("4294967296").StringValue);
            Assert.AreEqual("1e", Parse("1e").StringValue);
            Assert.AreEqual("1.5.2", Parse("1.5.2").StringValue);
            Assert.AreEqual("0x", Parse("0x").StringValue);
            Assert.AreEqual("1._5", Parse("1._5").StringValue);
            Assert.AreEqual("1e_5", Parse("1e_5").StringValue);
            Assert.AreEqual("0b_1", Parse("0b_1").StringValue);
            Assert.AreEqual("0_1", Parse("0_1").StringValue);
            Assert.AreEqual("1_", Parse("1_").StringValue);
            Assert.AreEqual("-0x1", Parse("-0x1").StringValue);
            // Overflowing floats are the infinities the classic parser stored
            Assert.IsTrue(float.IsPositiveInfinity(Parse("1e39f").FloatValue));
            Assert.IsTrue(double.IsPositiveInfinity(Parse("1.0e1000").DoubleValue));
            Assert.IsTrue(double.IsNegativeInfinity(Parse("-1e999d").DoubleValue));
            Assert.AreEqual("1e1000", Parse("1e1000").StringValue);
            // Booleans inside byte arrays
            CollectionAssert.AreEqual(new byte[] { 1, 0 }, ((NbtByteArray)Parse("[B;true,false]")).Value);
        }


        [TestMethod]
        public void NonFiniteNamesWithSuffixesAreNumbers() {
            Assert.IsTrue(float.IsNaN(Parse("NaNf").FloatValue));
            Assert.IsTrue(float.IsPositiveInfinity(Parse("Infinityf").FloatValue));
            Assert.IsTrue(float.IsNegativeInfinity(Parse("-Infinityf").FloatValue));
            Assert.IsTrue(double.IsNaN(Parse("NaNd").DoubleValue));
            Assert.IsTrue(double.IsPositiveInfinity(Parse("+InfinityD").DoubleValue));
            Assert.AreEqual("NaN", Parse("NaN").StringValue);
            Assert.AreEqual("Infinity", Parse("Infinity").StringValue);
            Assert.AreEqual("Infinityx", Parse("Infinityx").StringValue);
        }


        [TestMethod]
        public void EscapesFollowTheModernGrammar() {
            Assert.AreEqual("a\\b\"c'd\be f\tg\nh\fi\rj", Parse("\"a\\\\b\\\"c\\'d\\be\\sf\\tg\\nh\\fi\\rj\"").StringValue);
            Assert.AreEqual("AB", Parse("\"\\x41B\"").StringValue);
            Assert.AreEqual("A1", Parse("\"\\u00411\"").StringValue);
            Assert.AreEqual("\ud83d\ude00", Parse("\"\\U0001F600\"").StringValue);
            Assert.AreEqual("\ud83d", Parse("\"\\uD83D\"").StringValue);
            Assert.AreEqual("\u00ff", Parse("\"\\xFF\"").StringValue);
            // Raw control characters and a backslash-newline-letter sequence, as the game reads them
            Assert.AreEqual("a\nb\u0000c", Parse("\"a\nb\u0000c\"").StringValue);
            Assert.AreEqual("a\nb", Parse("\"a\\\n nb\"").StringValue);
            Refuses("\"\\q\"");
            Refuses("\"\\x4\"");
            Refuses("\"\\x4G\"");
            Refuses("\"\\u260\"");
            Refuses("\"\\U00110000\"");
            Refuses("\"abc");
            Refuses("\"abc\\");
            StringAssert.Contains(Refuses("\"\\N{SNOWMAN}\"").Message, "not supported");
        }


        [TestMethod]
        public void CompoundsKeepInsertionOrderAndLetLaterDuplicatesWin() {
            NbtCompound c = (NbtCompound)Parse("{zz:1,\"a b\":2,'q':3,1a:4,-x:5,\"\":6,zz:7,}");
            Assert.AreEqual("{zz:7,\"a b\":2,q:3,\"1a\":4,\"-x\":5,\"\":6}", c.ToSnbt());
            Assert.AreEqual(7, c["zz"].IntValue);
            Assert.IsNull(Parse("{}").Name);
            Refuses("{a:1 b:2}");
            Refuses("{a}");
            Refuses("{a:}");
            Refuses("{,}");
            Refuses("{a:1,,}");
            Refuses("{a:1");
            Refuses("{a:1;b:2}");
            Refuses("{a=1}");
            Refuses("{a");
            Refuses("{a:1,");
            Refuses("{:1}");
        }


        [TestMethod]
        public void ListsWrapMixedElementsLikeTheGameDoesOnDisk() {
            NbtList homogeneous = (NbtList)Parse("[1,2,]");
            Assert.AreEqual(NbtTagType.Int, homogeneous.ListType);
            Assert.AreEqual(2, homogeneous.Count);

            NbtList empty = (NbtList)Parse("[ ]");
            Assert.AreEqual(NbtTagType.End, empty.ListType);
            empty.Add(new NbtInt(1));

            NbtList mixed = (NbtList)Parse("[1,\"a\",{},[2],{\"\":5},{k:1}]");
            Assert.AreEqual(NbtTagType.Compound, mixed.ListType);
            Assert.AreEqual(1, mixed.Get<NbtCompound>(0)[""].IntValue);
            Assert.AreEqual("a", mixed.Get<NbtCompound>(1)[""].StringValue);
            Assert.AreEqual(0, mixed.Get<NbtCompound>(2).Count);
            Assert.AreEqual(NbtTagType.List, mixed.Get<NbtCompound>(3)[""].TagType);
            // A wrapper-shaped compound is wrapped again, so unwrapping on print gives it back
            Assert.AreEqual(5, mixed.Get<NbtCompound>(4)[""][""].IntValue);
            Assert.AreEqual(1, mixed.Get<NbtCompound>(5)["k"].IntValue);
            Assert.AreEqual("[1,\"a\",{},[2],{\"\":5},{k:1}]", mixed.ToSnbt());
            // A wrapper-shaped compound is wrapped again inside any list of compounds, as the game
            // does on save, so a loaded document's text does not drift through repeated round trips
            NbtList loaded = (NbtList)Parse("[{\"\":1},{\"\":2}]");
            Assert.AreEqual(1, loaded.Get<NbtCompound>(0)[""][""].IntValue);
            Assert.AreEqual("[{\"\":1},{\"\":2}]", loaded.ToSnbt());
            Assert.AreEqual("[{\"\":1},{k:2}]", Parse("[{\"\":1},{k:2}]").ToSnbt());

            // Lists of lists may differ inside; lists of different array kinds are mixed
            Assert.AreEqual(NbtTagType.List, ((NbtList)Parse("[[1],[\"a\"]]")).ListType);
            Assert.AreEqual(NbtTagType.Compound, ((NbtList)Parse("[[B;1],[I;2]]")).ListType);
            Refuses("[,]");
            Refuses("[1,,2]");
            Refuses("[1 2]");
            Refuses("[1");
            Refuses("[1]]");
            Refuses("[");
            Refuses("[1,");
            Refuses("[B");
            Refuses("[B;");
        }


        [TestMethod]
        public void ArraysTakeAnyIntegerThatFits() {
            CollectionAssert.AreEqual(new byte[] { 1, 2, 127, 255, 255, 128 },
                                      ((NbtByteArray)Parse("[B;1,2b,0x7F,0xFF,255ub,-128]")).Value);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, -1 }, ((NbtIntArray)Parse("[I;1b,2s,3,0xFFFFFFFF]")).Value);
            CollectionAssert.AreEqual(new[] { 1L, -1L, long.MinValue },
                                      ((NbtLongArray)Parse("[L;1,-1b,-9223372036854775808L]")).Value);
            Assert.AreEqual(0, ((NbtByteArray)Parse("[B;]")).Value.Length);
            Assert.AreEqual(0, ((NbtIntArray)Parse("[ I ; ]")).Value.Length);
            Assert.AreEqual(1, ((NbtLongArray)Parse("[L;1,]")).Value.Length);
            Refuses("[L;,]");
            // Unsuffixed elements take the array's type, and either signedness fits
            Assert.AreEqual(long.MaxValue, ((NbtLongArray)Parse("[L;9223372036854775807]")).Value[0]);
            Assert.AreEqual(-1L, ((NbtLongArray)Parse("[L;0xFFFFFFFFFFFFFFFF]")).Value[0]);
            Assert.AreEqual(128, ((NbtByteArray)Parse("[B;128]")).Value[0]);
            Refuses("[B;256]");
            Refuses("[B;-129]");
            Refuses("[I;4294967296]");
            Refuses("[B;1.0]");
            Refuses("[B;\"1\"]");
            Refuses("[B;1;2]");
            // Prefixes in either case; other letters are not prefixes
            Assert.AreEqual(NbtTagType.ByteArray, Parse("[b;1]").TagType);
            Assert.AreEqual(NbtTagType.IntArray, Parse("[ i ; 1, 2 ]").TagType);
            Assert.AreEqual(NbtTagType.LongArray, Parse("[l;]").TagType);
            Refuses("[S;1]");
            Refuses("[d;1]");
        }


        [TestMethod]
        public void OperationsEvaluateAtParseTime() {
            Assert.AreEqual(1, Parse("bool(1)").ByteValue);
            Assert.AreEqual(0, Parse("bool(0.0)").ByteValue);
            Assert.AreEqual(1, Parse("bool(0.5)").ByteValue);
            Assert.AreEqual(1, Parse("bool(true)").ByteValue);
            Assert.AreEqual(1, Parse("bool(bool(-1L),)").ByteValue);
            CollectionAssert.AreEqual(new[] { 306070887, -392490285, -1537850778, 337068032 },
                                      ((NbtIntArray)Parse("uuid('123e4567-E89B-12d3-a456-426614174000')")).Value);
            CollectionAssert.AreEqual(new[] { 0, 0, 0, 0 }, ((NbtIntArray)Parse("uuid(\"0-0-0-0-0\")")).Value);
            StringAssert.Contains(Refuses("bool()").Message, "bool/0");
            StringAssert.Contains(Refuses("bool(1,2)").Message, "bool/2");
            StringAssert.Contains(Refuses("foo(1)").Message, "foo/1");
            Refuses("bool(\"true\")");
            Refuses("bool([])");
            Refuses("uuid(1)");
            Refuses("uuid(\"not-a-uuid\")");
            Refuses("uuid(\"123e4567e89b12d3a456426614174000\")");
            Refuses("{bool(1):1}");
            Refuses("bool(");
            Refuses("bool(1");
            Refuses("bool(1 2)");
            Refuses("bool(1,");
            Refuses("uuid(\"123e45678-e89b-12d3-a456-426614174000\")");
            Refuses("uuid(\"123e456g-e89b-12d3-a456-426614174000\")");
            Refuses("uuid(\"-0-0-0-0\")");
            CollectionAssert.AreEqual(new[] { 306070887, -392490285, -1537850778, 337068032 },
                                      ((NbtIntArray)Parse("uuid(\"123e4567-e89b-12d3-a456-426614174000\",)")).Value);
        }


        [TestMethod]
        public void RootsMayBeAnyTagAndTrailingTextIsRefused() {
            Assert.AreEqual(NbtTagType.Byte, Parse("true").TagType);
            Assert.AreEqual(NbtTagType.String, Parse("'x'").TagType);
            Assert.AreEqual(NbtTagType.ByteArray, Parse("[B;1b]").TagType);
            Assert.AreEqual(NbtTagType.Compound, Parse(" \n{}\n ").TagType);
            Assert.Throws<ArgumentNullException>(() => NbtTag.ParseSnbt(null));
            Refuses("");
            Refuses("   ");
            Refuses("{} x");
            Refuses("{}{}");
            Refuses("1 2");
            Refuses("abc def");
        }


        [TestMethod]
        public void ErrorsCarryTheIndexAndTheLineAndColumn() {
            NbtFormatException ex = Refuses("{a:1,\n b:[1,,2]}");
            Assert.AreEqual(12, ex.Index);
            StringAssert.Contains(ex.Message, "at index 12 (line 2, column 7)");
            Assert.AreEqual(0, Refuses("").Index);
            Assert.AreEqual(3, Refuses("{} x").Index);
            Assert.AreEqual(1, Refuses("\"\\q\"").Index);
            // A bad array element is reported where it is, not at the bracket
            Assert.AreEqual(6, Refuses("[B;1, 300]").Index);
            Assert.AreEqual(5, Refuses("[I;1,\"x\"]").Index);
        }


        [TestMethod]
        public void IndexOverloadStopsAfterOneValue() {
            string command = "give @s stone[custom_data={a:1,b:\"x y\"}] 3";
            int at = command.IndexOf('{');
            NbtTag tag = NbtTag.ParseSnbt(command, at, out int consumed);
            Assert.AreEqual("{a:1,b:\"x y\"}", tag.ToSnbt());
            Assert.AreEqual("] 3", command.Substring(at + consumed));

            Assert.AreEqual("1abc", NbtTag.ParseSnbt("  1abc", 0, out consumed).StringValue);
            Assert.AreEqual(6, consumed);
            Assert.AreEqual("a", NbtTag.ParseSnbt("a b", 0, out consumed).StringValue);
            Assert.AreEqual(1, consumed);
            NbtFormatException ex = Assert.Throws<NbtFormatException>(() => NbtTag.ParseSnbt("x {", 2, out consumed));
            Assert.AreEqual(3, ex.Index);
            Assert.Throws<ArgumentOutOfRangeException>(() => NbtTag.ParseSnbt("x", 2, out consumed));
            Assert.Throws<ArgumentOutOfRangeException>(() => NbtTag.ParseSnbt("x", -1, out consumed));
            Assert.Throws<NbtFormatException>(() => NbtTag.ParseSnbt("x", 1, out consumed));
            Assert.Throws<ArgumentNullException>(() => NbtTag.ParseSnbt(null, 0, out consumed));
        }


        [TestMethod]
        public void NestingBeyondTheDepthLimitIsRefused() {
            string ok = new string('[', 512) + new string(']', 512);
            Assert.AreEqual(ok, Parse(ok).ToSnbt());
            NbtFormatException ex = Refuses(new string('[', 513) + new string(']', 513));
            Assert.AreEqual(512, ex.Index);
            StringAssert.Contains(ex.Message, "512 levels) at index 512");
            Refuses(string.Concat(Enumerable.Repeat("{a:", 513)) + "1" + new string('}', 513));
            // Operation calls nest like containers
            Assert.AreEqual(1, Parse(string.Concat(Enumerable.Repeat("bool(", 512)) + "1" + new string(')', 512)).ByteValue);
            Refuses(string.Concat(Enumerable.Repeat("bool(", 513)) + "1" + new string(')', 513));
        }


        [TestMethod]
        public void ParsingIsCultureIndependent() {
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;
            try {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                Assert.AreEqual(1.5, Parse("1.5").DoubleValue);
                Assert.AreEqual(1000000.0, Parse("1e6").DoubleValue);
                Assert.AreEqual("1,5", Parse("\"1,5\"").StringValue);
            } finally {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }


        [TestMethod]
        public void EveryTagTypeRoundTripsThroughText() {
            NbtCompound root = TestFiles.MakeAllValuesRoot();
            root.Add(new NbtString("quotes", "it's \"both\" \\ and\nnewline"));
            root.Add(new NbtList("mixedOnDisk", new NbtTag[] {
                new NbtCompound { new NbtInt("", 1) },
                new NbtCompound { new NbtString("", "a") }
            }));
            root.Add(new NbtFloat("nan", float.NaN));
            root.Add(new NbtDouble("inf", double.NegativeInfinity));
            // Text carries no element type, so an empty list comes back End-typed, as from a file
            root.Add(new NbtList("empty", NbtTagType.End));
            foreach (SnbtLayout layout in new[] { SnbtLayout.Compact, SnbtLayout.Spaced, SnbtLayout.Indented }) {
                string text = root.ToSnbt(new SnbtOptions { WriteLayout = layout });
                NbtTag back = Parse(text);
                back.Name = root.Name;
                NbtAssert.AreEqual(root, back, layout.ToString());
            }
            // Text carries no element type for an empty list, and the comparer needs none
            NbtCompound lists = TestFiles.MakeAllListsRoot();
            NbtTag parsed = Parse(lists.ToSnbt());
            parsed.Name = lists.Name;
            NbtAssert.AreEqual(lists, parsed);
        }
    }
}

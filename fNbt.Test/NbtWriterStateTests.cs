using System;
using System.IO;

namespace fNbt.Test {
    // NbtWriter's argument checks and its emission window: a refused call writes nothing and
    // leaves the writer usable, a failure mid-write leaves it in its error state for good.
    [TestClass]
    public class NbtWriterStateTests {
        [TestMethod]
        public void ArgumentsAndStateAreValidated() {
            byte[] dummyByteArray = { 1, 2, 3, 4, 5 };
            int[] dummyIntArray = { 1, 2, 3, 4, 5 };
            long[] dummyLongArray = { 1, 2, 3, 4, 5 };
            MemoryStream dummyStream = new MemoryStream(dummyByteArray);

            using (var ms = new MemoryStream()) {
                // null constructor parameters, or a non-writable stream
                Assert.Throws<ArgumentNullException>(() => new NbtWriter(null, "root"));
                Assert.Throws<ArgumentNullException>(() => new NbtWriter(ms, null));
                Assert.Throws<ArgumentException>(() => new NbtWriter(new NonWritableStream(), "root"));

                var writer = new NbtWriter(ms, "root");
                {
                    // use negative list size
                    Assert.Throws<ArgumentOutOfRangeException>(() => writer.BeginList("list", NbtTagType.Int, -1));
                    writer.BeginList("listOfLists", NbtTagType.List, 1);
                    Assert.Throws<ArgumentOutOfRangeException>(() => writer.BeginList(NbtTagType.Int, -1));
                    writer.BeginList(NbtTagType.Int, 0);
                    writer.EndList();
                    writer.EndList();

                    writer.BeginList("list", NbtTagType.Int, 1);

                    // call EndCompound when not in a compound
                    Assert.Throws<NbtFormatException>(writer.EndCompound);

                    // end list before all elements have been written
                    Assert.Throws<NbtFormatException>(writer.EndList);

                    // write the wrong kind of tag inside a list
                    Assert.Throws<NbtFormatException>(() => writer.WriteShort(0));

                    // write a named tag where an unnamed tag is expected
                    Assert.Throws<NbtFormatException>(() => writer.WriteInt("NamedInt", 0));

                    // write too many list elements
                    writer.WriteTag(new NbtInt());
                    Assert.Throws<NbtFormatException>(() => writer.WriteInt(0));
                    writer.EndList();

                    // write a null tag
                    Assert.Throws<ArgumentNullException>(() => writer.WriteTag(null));

                    // write an unnamed tag where a named tag is expected
                    Assert.Throws<NbtFormatException>(() => writer.WriteTag(new NbtInt()));
                    Assert.Throws<NbtFormatException>(() => writer.WriteInt(0));

                    // end a list when not in a list
                    Assert.Throws<NbtFormatException>(writer.EndList);

                    // unacceptable nulls: WriteString
                    Assert.Throws<ArgumentNullException>(() => writer.WriteString(null));
                    Assert.Throws<ArgumentNullException>(() => writer.WriteString("NullString", null));

                    // unacceptable nulls: WriteByteArray from array
                    Assert.Throws<ArgumentNullException>(() => writer.WriteByteArray(null));
                    Assert.Throws<ArgumentNullException>(() => writer.WriteByteArray(null, 0, 5));
                    Assert.Throws<ArgumentNullException>(() => writer.WriteByteArray("NullByteArray", null));
                    Assert.Throws<ArgumentNullException>(() => writer.WriteByteArray("NullByteArray", null, 0, 5));

                    // unacceptable nulls: WriteByteArray from stream
                    Assert.Throws<ArgumentNullException>(() => writer.WriteByteArray(null, 5));
                    Assert.Throws<ArgumentNullException>(() => writer.WriteByteArray(null, 5, null));
                    Assert.Throws<ArgumentNullException>(() => writer.WriteByteArray(dummyStream, 5, null));
                    Assert.Throws<ArgumentNullException>(() => writer.WriteByteArray("NullBuffer", dummyStream, 5, null));
                    Assert.Throws<ArgumentNullException>(() => writer.WriteByteArray("NullStream", null, 5));
                    Assert.Throws<ArgumentNullException>(
                        () => writer.WriteByteArray("NullStream", null, 5, dummyByteArray));

                    // unacceptable nulls: WriteIntArray
                    Assert.Throws<ArgumentNullException>(() => writer.WriteIntArray(null));
                    Assert.Throws<ArgumentNullException>(() => writer.WriteIntArray(null, 0, 5));
                    Assert.Throws<ArgumentNullException>(() => writer.WriteIntArray("NullIntArray", null));
                    Assert.Throws<ArgumentNullException>(() => writer.WriteIntArray("NullIntArray", null, 0, 5));

                    // unacceptable nulls: WriteLongArray
                    Assert.Throws<ArgumentNullException>(() => writer.WriteLongArray(null));
                    Assert.Throws<ArgumentNullException>(() => writer.WriteLongArray(null, 0, 5));
                    Assert.Throws<ArgumentNullException>(() => writer.WriteLongArray("NullLongArray", null));
                    Assert.Throws<ArgumentNullException>(() => writer.WriteLongArray("NullLongArray", null, 0, 5));

                    // non-readable streams are unacceptable
                    Assert.Throws<ArgumentException>(() => writer.WriteByteArray(new NonReadableStream(), 0));
                    Assert.Throws<ArgumentException>(
                        () => writer.WriteByteArray(new NonReadableStream(), 0, new byte[10]));
                    Assert.Throws<ArgumentException>(
                        () => writer.WriteByteArray("NonReadableStream", new NonReadableStream(), 0));

                    // trying to write array with out-of-range offset/count
                    Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteByteArray(dummyByteArray, -1, 5));
                    Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteByteArray(dummyByteArray, 0, -1));
                    Assert.Throws<ArgumentException>(() => writer.WriteByteArray(dummyByteArray, 0, 6));
                    Assert.Throws<ArgumentException>(() => writer.WriteByteArray(dummyByteArray, 1, 5));
                    Assert.Throws<ArgumentOutOfRangeException>(
                        () => writer.WriteByteArray("OutOfRangeByteArray", dummyByteArray, -1, 5));
                    Assert.Throws<ArgumentOutOfRangeException>(
                        () => writer.WriteByteArray("OutOfRangeByteArray", dummyByteArray, 0, -1));
                    Assert.Throws<ArgumentException>(
                        () => writer.WriteByteArray("OutOfRangeByteArray", dummyByteArray, 0, 6));
                    Assert.Throws<ArgumentException>(
                        () => writer.WriteByteArray("OutOfRangeByteArray", dummyByteArray, 1, 5));

                    Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteIntArray(dummyIntArray, -1, 5));
                    Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteIntArray(dummyIntArray, 0, -1));
                    Assert.Throws<ArgumentException>(() => writer.WriteIntArray(dummyIntArray, 0, 6));
                    Assert.Throws<ArgumentException>(() => writer.WriteIntArray(dummyIntArray, 1, 5));
                    Assert.Throws<ArgumentOutOfRangeException>(
                        () => writer.WriteIntArray("OutOfRangeIntArray", dummyIntArray, -1, 5));
                    Assert.Throws<ArgumentOutOfRangeException>(
                        () => writer.WriteIntArray("OutOfRangeIntArray", dummyIntArray, 0, -1));
                    Assert.Throws<ArgumentException>(
                        () => writer.WriteIntArray("OutOfRangeIntArray", dummyIntArray, 0, 6));
                    Assert.Throws<ArgumentException>(
                        () => writer.WriteIntArray("OutOfRangeIntArray", dummyIntArray, 1, 5));

                    Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteLongArray(dummyLongArray, -1, 5));
                    Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteLongArray(dummyLongArray, 0, -1));
                    Assert.Throws<ArgumentException>(() => writer.WriteLongArray(dummyLongArray, 0, 6));
                    Assert.Throws<ArgumentException>(() => writer.WriteLongArray(dummyLongArray, 1, 5));
                    Assert.Throws<ArgumentOutOfRangeException>(
                        () => writer.WriteLongArray("OutOfRangeLongArray", dummyLongArray, -1, 5));
                    Assert.Throws<ArgumentOutOfRangeException>(
                        () => writer.WriteLongArray("OutOfRangeLongArray", dummyLongArray, 0, -1));
                    Assert.Throws<ArgumentException>(
                        () => writer.WriteLongArray("OutOfRangeLongArray", dummyLongArray, 0, 6));
                    Assert.Throws<ArgumentException>(
                        () => writer.WriteLongArray("OutOfRangeLongArray", dummyLongArray, 1, 5));

                    // out-of-range values for stream-reading overloads of WriteByteArray
                    Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteByteArray(dummyStream, -1));
                    Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteByteArray("BadLength", dummyStream, -1));
                    Assert.Throws<ArgumentOutOfRangeException>(
                        () => writer.WriteByteArray(dummyStream, -1, dummyByteArray));
                    Assert.Throws<ArgumentOutOfRangeException>(
                        () => writer.WriteByteArray("BadLength", dummyStream, -1, dummyByteArray));
                    Assert.Throws<ArgumentException>(() => writer.WriteByteArray(dummyStream, 5, new byte[0]));
                    Assert.Throws<ArgumentException>(
                        () => writer.WriteByteArray("BadLength", dummyStream, 5, new byte[0]));

                    // trying to read from non-readable stream
                    Assert.Throws<ArgumentException>(
                        () => writer.WriteByteArray("ByteStream", new NonReadableStream(), 0));

                    // finish too early
                    Assert.Throws<NbtFormatException>(writer.Finish);

                    writer.EndCompound();
                    writer.Finish();

                    // write tag after finishing
                    Assert.Throws<NbtFormatException>(() => writer.WriteTag(new NbtInt()));
                }
            }
        }


        [TestMethod]
        public void UnnamedTagsAreRejectedInCompounds() {
            using (var ms = new MemoryStream()) {
                NbtWriter writer = new NbtWriter(ms, "test");
                // All tags (aside from list elements) must be named. The check is
                // type-independent, so one value, one array, and two containers cover it.
                Assert.Throws<NbtFormatException>(() => writer.WriteTag(new NbtInt(123)));
                Assert.Throws<NbtFormatException>(() => writer.WriteTag(new NbtByteArray(new byte[0])));
                Assert.Throws<NbtFormatException>(() => writer.WriteTag(new NbtList(NbtTagType.Byte)));
                Assert.Throws<NbtFormatException>(() => writer.WriteTag(new NbtCompound()));
                // Refusals must not poison the writer
                Assert.IsFalse(writer.IsInErrorState);
                writer.EndCompound();
                writer.Finish();
            }
        }


        [TestMethod]
        public void WriteByteArrayFromShortStreamThrows() {
            // A source shorter than count used to spin forever returning 0. It must throw instead.
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "root");
                Assert.Throws<EndOfStreamException>(
                    () => writer.WriteByteArray("arr", new MemoryStream(new byte[5]), 10));
                Assert.Throws<NbtFormatException>(() => writer.WriteInt("afterFailure", 1));
            }
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "root");
                writer.BeginList("list", NbtTagType.ByteArray, 1);
                Assert.Throws<EndOfStreamException>(
                    () => writer.WriteByteArray(new MemoryStream(new byte[5]), 10));
                Assert.Throws<NbtFormatException>(() => writer.WriteByteArray(new byte[0]));
            }
        }


        [TestMethod]
        public void StandardUtf8StringsAreValidatedBeforeOutput() {
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "root", NbtFlavor.Bedrock);
                writer.BeginList("strings", NbtTagType.String, 1);
                NbtAssert.WritesNothing<NbtFormatException>(ms, () => writer.WriteString("\uD800"));
                writer.WriteString("ok");
                writer.EndList();

                NbtAssert.WritesNothing<NbtFormatException>(ms, () => writer.WriteInt("\uD800", 1));
                NbtAssert.WriterStillUsable(writer);
            }

            using (var ms = new MemoryStream()) {
                Assert.Throws<NbtFormatException>(() => new NbtWriter(ms, "\uD800", NbtFlavor.Bedrock));
                Assert.AreEqual(0, ms.Length);
            }
        }


        [TestMethod]
        public void StandardUtf8TreeIsValidatedBeforeOutput() {
            const string loneSurrogate = "\uD800";

            // With tree validation disabled, a direct name is still measured before its type
            // byte is emitted, so rejection does not damage the writer.
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "root", new NbtOptions(NbtFlavor.Bedrock) {
                    ValidateOnWrite = false
                });
                NbtAssert.WritesNothing<NbtFormatException>(ms,
                    () => writer.WriteTag(new NbtInt(loneSurrogate, 1)));
                NbtAssert.WriterStillUsable(writer);
            }

            // With validation on, the pre-walk counts the exact bytes under standard UTF-8, so
            // a nested lone surrogate is refused before anything is written
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "root", NbtFlavor.Bedrock);
                NbtAssert.WritesNothing<NbtFormatException>(ms,
                    () => writer.WriteTag(new NbtCompound("nested") {
                        new NbtString("value", loneSurrogate)
                    }));
                NbtAssert.WriterStillUsable(writer);
            }

            // NbtFile and NbtCodec share the validation walk
            var doc = new NbtCompound("root") { new NbtString("value", loneSurrogate) };
            using (var ms = new MemoryStream()) {
                var file = new NbtFile(doc, NbtFlavor.Bedrock);
                Assert.Throws<NbtFormatException>(() => file.SaveToStream(ms, NbtCompression.None));
                Assert.AreEqual(0, ms.Length);
            }
            Assert.Throws<NbtFormatException>(() => NbtCodec.For(NbtFlavor.Bedrock).WriteTag(doc));
        }


        [TestMethod]
        public void WriteTagRefusedUpFrontLeavesWriterUsable() {
            // A list with no element type is refused before the emission window opens, like
            // the tag layer would refuse it, so nothing is written and the writer goes on
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "root");
                NbtAssert.WritesNothing<NbtFormatException>(ms, () => writer.WriteTag(new NbtList("l")));
                NbtAssert.WriterStillUsable(writer);
            }
        }


        [TestMethod]
        public void PartialTreeFailurePoisonsWriter() {
            var partialTree = new NbtCompound("partial") {
                new NbtString("tooLong", new string('x', ushort.MaxValue + 1))
            };
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "root");
                long before = ms.Length;
                Assert.Throws<NbtFormatException>(() => writer.WriteTag(partialTree));
                Assert.IsTrue(ms.Length > before);
                NbtAssert.WriterIsPoisoned(writer);
            }
        }


        [TestMethod]
        public void DirectOutputFailurePoisonsWriter() {
            using (var stream = new FailAfterBytesStream()) {
                var writer = new NbtWriter(stream, "root");
                writer.BeginList("ints", NbtTagType.Int, 1);
                stream.FailAfter(2);

                // The int payload is four bytes, so the stream writes a prefix before throwing.
                Assert.Throws<IOException>(() => writer.WriteInt(123));
                // Observable without catching, and distinct from being done
                Assert.IsTrue(writer.IsInErrorState);
                Assert.IsFalse(writer.IsDone);
                Assert.Throws<NbtFormatException>(() => writer.WriteInt(456));
                Assert.Throws<NbtFormatException>(writer.EndList);
                Assert.Throws<NbtFormatException>(writer.EndCompound);
                Assert.Throws<NbtFormatException>(writer.Finish);
                // The stream itself stays reachable, so the caller can inspect or discard it
                Assert.IsNotNull(writer.BaseStream);
            }

            // A constructor failure cannot leak a usable writer instance.
            using (var stream = new FailAfterBytesStream()) {
                stream.FailAfter(0);
                Assert.Throws<IOException>(() => new NbtWriter(stream, "root"));
                Assert.AreEqual(0, stream.Length);
            }
        }


        [TestMethod]
        public void BaseStreamFlushFailureLeavesWriterUsable() {
            using (var stream = new FlushFailingStream()) {
                var writer = new NbtWriter(stream, "root");
                writer.WriteInt("before", 1);
                stream.FailFlush();

                // The getter flushes, so the failure surfaces there, but reading the property
                // is not a write: the writer stays usable, and a monitoring read cannot fail it
                Assert.Throws<IOException>(() => { _ = writer.BaseStream; });
                Assert.IsFalse(writer.IsInErrorState);
                writer.WriteInt("after", 2);
                writer.EndCompound();
                writer.Finish();
            }
        }


        [TestMethod]
        public void BaseStreamReadFromInsideAWriteIsHarmless() {
            // A stream that logs the writer's position from its own Write must not fail the writer
            using (var stream = new PositionLoggingStream()) {
                var writer = new NbtWriter(stream, "root");
                stream.Writer = writer;
                writer.WriteInt("a", 1);
                writer.WriteString("b", "two");
                writer.EndCompound();
                writer.Finish();
                Assert.IsTrue(stream.Logged > 0);
                // In-flight writes share the error-state sentinel, so a stream that asks from
                // inside its own Write sees true. Reads from outside a write are exact.
                Assert.IsTrue(stream.SawInFlightErrorState);
                Assert.IsFalse(writer.IsInErrorState);
            }
        }


        [TestMethod]
        public void BadListTypesRejected() {
            // "End" is only valid for empty lists, and "Unknown" is never valid.
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "root");
                Assert.Throws<ArgumentOutOfRangeException>(() => writer.BeginList("bad", NbtTagType.End, 1));
                Assert.Throws<ArgumentOutOfRangeException>(() => writer.BeginList("bad", NbtTagType.Unknown, 0));
            }
        }


        [TestMethod]
        public void RejectedWriteTagDoesNotConsumeAListSlot() {
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "r", NbtFlavor.ClassiCube);
                writer.BeginList("l", NbtTagType.String, 2);
                // Fails the flavor's 256-byte string ceiling; the list must still have both slots
                var over = new NbtString(new string('x', 300));
                Assert.Throws<NbtFormatException>(() => writer.WriteTag(over));
                writer.WriteString("a");
                writer.WriteString("b");
                writer.EndList();
                writer.EndCompound();
                writer.Finish();
            }
        }


        [TestMethod]
        public void RejectedStringDoesNotConsumeAListSlot() {
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "r", NbtFlavor.ClassiCube);
                writer.BeginList("l", NbtTagType.String, 1);
                NbtAssert.WritesNothing<NbtFormatException>(ms, () => writer.WriteString(new string('x', 300)));
                // The slot is still open, so the list cannot close short
                Assert.Throws<NbtFormatException>(() => writer.EndList());
                writer.WriteString("ok");
                writer.EndList();
                writer.EndCompound();
                NbtFile file = TestFiles.FinishAndReload(writer, ms, new NbtOptions(NbtFlavor.ClassiCube));
                Assert.AreEqual("ok", file.RootTag["l"][0].StringValue);
            }

            // The Java wire limit gets the same treatment
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "r");
                writer.BeginList("l", NbtTagType.String, 1);
                Assert.Throws<NbtFormatException>(() => writer.WriteString(new string('x', 65_536)));
                Assert.Throws<NbtFormatException>(() => writer.EndList());
            }
        }


        [TestMethod]
        public void RejectedStringsAndNamesWriteNothing() {
            string over = new string('x', 300);
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "r", NbtFlavor.ClassiCube);
                NbtAssert.WritesNothing<NbtFormatException>(ms, () => writer.WriteString("s", over));
                NbtAssert.WritesNothing<NbtFormatException>(ms, () => writer.WriteInt(over, 1));
                NbtAssert.WritesNothing<NbtFormatException>(ms, () => writer.BeginCompound(over));
                NbtAssert.WritesNothing<NbtFormatException>(ms, () => writer.BeginList(over, NbtTagType.Int, 0));
                NbtAssert.WritesNothing<NbtFormatException>(ms, () => writer.WriteTag(new NbtInt(over, 1)));
                // No type byte or name reached the stream, no container was left open, and
                // nothing that wrote nothing counts as an error
                NbtAssert.WriterStillUsable(writer);

                NbtFile file = TestFiles.Load(ms.ToArray(), NbtFlavor.ClassiCube);
                Assert.AreEqual(1, file.RootTag["afterRefusal"].IntValue);
            }

            // A root name over the limit writes nothing at all
            using (var ms = new MemoryStream()) {
                Assert.Throws<NbtFormatException>(() => new NbtWriter(ms, over, NbtFlavor.ClassiCube));
                Assert.AreEqual(0, ms.Length);
            }

            // A null name on a named overload is an argument error, not an unnamed element
            using (var ms = new MemoryStream()) {
                var writer = new NbtWriter(ms, "r");
                writer.BeginList("l", NbtTagType.Int, 1);
                NbtAssert.WritesNothing<ArgumentNullException>(ms, () => writer.WriteInt(null, 5));
                Assert.Throws<NbtFormatException>(() => writer.EndList());
                writer.WriteInt(5);
                writer.EndList();
                writer.EndCompound();
                writer.Finish();
            }
        }


        sealed class PositionLoggingStream : MemoryStream {
            public NbtWriter Writer;
            public int Logged;
            public bool SawInFlightErrorState;


            public override void Write(byte[] buffer, int offset, int count) {
                base.Write(buffer, offset, count);
                if (Writer != null) {
                    _ = Writer.BaseStream.Position;
                    if (Writer.IsInErrorState) SawInFlightErrorState = true;
                    Logged++;
                }
            }
        }


        sealed class FailAfterBytesStream : MemoryStream {
            int bytesBeforeFailure = int.MaxValue;


            public void FailAfter(int byteCount) {
                bytesBeforeFailure = byteCount;
            }


            public override void WriteByte(byte value) {
                if (bytesBeforeFailure == 0) throw new IOException("Injected write failure.");
                base.WriteByte(value);
                bytesBeforeFailure--;
            }


            public override void Write(byte[] buffer, int offset, int count) {
                if (count <= bytesBeforeFailure) {
                    base.Write(buffer, offset, count);
                    bytesBeforeFailure -= count;
                    return;
                }
                if (bytesBeforeFailure > 0) {
                    base.Write(buffer, offset, bytesBeforeFailure);
                    bytesBeforeFailure = 0;
                }
                throw new IOException("Injected write failure.");
            }
        }


        sealed class FlushFailingStream : MemoryStream {
            bool failFlush;


            public void FailFlush() {
                failFlush = true;
            }


            public override void Flush() {
                if (failFlush) throw new IOException("Injected flush failure.");
                base.Flush();
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace fNbt {
    /// <summary> Reads and writes single NBT documents ("blobs") that carry no file-level framing:
    /// network packet payloads, LevelDB values, and NBT embedded inside other formats. </summary>
    /// <remarks> These statics are shorthand for an <see cref="NbtCodec"/> with default
    /// <see cref="NbtOptions"/> for the given flavor: reads are generous, writes validate the
    /// flavor's conformance rules, no allocation limit. Create an <see cref="NbtCodec"/> directly
    /// to configure those. Unlike <see cref="NbtFile"/>, blobs are never compressed at this layer,
    /// may (per flavor) have unnamed or non-compound roots, and reads stop exactly at the end of
    /// one document, leaving any trailing bytes in place. </remarks>
    public static class NbtBlob {
        /// <inheritdoc cref="NbtCodec.ReadTag(Stream)"/>
        /// <param name="stream"> Stream to read from. Does not need to be seekable. </param>
        /// <param name="flavor"> Encoding to read the document with. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="stream"/> or <paramref name="flavor"/> is <c>null</c>. </exception>
        /// <exception cref="NotSupportedException"> <paramref name="flavor"/> is not yet supported. </exception>
        public static NbtTag ReadTag(Stream stream, NbtFlavor flavor) {
            return GetCodec(flavor).ReadTag(stream);
        }


        /// <inheritdoc cref="NbtCodec.ReadTag(Stream,NbtTagType)"/>
        /// <param name="stream"> Stream to read from. Does not need to be seekable. </param>
        /// <param name="flavor"> Encoding to read the document with. </param>
        /// <param name="expectedRootType"> Root tag type that the document must have. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="stream"/> or <paramref name="flavor"/> is <c>null</c>. </exception>
        /// <exception cref="NotSupportedException"> <paramref name="flavor"/> is not yet supported. </exception>
        public static NbtTag ReadTag(Stream stream, NbtFlavor flavor, NbtTagType expectedRootType) {
            return GetCodec(flavor).ReadTag(stream, expectedRootType);
        }


        /// <inheritdoc cref="NbtCodec.ReadTag(byte[],int,int,out int)"/>
        /// <param name="buffer"> Buffer to read from. </param>
        /// <param name="index"> Index in <paramref name="buffer"/> at which the document begins. </param>
        /// <param name="length"> Maximum number of bytes the document may occupy. Trailing bytes past the
        /// document's actual end are ignored. </param>
        /// <param name="flavor"> Encoding to read the document with. </param>
        /// <param name="bytesConsumed"> Set to the exact number of bytes the document occupied. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="buffer"/> or <paramref name="flavor"/> is <c>null</c>. </exception>
        /// <exception cref="NotSupportedException"> <paramref name="flavor"/> is not yet supported. </exception>
        public static NbtTag ReadTag(byte[] buffer, int index, int length, NbtFlavor flavor, out int bytesConsumed) {
            return GetCodec(flavor).ReadTag(buffer, index, length, out bytesConsumed);
        }


        /// <inheritdoc cref="NbtCodec.TryReadTag(Stream,out NbtTag)"/>
        /// <param name="stream"> Stream to read from. Does not need to be seekable. </param>
        /// <param name="flavor"> Encoding to read the document with. </param>
        /// <param name="tag"> Set to the root tag, or <c>null</c> if no tag was present. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="stream"/> or <paramref name="flavor"/> is <c>null</c>. </exception>
        /// <exception cref="NotSupportedException"> <paramref name="flavor"/> is not yet supported. </exception>
        public static bool TryReadTag(Stream stream, NbtFlavor flavor, [NotNullWhen(true)] out NbtTag? tag) {
            return GetCodec(flavor).TryReadTag(stream, out tag);
        }


        /// <inheritdoc cref="NbtCodec.TryReadTag(byte[],int,int,out NbtTag,out int)"/>
        /// <param name="buffer"> Buffer to read from. </param>
        /// <param name="index"> Index in <paramref name="buffer"/> at which the document begins. </param>
        /// <param name="length"> Maximum number of bytes the document may occupy. </param>
        /// <param name="flavor"> Encoding to read the document with. </param>
        /// <param name="tag"> Set to the root tag, or <c>null</c> if no tag was present. </param>
        /// <param name="bytesConsumed"> Set to the exact number of bytes consumed. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="buffer"/> or <paramref name="flavor"/> is <c>null</c>. </exception>
        /// <exception cref="NotSupportedException"> <paramref name="flavor"/> is not yet supported. </exception>
        public static bool TryReadTag(byte[] buffer, int index, int length, NbtFlavor flavor,
                                      [NotNullWhen(true)] out NbtTag? tag, out int bytesConsumed) {
            return GetCodec(flavor).TryReadTag(buffer, index, length, out tag, out bytesConsumed);
        }


        /// <inheritdoc cref="NbtCodec.ReadConcatenatedTags(Stream)"/>
        /// <param name="stream"> Stream to read from. Does not need to be seekable. </param>
        /// <param name="flavor"> Encoding to read the documents with. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="stream"/> or <paramref name="flavor"/> is <c>null</c>. </exception>
        /// <exception cref="NotSupportedException"> <paramref name="flavor"/> is not yet supported. </exception>
        public static IEnumerable<NbtTag> ReadConcatenatedTags(Stream stream, NbtFlavor flavor) {
            return GetCodec(flavor).ReadConcatenatedTags(stream);
        }


        /// <inheritdoc cref="NbtCodec.WriteTag(NbtTag,Stream)"/>
        /// <param name="tag"> Root tag to write. For flavors that allow non-compound roots, <c>null</c>
        /// writes an absent document (a lone <c>TAG_End</c> byte); other flavors require an
        /// <see cref="NbtCompound"/>. A <c>null</c> root name is written as an empty string. </param>
        /// <param name="stream"> Stream to write to. </param>
        /// <param name="flavor"> Encoding to write the document with. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="stream"/> or <paramref name="flavor"/> is <c>null</c>;
        /// or <paramref name="tag"/> is <c>null</c> and the flavor requires a compound root. </exception>
        /// <exception cref="NotSupportedException"> <paramref name="flavor"/> is not yet supported. </exception>
        public static void WriteTag(NbtTag? tag, Stream stream, NbtFlavor flavor) {
            GetCodec(flavor).WriteTag(tag, stream);
        }


        /// <inheritdoc cref="NbtCodec.WriteTag(NbtTag)"/>
        /// <param name="tag"> Root tag to write. For flavors that allow non-compound roots, <c>null</c>
        /// writes an absent document (a lone <c>TAG_End</c> byte); other flavors require an
        /// <see cref="NbtCompound"/>. A <c>null</c> root name is written as an empty string. </param>
        /// <param name="flavor"> Encoding to write the document with. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="flavor"/> is <c>null</c>;
        /// or <paramref name="tag"/> is <c>null</c> and the flavor requires a compound root. </exception>
        /// <exception cref="NotSupportedException"> <paramref name="flavor"/> is not yet supported. </exception>
        public static byte[] WriteTag(NbtTag? tag, NbtFlavor flavor) {
            return GetCodec(flavor).WriteTag(tag);
        }


        static NbtCodec GetCodec(NbtFlavor flavor) {
            if (flavor == null) throw new ArgumentNullException(nameof(flavor));
            return flavor.DefaultCodec;
        }
    }
}

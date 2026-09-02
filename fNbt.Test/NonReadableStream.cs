using System;
using System.IO;

namespace fNbt.Test {
    // A stream the reader must refuse to open
    class NonReadableStream : MemoryStream {
        public override bool CanRead {
            get { return false; }
        }


        public override int ReadByte() {
            throw new NotSupportedException();
        }


        public override int Read(byte[] buffer, int offset, int count) {
            throw new NotSupportedException();
        }
    }
}

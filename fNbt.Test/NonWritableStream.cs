using System;
using System.IO;

namespace fNbt.Test {
    // A stream the writer must refuse to open
    class NonWritableStream : MemoryStream {
        public override bool CanWrite {
            get { return false; }
        }


        public override void WriteByte(byte value) {
            throw new NotSupportedException();
        }


        public override void Write(byte[] buffer, int offset, int count) {
            throw new NotSupportedException();
        }
    }
}

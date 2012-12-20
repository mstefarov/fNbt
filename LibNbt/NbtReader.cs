﻿using System;
using System.IO;
using System.Net;
using System.Text;
using JetBrains.Annotations;

namespace LibNbt {
    sealed class NbtReader : BinaryReader {
        readonly byte[] floatBuffer = new byte[sizeof( float )],
                        doubleBuffer = new byte[sizeof( double )];

        byte[] seekBuffer;
        const int SeekBufferSize = 64 * 1024;
        readonly bool bigEndian;


        public NbtReader( [NotNull] Stream input, bool bigEndian )
            : base( input ) {
            this.bigEndian = bigEndian;
        }


        public NbtTagType ReadTagType() {
            return (NbtTagType)ReadByte();
        }


        public override short ReadInt16() {
            if( BitConverter.IsLittleEndian == bigEndian ) {
                return NbtWriter.Swap( base.ReadInt16() );
            } else {
                return base.ReadInt16();
            }
        }


        public override int ReadInt32() {
            if( BitConverter.IsLittleEndian == bigEndian ) {
                return NbtWriter.Swap( base.ReadInt32() );
            } else {
                return base.ReadInt32();
            }
        }


        public override long ReadInt64() {
            if( BitConverter.IsLittleEndian == bigEndian ) {
                return NbtWriter.Swap( base.ReadInt64() );
            } else {
                return base.ReadInt64();
            }
        }


        public override float ReadSingle() {
            if( BitConverter.IsLittleEndian == bigEndian ) {
                BaseStream.Read( floatBuffer, 0, sizeof( float ) );
                Array.Reverse( floatBuffer );
                return BitConverter.ToSingle( floatBuffer, 0 );
            }
            return base.ReadSingle();
        }


        public override double ReadDouble() {
            if( BitConverter.IsLittleEndian == bigEndian ) {
                BaseStream.Read( doubleBuffer, 0, sizeof( double ) );
                Array.Reverse( doubleBuffer );
                return BitConverter.ToDouble( doubleBuffer, 0 );
            }
            return base.ReadDouble();
        }


        public override string ReadString() {
            short length = ReadInt16();
            if( length < 0 ) {
                throw new NbtFormatException( "Negative string length given!" );
            }
            byte[] stringData = ReadBytes( length );
            return Encoding.UTF8.GetString( stringData );
        }


        public void Skip( int bytesToSkip ) {
            if( bytesToSkip < 0 ) {
                throw new ArgumentOutOfRangeException( "bytesToSkip" );
            } else if( BaseStream.CanSeek ) {
                BaseStream.Position += bytesToSkip;
            } else if( bytesToSkip != 0 ) {
                if( seekBuffer == null )
                    seekBuffer = new byte[SeekBufferSize];
                int bytesDone = 0;
                while( bytesDone < bytesToSkip ) {
                    int readThisTime = BaseStream.Read( seekBuffer, bytesDone, bytesToSkip - bytesDone );
                    if( readThisTime == 0 ) {
                        throw new EndOfStreamException();
                    }
                    bytesDone += readThisTime;
                }
            }
        }


        public void SkipString() {
            short length = ReadInt16();
            if( length < 0 ) {
                throw new NbtFormatException( "Negative string length given!" );
            }
            Skip( length );
        }


        public TagSelector Selector { get; set; }
    }
}
using System;
#if NETCOREAPP
using System.Runtime.CompilerServices;
#endif

namespace fNbt {
    // Arrays that are filled completely before anyone can see them skip the runtime's zeroing
    // on .NET Core. Only large-object-heap sizes qualify: smaller arrays come out of memory the
    // GC has already cleared, so there is nothing to save.
    internal static class ArrayAllocator {
        internal const int LargeObjectThreshold = 85_000;


        // The caller must write every element before the array becomes visible.
        public static T[] ForOverwrite<T>(int length) where T : unmanaged {
#if NETCOREAPP
            if ((long)length * Unsafe.SizeOf<T>() >= LargeObjectThreshold) {
                return GC.AllocateUninitializedArray<T>(length);
            }
#endif
            return new T[length];
        }


        // An exact output buffer must end up written to its last byte; a tree that changed
        // between the sizing and the writing walk would otherwise hand out stale memory.
        public static void EnsureFilled(byte[] buffer, long written) {
            if (written != buffer.Length) {
                throw new InvalidOperationException("The document changed while it was being written.");
            }
        }
    }
}

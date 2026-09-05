using System;

namespace fNbt {
    // Arrays that are filled completely before anyone can see them skip the runtime's zeroing
    // on .NET Core. Only large-object-heap sizes qualify: below that, gen0 memory is cleared in
    // bulk ahead of allocation, so there is nothing to save.
    internal static class ArrayAllocator {
        const int LargeObjectThreshold = 85_000;


        // The caller must write every element before the array becomes visible.
        public static T[] ForOverwrite<T>(int length, long byteLength) {
#if NETCOREAPP
            if (byteLength >= LargeObjectThreshold) {
                return GC.AllocateUninitializedArray<T>(length);
            }
#endif
            return new T[length];
        }
    }
}

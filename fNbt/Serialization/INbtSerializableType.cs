using System.Collections;
using System.Collections.Generic;

namespace fNbt.Serialization {
    /// <summary>
    ///  Type supporting NBT serialization
    ///  
    /// </summary>
    public interface INbtSerializableType {
        /// <summary>
        /// Convert class to NBT format
        /// </summary>
        /// <returns>Serialized NBT tag</returns>
        NbtTag SerializeToNbt();
    }

    /// <summary>
    /// Type supporting NBT deserialization
    /// </summary>
    public interface INbtDeserializableType {
        /// <summary>
        /// Reconstruct class from NBT format
        /// </summary>
        void DeserializeFromNbt(NbtTag tag);
    }
}

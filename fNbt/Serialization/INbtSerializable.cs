using System;
using System.Collections.Generic;
using System.Text;
using fNbt;

namespace fNbt.Serialization
{
    public interface INbtSerializable
    {
        NbtTag Serialize(string tagName);
        void Deserialize(NbtTag value);
    }
}

using System;

namespace fNbt.Serialization {
    /// <summary>
    /// 
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public class NbtSerializableType: Attribute {
        
    }
    /// <summary>
    /// 
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class NbtPropertyName : Attribute{
        /// None
        public string Name { get; set; }
        /// None
        public NbtPropertyName(string name) {
            Name = name;
        }
    }

    /// <summary>
    /// 
    /// </summary>
    public class NotNbtProperty : Attribute{
        
    }
}

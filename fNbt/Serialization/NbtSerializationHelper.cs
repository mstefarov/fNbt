using System;
using System.Collections.Generic;
using System.Linq;


namespace fNbt.Serialization {
    /// <summary>
    /// Provides <c>serialization</c> and <c>deserialization</c> support for primitive types.
    /// <para>The reason is as follows:</para>
    /// <para>In my design, <c>serialization</c> and <c>deserialization</c> will have a strong binding with <c>source generators</c>.</para>
    /// <para>When the <c>source generator</c> automatically generates <c></c> and <c></c> functions,</para>
    /// <para>if there is a relatively simple data type that is not built-in, there is no need to encapsulate the data type at this time.Instead, adding an <c>internal extension method</c> can solve the problem.</para>
    /// <para>In this way, the <c>source generator</c> will work normally with minimal effort, and provide more editability while being portable.</para>
    /// <example>
    /// <code>
    ///      //The source generator will automatically serialize this type
    ///        internal NbtTag SerializeToNbt(this IEnumerable&lt;double&gt; obj) {
    ///         //TOD Serialize IEnumerable&lt;double&gt; then Return
    ///      }
    /// </code>
    /// </example>
    /// </summary>
    public class NbtSerializationHelper {
        #region Serialization support for primitive data types
        /// <summary>
        /// Provide serialization/deserialization support for int32
        /// </summary>
        /// <returns></returns>
        public   NbtTag SerializeToNbt(int obj) {
            return new NbtInt(obj);
        }
        /// <summary>
        /// Provide serialization/deserialization support for int64
        /// </summary>
        /// <returns></returns>
        public   NbtTag SerializeToNbt(long obj) {
            return new NbtLong(obj);
        }
        /// <summary>
        /// Provide serialization/deserialization support for int16
        /// </summary>
        /// <returns></returns>
        public   NbtTag SerializeToNbt(short obj) {
            return new NbtShort(obj);
        }
        /// <summary>
        /// Provide serialization/deserialization support for uint8
        /// </summary>
        /// <returns></returns>
        public   NbtTag SerializeToNbt(byte obj) {
            return new NbtByte(obj);
        }
        /// <summary>
        /// Provide serialization/deserialization support for float32
        /// </summary>
        /// <returns></returns>
        public   NbtTag SerializeToNbt(float obj) {
            return new NbtFloat(obj);
        }
        /// <summary>
        /// Provide serialization/deserialization support for float64(double)
        /// </summary>
        /// <returns></returns>
        public   NbtTag SerializeToNbt(double obj) {
            return new NbtDouble(obj);
        }
        #endregion
        
        #region NBT's built-in array-like data types (including string)
        /// <summary>
        /// Provide serialization/deserialization support for string
        /// </summary>
        /// <returns></returns>
        public   NbtTag SerializeToNbt(string obj) {
            return new NbtString(obj);
        }
        /// <summary>
        /// Provide serialization/deserialization support for IEnumerable&lt;int(int32)&gt;> to NbtIntArray
        /// </summary>
        /// <returns></returns>
        public   NbtTag SerializeToNbt(IEnumerable<int> obj) {
            return new NbtIntArray(obj.ToArray());
        }
        /// <summary>
        /// Provide serialization/deserialization support for IEnumerable&lt;long(int64)&gt;> to NbtLongArray
        /// </summary>
        /// <returns></returns>
        public   NbtTag SerializeToNbt(IEnumerable<long> obj) {
            return new NbtLongArray(obj.ToArray());
        }

        #endregion
        
        #region Speical(to NbtList but only INbtSerializableType or NbtTag)
        /// <summary>
        /// Provide serialization/deserialization support for IEnumerable&lt;NbtTag&gt;> to NbtList
        /// </summary>
        /// <returns></returns>
        public   NbtTag SerializeToNbt(IEnumerable<NbtTag> obj){
            return new NbtList(obj);
        }
        /// <summary>
        /// Provide serialization/deserialization support for IEnumerable&lt;INbtSerializableType&gt;> to NbtList of NbtCompound(or other,but most of the time are NbtCompound)
        /// </summary>
        /// <returns></returns>
        public   NbtTag SerializeToNbt<TSerialization>(IEnumerable<TSerialization> obj) where TSerialization: INbtSerializableType{
            return new NbtList(obj.Select(t=>t.SerializeToNbt()));
        }
        

        #endregion
    }

    /// <summary>
    /// Provides <c>serialization</c> and <c>deserialization</c> support for primitive types
    /// <seealso cref="NbtSerializationHelper"></seealso>
    /// </summary>
    public class NbtDeserializationHelper {
        
        #region Deserialization support for primitive data types
        /// <summary>
        /// Provide serialization/deserialization support for int32
        /// </summary>
        public   void DeserializeFromNbt(NbtTag obj,out int refValue) {
            if (obj is not NbtInt) {
                throw new ArgumentException("value of NbtTag is not Int32");
            }
            refValue = obj.IntValue;
        }
        /// <summary>
        /// Provide serialization/deserialization support for int64
        /// </summary>
        public   void DeserializeFromNbt(NbtTag obj,out long refValue) {
            if (obj is not NbtLong) {
                throw new ArgumentException("value of NbtTag is not Int64");
            }
            refValue = obj.LongValue;
        }
        /// <summary>
        /// Provide serialization/deserialization support for int16
        /// </summary>
        public   void DeserializeFromNbt(NbtTag obj,out short refValue) {
            if (obj is not NbtShort) {
                throw new ArgumentException("value of NbtTag is not Int16");
            }
            refValue = obj.ShortValue;
        }
        /// <summary>
        /// Provide serialization/deserialization support for uint8(byte)
        /// </summary>
        public   void DeserializeFromNbt(NbtTag obj,out byte refValue) {
            if (obj is not NbtByte) {
                throw new ArgumentException("value of NbtTag is not UInt8");
            }
            refValue = obj.ByteValue;
        }
        /// <summary>
        /// Provide serialization/deserialization support for uint8(byte)
        /// </summary>
        public   void DeserializeFromNbt(NbtTag obj,out float refValue) {
            if (obj is not NbtFloat) {
                throw new ArgumentException("value of NbtTag is not float32");
            }
            refValue = obj.FloatValue;
        }
        /// <summary>
        /// Provide serialization/deserialization support for float64(double)
        /// </summary>
        public   void DeserializeFromNbt(NbtTag obj,out double refValue) {
            if (obj is not NbtFloat) {
                throw new ArgumentException("value of NbtTag is not double(float64)");
            }
            refValue = obj.DoubleValue;
        }
        #endregion
        
        #region NBT's built-in array-like data types (including string)
        /// <summary>
        /// Provide serialization/deserialization support for string
        /// </summary>
        public   void DeserializeFromNbt(NbtTag obj,out string refValue) {
            if (obj is not NbtString) {
                throw new ArgumentException("value of NbtTag is not String");
            }
            refValue = obj.StringValue;
        }
        /// <summary>
        /// Provide serialization/deserialization support for IEnumerable&lt;int(int32)&gt;> from NbtIntArray
        /// </summary>
        public   void DeserializeFromNbt(NbtTag obj,out IEnumerable<int> refValue) {
            if (obj is not NbtIntArray) {
                throw new ArgumentException("value of NbtTag is not NbtIntArray");
            }
            refValue = obj.IntArrayValue;
        }
        /// None
        public   void DeserializeFromNbt(NbtTag obj,out int[] refValue) {
            if (obj is not NbtIntArray) {
                throw new ArgumentException("value of NbtTag is not NbtIntArray");
            }
            refValue = obj.IntArrayValue;
        }
        /// None
        public   void DeserializeFromNbt(NbtTag obj,out List<int> refValue) {
            if (obj is not NbtIntArray) {
                throw new ArgumentException("value of NbtTag is not NbtIntArray");
            }
            refValue = obj.IntArrayValue.ToList();
        }
        /// <summary>
        /// Provide serialization/deserialization support for IEnumerable&lt;int(int32)&gt;> from NbtIntArray
        /// </summary>
        public   void DeserializeFromNbt(NbtTag obj,out IEnumerable<long> refValue) {
            if (obj is not NbtLongArray) {
                throw new ArgumentException("value of NbtTag is not NbtLongArray");
            }
            refValue = obj.LongArrayValue;
        }
        /// None
        public   void DeserializeFromNbt(NbtTag obj,out List<long> refValue) {
            if (obj is not NbtLongArray) {
                throw new ArgumentException("value of NbtTag is not NbtLongArray");
            }
            refValue = obj.LongArrayValue.ToList();
        }
        /// None
        public   void DeserializeFromNbt(NbtTag obj,out long[] refValue) {
            if (obj is not NbtLongArray) {
                throw new ArgumentException("value of NbtTag is not NbtLongArray");
            }
            refValue = obj.LongArrayValue;
        }
        #endregion
        
        #region Speical(to NbtList but only INbtDeserializableType or NbtTag)
        /// <summary>
        /// Provide serialization/deserialization support for List of INbtDeserializable
        /// </summary>
        public   void DeserializeFromNbt<T>(NbtTag obj,out IEnumerable<T> refValue)where T: INbtDeserializableType,new(){
            if (obj is not NbtList list) {
                throw new ArgumentException("value of NbtTag is not List");
            }
            
            refValue = list.ToArray().Select(v => {
                var a = new T();a.DeserializeFromNbt(v); return a;});
        }
        
        

        #endregion



    }
}

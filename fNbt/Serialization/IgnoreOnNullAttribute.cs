using System;
using System.Collections.Generic;
using System.Text;

namespace fNbt.Serialization
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    public sealed class IgnoreOnNullAttribute : Attribute { }
}

using System;

namespace fNbt.Serialization
{
	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
	public class NbtPropertyAttribute : Attribute
	{
		public string Name { get; }
		public bool HideDefault { get; }

		public NbtPropertyAttribute(string name = null, bool hideDefault = true)
		{
			Name = name;
			HideDefault = hideDefault;
		}
	}
}

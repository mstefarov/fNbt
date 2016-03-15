using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Explicit)]
public struct FloatAndUIntUnion {
	[FieldOffset(0)]
	public uint UInt32Bits;
	[FieldOffset(0)]
	public float FloatValue;
}

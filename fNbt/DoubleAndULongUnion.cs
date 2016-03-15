using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Explicit)]
public struct DoubleAndULongUnion {
	[FieldOffset(0)]
	public ulong UInt64Bits;
	[FieldOffset(0)]
	public double DoubleValue;
}

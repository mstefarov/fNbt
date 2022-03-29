using fNbt.Serialization;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace fNbt.Test
{
	[TestFixture]
	public sealed class NbtSerializerTests
	{
		[Test]
		public void BuildFromTagTest()
		{
			CheckFromTag(false);
		}

		[Test]
		public void FillFromTagTest()
		{
			CheckFromTag(true);
		}

		[Test]
		public void FillTest()
		{
			var valuesSet = new List<string>() { "testVal1", "test_val_2", "TestVal3", "TEST_VAL_4" };

			var original = new FillTestClass();
			original.GetOnlyTestStringList.AddRange(valuesSet);
			original.GetOnlyTestClassProperty.EasyIntProperty = 321;
			original.GetOnlyTestClassProperty.EasyStringProperty = "ESP_TEST_FILL";

			var tag = NbtSerializer.SerializeObject(original);

			var result = NbtSerializer.DeserializeObject<FillTestClass>(tag);

			Assert.IsNotNull(result);

			Assert.AreEqual(original.GetOnlyTestClassProperty.EasyIntProperty, result.GetOnlyTestClassProperty.EasyIntProperty);
			Assert.AreEqual(original.GetOnlyTestClassProperty.EasyStringProperty, result.GetOnlyTestClassProperty.EasyStringProperty);
			CollectionAssert.AreEquivalent(original.GetOnlyTestStringList, result.GetOnlyTestStringList);
		}

		[Test]
		public void FieldsAndPropertiesTest()
		{
			var raw = new FieldsAndPropertiesTestClass();
			var tag = NbtSerializer.SerializeObject(raw);

			Assert.IsNotNull(tag);

			Assert.IsFalse(tag.Contains(nameof(raw.HidenTestIntFiled)));
			Assert.IsFalse(tag.Contains(nameof(raw.HidenDefaultTestIntFiled)));

			Assert.IsFalse(tag.Contains(nameof(raw.HidenTestIntProperty)));
			Assert.IsFalse(tag.Contains(nameof(raw.HidenDefaultTestIntProperty)));

			AssertTagValue<NbtInt>(tag, nameof(raw.DefaultTestIntFiled), raw.DefaultTestIntFiled, t => t.Value);
			AssertTagValue<NbtInt>(tag, nameof(raw.TestIntFiled), raw.TestIntFiled, t => t.Value);
			AssertTagValue<NbtInt>(tag, "PrivateTestIntFiled", 3, t => t.Value);
			AssertTagValue<NbtInt>(tag, FieldsAndPropertiesTestClass.NameForNamedTestIndFiled, raw.NamedTestIntFiled, t => t.Value);

			AssertTagValue<NbtInt>(tag, nameof(raw.DefaultTestIntProperty), raw.DefaultTestIntProperty, t => t.Value);
			AssertTagValue<NbtInt>(tag, nameof(raw.TestIntProperty), raw.TestIntProperty, t => t.Value);
			AssertTagValue<NbtInt>(tag, nameof(raw.OnlyGetTestIntProperty), raw.OnlyGetTestIntProperty, t => t.Value);
			AssertTagValue<NbtInt>(tag, "PrivateTestIntProperty", 8, t => t.Value);
			AssertTagValue<NbtInt>(tag, FieldsAndPropertiesTestClass.NameForNamedTestIndProperty, raw.NamedTestIntProperty, t => t.Value);
		}

		[Test]
		public void TypesTest()
		{
			var raw = new TypesTestClass();
			var tag = NbtSerializer.SerializeObject(raw);
			var deserialized = NbtSerializer.DeserializeObject<TypesTestClass>(tag);

			Assert.IsNotNull(tag);

			Assert.AreEqual(raw.BoolFalseTestProperty, deserialized.BoolFalseTestProperty);
			Assert.AreEqual(raw.BoolTrueTestProperty, deserialized.BoolTrueTestProperty);
			Assert.AreEqual(raw.ByteArrayTestProperty, deserialized.ByteArrayTestProperty);
			Assert.AreEqual(raw.IntArrayTestProperty, deserialized.IntArrayTestProperty);
			Assert.AreEqual(raw.ByteTestProperty, deserialized.ByteTestProperty);
			Assert.AreEqual(raw.SByteTestProperty, deserialized.SByteTestProperty);
			Assert.AreEqual(raw.ShortTestProperty, deserialized.ShortTestProperty);
			Assert.AreEqual(raw.UShortTestProperty, deserialized.UShortTestProperty);
			Assert.AreEqual(raw.IntTestProperty, deserialized.IntTestProperty);
			Assert.AreEqual(raw.UIntTestProperty, deserialized.UIntTestProperty);
			Assert.AreEqual(raw.LongTestProperty, deserialized.LongTestProperty);
			Assert.AreEqual(raw.ULongTestProperty, deserialized.ULongTestProperty);

			Assert.AreEqual(raw.NbtIntProperty.Value, deserialized.NbtIntProperty.Value);

			AssertTagValue<NbtByte>(tag, nameof(raw.BoolFalseTestProperty), raw.BoolFalseTestProperty, t => Convert.ToBoolean(t.Value));
			AssertTagValue<NbtByte>(tag, nameof(raw.BoolTrueTestProperty), raw.BoolTrueTestProperty, t => Convert.ToBoolean(t.Value));
			AssertTagValue<NbtByteArray>(tag, nameof(raw.ByteArrayTestProperty), raw.ByteArrayTestProperty, t => t.Value);
			AssertTagValue<NbtIntArray>(tag, nameof(raw.IntArrayTestProperty), raw.IntArrayTestProperty, t => t.Value);
			AssertTagValue<NbtByte>(tag, nameof(raw.ByteTestProperty), raw.ByteTestProperty, t => t.Value);
			AssertTagValue<NbtByte>(tag, nameof(raw.SByteTestProperty), raw.SByteTestProperty, t => (sbyte)t.Value);
			AssertTagValue<NbtShort>(tag, nameof(raw.ShortTestProperty), raw.ShortTestProperty, t => t.Value);
			AssertTagValue<NbtShort>(tag, nameof(raw.UShortTestProperty), raw.UShortTestProperty, t => (ushort)t.Value);
			AssertTagValue<NbtInt>(tag, nameof(raw.IntTestProperty), raw.IntTestProperty, t => t.Value);
			AssertTagValue<NbtInt>(tag, nameof(raw.UIntTestProperty), raw.UIntTestProperty, t => (uint)t.Value);
			AssertTagValue<NbtLong>(tag, nameof(raw.LongTestProperty), raw.LongTestProperty, t => t.Value);
			AssertTagValue<NbtLong>(tag, nameof(raw.ULongTestProperty), raw.ULongTestProperty, t => (ulong)t.Value);

			AssertTagValue<NbtInt>(tag, nameof(raw.NbtIntProperty), raw.NbtIntProperty.Value, t => t.Value);
		}

		private void CheckFromTag(bool fill)
		{
			var stringValue = "test_string_value_rv";
			var intValue = 882882;

			var shadow = new EasyTestClass();

			var tag = new NbtCompound();
			tag.Add(new NbtString(nameof(shadow.EasyStringProperty), stringValue));
			tag.Add(new NbtInt(nameof(shadow.EasyIntProperty), intValue));

			EasyTestClass result = null;

			if (fill)
			{
				result = shadow;
				NbtSerializer.FillObject(result, tag);
			}
			else
			{
				result = NbtSerializer.DeserializeObject<EasyTestClass>(tag);
			}

			Assert.IsNotNull(result);

			Assert.AreEqual(stringValue, result.EasyStringProperty);
			Assert.AreEqual(intValue, result.EasyIntProperty);
		}

		private void AssertTagValue<TExpected>(NbtCompound parentTag, string expectedTagName, object expectedValue, Func<TExpected, object> getValue) where TExpected : NbtTag
		{
			Assert.True(parentTag.TryGet(expectedTagName, out NbtTag tag), 
				$"expected tag [{expectedTagName}] is not contains.");

			Assert.IsAssignableFrom<TExpected>(tag, 
				$"actual tag type is [{tag.GetType()}], but expected [{typeof(TExpected)}]");

			var actual = getValue((TExpected)tag);
			Assert.AreEqual(expectedValue, actual);
		}

		#region test classes

		public class FillTestClass
		{
			[NbtProperty] public List<string> GetOnlyTestStringList { get; } = new List<string>();
			[NbtProperty] public EasyTestClass GetOnlyTestClassProperty { get; } = new EasyTestClass();
		}

		public class EasyTestClass
		{
			[NbtProperty] public string EasyStringProperty { get; set; } = "easy property value";
			[NbtProperty] public int EasyIntProperty { get; set; } = 123456789;
		}

		public class TypesTestClass
		{
			[NbtProperty(hideDefault: false)] public bool BoolFalseTestProperty { get; set; } = false;
			[NbtProperty] public bool BoolTrueTestProperty { get; set; } = true;
			[NbtProperty] public byte[] ByteArrayTestProperty { get; set; } = new byte[] { 1, 2, 3, 4 };
			[NbtProperty] public int[] IntArrayTestProperty { get; set; } = new int[] { 5, 6, 7, 8 };
			[NbtProperty] public byte ByteTestProperty { get; set; } = 1;
			[NbtProperty] public sbyte SByteTestProperty { get; set; } = 2;
			[NbtProperty] public short ShortTestProperty { get; set; } = 3;
			[NbtProperty] public ushort UShortTestProperty { get; set; } = 4;
			[NbtProperty] public int IntTestProperty { get; set; } = 5;
			[NbtProperty] public uint UIntTestProperty { get; set; } = 6;
			[NbtProperty] public ulong LongTestProperty { get; set; } = 7;
			[NbtProperty] public ulong ULongTestProperty { get; set; } = 8;

			[NbtProperty] public NbtInt NbtIntProperty { get; set; } = new NbtInt(100);
		}

		public class FieldsAndPropertiesTestClass
		{
			public int HidenTestIntFiled = 1;
			[NbtProperty] public int HidenDefaultTestIntFiled = 0;
			[NbtProperty(hideDefault: false)] public int DefaultTestIntFiled = 0;
			[NbtProperty] public int TestIntFiled = 2;

			[NbtProperty] private int PrivateTestIntFiled = 3;

			public const string NameForNamedTestIndFiled = "named_test_int_field";
			[NbtProperty(NameForNamedTestIndFiled)] public int NamedTestIntFiled = 4;

			public int HidenTestIntProperty { get; set; } = 5;
			[NbtProperty] public int HidenDefaultTestIntProperty { get; set; } = 0;
			[NbtProperty(hideDefault: false)] public int DefaultTestIntProperty { get; set; } = 0;
			[NbtProperty] public int TestIntProperty { get; set; } = 6;
			[NbtProperty] public int OnlyGetTestIntProperty { get; } = 7;
			[NbtProperty] private int PrivateTestIntProperty { get; set; } = 8;

			public const string NameForNamedTestIndProperty = "named_test_int_property";
			[NbtProperty(NameForNamedTestIndProperty)] public int NamedTestIntProperty { get; set; } = 9;
		}

		#endregion
	}
}

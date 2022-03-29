using NUnit.Framework;

namespace fNbt.Serialization.Test {
    [TestFixture]
    public class DynamicConverterTests {

        [Test]
        public void PrimitiveTest() {
            NbtTag intTag = NbtConvert.MakeTag("derp", 1);
            Assert.IsInstanceOf<NbtInt>(intTag);

			Assert.AreEqual(intTag.IntValue, 1);
        }
    }
}
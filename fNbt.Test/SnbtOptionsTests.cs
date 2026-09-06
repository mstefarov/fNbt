using System;

namespace fNbt.Test {
    // The process-wide SnbtOptions default, and how new options and ToSnbt() pick it up.
    // The default is process-wide, so a cleanup restores it and the class never runs in parallel.
    [TestClass]
    [DoNotParallelize]
    public class SnbtOptionsTests {
        [TestCleanup]
        public void RestoreDefaults() {
            SnbtOptions.DefaultWriteLayout = SnbtLayout.Compact;
        }


        [TestMethod]
        public void DefaultLayoutIsCompactAndSeedsNewOptions() {
            Assert.AreEqual(SnbtLayout.Compact, SnbtOptions.DefaultWriteLayout);
            Assert.AreEqual(SnbtLayout.Compact, new SnbtOptions().WriteLayout);
            NbtCompound tag = new NbtCompound { new NbtInt("a", 1) };
            Assert.AreEqual("{a:1}", tag.ToSnbt());

            SnbtOptions before = new SnbtOptions();
            SnbtOptions.DefaultWriteLayout = SnbtLayout.Spaced;
            Assert.AreEqual(SnbtLayout.Spaced, new SnbtOptions().WriteLayout);
            Assert.AreEqual("{a: 1}", tag.ToSnbt());
            // Instances created earlier keep their own value
            Assert.AreEqual(SnbtLayout.Compact, before.WriteLayout);
            Assert.AreEqual("{a:1}", tag.ToSnbt(before));
        }


        [TestMethod]
        public void SettersRejectUndefinedLayouts() {
            Assert.Throws<ArgumentOutOfRangeException>(() => SnbtOptions.DefaultWriteLayout = (SnbtLayout)3);
            Assert.Throws<ArgumentOutOfRangeException>(() => SnbtOptions.DefaultWriteLayout = (SnbtLayout)(-1));
            Assert.AreEqual(SnbtLayout.Compact, SnbtOptions.DefaultWriteLayout);
            SnbtOptions options = new SnbtOptions();
            Assert.Throws<ArgumentOutOfRangeException>(() => options.WriteLayout = (SnbtLayout)3);
            Assert.AreEqual(SnbtLayout.Compact, options.WriteLayout);
        }


        [TestMethod]
        public void ToSnbtRejectsNullOptions() {
            Assert.Throws<ArgumentNullException>(() => new NbtInt(1).ToSnbt(null));
        }
    }
}

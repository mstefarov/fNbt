using System;
using System.Collections.Generic;
using System.Text;
using fNbt.Serialization;
using NUnit.Framework;

namespace fNbt.Test {
    [TestFixture]
    public sealed class DeserializationTest {
        [Test]
        public void TestSerialize() {
            PersonalCareer personalCareer = new PersonalCareer() {
                Name = "Mike",
                Http = "https://mike.iiiiicuasdi",
                Mods = [
                    new ModInfo() {
                        Version = "10.0",
                        Author = "Mike",
                        Date = 17777777,
                        Pos = [1000, 11000, 5550]
                    }
                ]
            };
            NbtTag serializeToNbt = personalCareer.SerializeToNbt();
            serializeToNbt.Name = "Mike";
            Console.WriteLine(serializeToNbt);
            new NbtFile{RootTag = (NbtCompound)serializeToNbt}.SaveToFile("TestFiles/TestSerialize.personalCareer.nbt", NbtCompression.None);
        }

        [Test]
        public void TestDeserialize() {
            PersonalCareer personalCareer = new PersonalCareer();
            NbtFile nbtFile = new("TestFiles/TestSerialize.personalCareer.nbt");
            personalCareer.DeserializeFromNbt(nbtFile.RootTag);
            Console.WriteLine(nbtFile.RootTag);
            Console.WriteLine(personalCareer);
        }
        
    }

    [NbtSerializableType]
    public partial class PersonalCareer {
        public IEnumerable<ModInfo> Mods { get; set; }
        [NbtPropertyName("RealName")]
        public string Name { get; set; }
        [NotNbtProperty]
        public string Http { get; set; }

        public override string ToString() {
            var a=new StringBuilder("""
                                    Name is {Name}
                                    Http is {Http}
                                    Has Many Mod,such as

                                    """);
            foreach (ModInfo modInfo in Mods) {
                a.AppendLine(modInfo.ToString());
            }
            return a.ToString();
        }
    }
    [NbtSerializableType]
    public partial class ModInfo {
        public string Version { get; set; }
        public string Author { get; set; }
        public long Date { get; set; }
        public List<long> Pos { get; set; }

        public override string ToString() {
            var p= new StringBuilder(Version + ", " + Author + ", " + Date+"\n");
            foreach (var item in Pos) {
                p.AppendLine(item.ToString());
            }
            return p.ToString();
        }
    }
}

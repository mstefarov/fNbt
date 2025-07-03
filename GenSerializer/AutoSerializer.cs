using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using fNbt;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GenSerializer {
    [Generator]
    public class AutoSerializer: IIncrementalGenerator{
        public void Initialize(IncrementalGeneratorInitializationContext context) {
            IncrementalValuesProvider<INamedTypeSymbol> incrementalValuesProvider = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: Utils.FindNbtSerializableType,
                transform: Utils.GetNbtSerializableType);
            
            context.RegisterSourceOutput(incrementalValuesProvider,Excute);
            
        }


        private void Excute(SourceProductionContext context, INamedTypeSymbol classAuto) {
            //Gets properties that do not contain the NotNbtProperty Attribute and then foreach
            IEnumerable<IPropertySymbol> propertySymbols = classAuto.GetMembers()
                                                                    .Where(v=>v is IPropertySymbol propertySymbol
                                                                                           && !propertySymbol.GetAttributes()
                                                                                               .Any(p=>p.ToString().Contains("NotNbtProperty")))
                                                                    .Cast<IPropertySymbol>().ToArray();

            string genSource = $$"""
                                namespace {{classAuto.ContainingNamespace.ToDisplayString()}}{
                                {{classAuto.DeclaredAccessibility.ToString().ToLower()}} partial class {{classAuto.Name}} : fNbt.Serialization.INbtDeserializableType,fNbt.Serialization.INbtSerializableType
                                    {
                                    {{GetSerialization(propertySymbols)}}
                                    {{GetDeserialization(propertySymbols)}}
                                    }
                                }
                                """;
            context.AddSource($"{classAuto.ContainingNamespace.ToDisplayString()}.{classAuto.Name}Nbt.g.cs",genSource);
            return;
        }

        private static string GetNbtPropertyName(IPropertySymbol item)
        {
            // 获取 NbtPropertyName 特性
            AttributeData? attributeData = item.GetAttributes()
                                               .FirstOrDefault(v => v.AttributeClass?.Name.Contains("NbtPropertyName") == true);
            // 如果没有找到特性，则使用 item.Name 作为默认值
            if (attributeData == null)
            {
                return item.Name;
            }
            // 获取特性的命名参数
            ImmutableArray<KeyValuePair<string, TypedConstant>> keyValuePairs = attributeData.NamedArguments;
            // 如果没有命名参数，则使用 item.Name 作为默认值
            if (keyValuePairs.IsDefaultOrEmpty) //注意这里要判断是否为default or empty
            {
                return item.Name;
            }
            // 查找名为 "Name" 的键值对
            KeyValuePair<string, TypedConstant> nameValuePair = keyValuePairs.FirstOrDefault(v => v.Key == "Name");
            // 如果找到了 "Name" 键值对，并且值不为空，则返回该值
            if (!String.IsNullOrEmpty(nameValuePair.Key) && nameValuePair.Value.Value is string propertyName)
            {
                return propertyName;
            }
            // 否则，返回 item.Name 作为默认值
            return item.Name;
        }
         
        private string GetSerialization(IEnumerable<IPropertySymbol> propertySymbols) {
            StringBuilder sb = new("		public NbtTag SerializeToNbt(){\n");
            sb.AppendLine("\t\t\tvar helper=new fNbt.Serialization.NbtSerializationHelper();");
            sb.AppendLine("\t\t\tvar result = new fNbt.NbtCompound();");
            sb.AppendLine("\t\t\tfNbt.NbtTag obj;");
            foreach (IPropertySymbol item in propertySymbols) {
                //Okay, here's the translation and suggested names

                string propertyName = GetNbtPropertyName(item);
                if (item.Type.AllInterfaces.Any(v=>v.Name=="INbtSerializableType")) {
                    sb.AppendLine($"\t\t\tobj = {item.Name}.SerializeToNbt();");
                    sb.AppendLine($"\t\t\tobj.Name = \"{propertyName}\";");
                    sb.AppendLine($"\t\t\tresult.Add(obj)");
                    continue;
                }
                sb.AppendLine($"\t\t\tobj = helper.SerializeToNbt({item.Name});");
                sb.AppendLine($"\t\t\tobj.Name = \"{propertyName}\";");
                sb.AppendLine($"\t\t\tresult.Add(obj);");
            }
            sb.AppendLine("\t\t\treturn result;");
            sb.AppendLine("		}");
            return sb.ToString();
        }


        private string GetDeserialization(IEnumerable<IPropertySymbol> propertySymbols) {
            StringBuilder sb = new("		public void DeserializeFromNbt(NbtTag tag){\n");
            sb.AppendLine("\t\t\tif(tag is not NbtCompound nbtTag) throw new System.ArgumentException(\"tag was not a NbtCompound\");");
            sb.AppendLine("\t\t\tvar helper =new fNbt.Serialization.NbtDeserializationHelper();");
            foreach (IPropertySymbol item in propertySymbols) {
                string propertyName = GetNbtPropertyName(item);
                
                if (item.Type.AllInterfaces.Any(v=>v.Name=="INbtDeserializableType")) {
                    sb.AppendLine($"\t\t\t{item.Name} = new();");
                    sb.AppendLine($"\t\t\t{item.Name}.DeserializeFromNbt(nbtTag[\"{propertyName}\"]);");
                    //sb.AppendLine($"\t\t\tresult.Add({item.Name}.SerializeToNbt())");
                    continue;
                }
                //sb.AppendLine($"\t\t\tresult.Add(fNbt.Serialization.NbtSerializationHelper.SerializeToNbt({item.Name}));");
                sb.AppendLine($"\t\t\tvar {item.Name}Temp = {item.Name};");
                sb.AppendLine($"\t\t\thelper.DeserializeFromNbt(nbtTag[\"{propertyName}\"],out {item.Name}Temp);");
                sb.AppendLine($"\t\t\t{item.Name} = {item.Name}Temp;");
            }
            //sb.AppendLine("\t\t\treturn result;");
            sb.AppendLine("		}");
            return sb.ToString();
        }
    }


    public static class Utils {

        #region Dialog

        public static DiagnosticDescriptor GetOrSetMethonIsNull =
            new("NBT001", "Can't Find Set or Get", "NbtSerialize Property must have get and set , but '{0}' don't",
                "Generator", DiagnosticSeverity.Error, true);


        public static void ReportGetOrSetMethonIsNullERROR(this SourceProductionContext context, IPropertySymbol property) {
            context.ReportDiagnostic(Diagnostic.Create(GetOrSetMethonIsNull, property.Locations[0], property.Name));
        }

        #endregion
        /// <summary>
        /// Find Class which has NbtSerializableType(Attribute) 
        /// </summary>
        /// <param name="node"></param>
        /// <param name="arg2"></param>
        /// <returns></returns>
        public static bool FindNbtSerializableType(SyntaxNode node, CancellationToken arg2) {
            if (node is not ClassDeclarationSyntax cNode|| cNode.AttributeLists.Count==0) return false;
            return cNode.AttributeLists.Any(attList => attList.Attributes.Any(
                                           att => att.Name.ToString().Contains("NbtSerializableType")));
        }

        /// <summary>
        /// To SemanticModel/Symbol
        /// </summary>
        /// <param name="context"></param>
        /// <param name="arg2"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static INamedTypeSymbol GetNbtSerializableType(GeneratorSyntaxContext context, CancellationToken arg2) {
            return context.SemanticModel.GetDeclaredSymbol((ClassDeclarationSyntax)context.Node) as INamedTypeSymbol ?? throw new ArgumentException($"{context.Node.ToFullString()} is not a supported type");
        }
    }
}

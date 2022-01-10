using System;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Megasware128.InternalToPublic
{
    [Generator]
    public class InternalToPublicGenerator : ISourceGenerator
    {
        private const string attributeText = @"
using System;
using System.Reflection;
namespace Megasware128.InternalToPublic
{
    [AttributeUsage(AttributeTargets.Assembly, Inherited = false, AllowMultiple = true)]
    public sealed class InternalToPublicAttribute : Attribute
    {
        public InternalToPublicAttribute(string assemblyName, string typeName)
        {
        }
    }
}
";

        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForPostInitialization(i => i.AddSource("InternalToPublic.g.cs", attributeText));
        }

        public void Execute(GeneratorExecutionContext context)
        {
            try
            {
                var attributes = context.Compilation.Assembly.GetAttributes().Where(a => a.AttributeClass.Name == "InternalToPublicAttribute");

                foreach (var attribute in attributes)
                {
                    var assemblyName = attribute.ConstructorArguments[0].Value.ToString();
                    var typeName = attribute.ConstructorArguments[1].Value.ToString();

                    var assemblyId = context.Compilation.SourceModule.ReferencedAssemblies.FirstOrDefault(a => a.Name == assemblyName);

                    var assembly = Assembly.Load(assemblyId.GetDisplayName(fullKey: true));

                    if (assembly == null)
                    {
                        throw new Exception($"Could not find assembly {assemblyName}");
                    }

                    var internalType = assembly.GetType(typeName);

                    var publicType = assembly.GetTypes().First(t => t.IsPublic);

                    if (internalType == null)
                    {
                        throw new Exception($"Could not find type {typeName} in assembly {assemblyName}");
                    }

                    var stringBuilder = new StringBuilder(@"using System.Reflection;


namespace Megasware128.InternalToPublic
{
    static class ");
                    stringBuilder.Append(internalType.Name);
                    stringBuilder.AppendLine("{");

                    stringBuilder.AppendLine($"private static Type internalType = typeof({publicType}).Assembly.GetType(\"{typeName}\");");

                    foreach (var method in internalType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic))
                    {
                        stringBuilder.AppendLine($"public static {method.ReturnType.FullName} {method.Name}(");

                        var parameters = method.GetParameters();

                        for (var i = 0; i < parameters.Length; i++)
                        {
                            var parameter = parameters[i];

                            stringBuilder.Append($"{parameter.ParameterType.FullName} {parameter.Name}");

                            if (i < parameters.Length - 1)
                            {
                                stringBuilder.Append(", ");
                            }
                        }

                        stringBuilder.Append(")");

                        stringBuilder.AppendLine("{");

                        stringBuilder.AppendLine($"return (({method.ReturnType.FullName})internalType.GetMethod(\"{method.Name}\", BindingFlags.Static | BindingFlags.NonPublic)");
                        stringBuilder.Append(".Invoke(null, new object[]{");

                        for (var i = 0; i < parameters.Length; i++)
                        {
                            var parameter = parameters[i];

                            stringBuilder.Append($"{parameter.Name}");

                            if (i < parameters.Length - 1)
                            {
                                stringBuilder.Append(", ");
                            }
                        }

                        stringBuilder.Append("}));");

                        stringBuilder.AppendLine("}");
                    }

                    stringBuilder.AppendLine("}");
                    stringBuilder.Append("}");

                    context.AddSource(internalType.Name + ".g.cs", stringBuilder.ToString());
                }
            }
            catch (Exception ex)
            {
                // Write exception to diagnostic
                context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor("InternalToPublicGeneratorException",
                        "InternalToPublicGeneratorException",
                        "InternalToPublicGeneratorException: " + ex,
                        "InternalToPublicGenerator",
                        DiagnosticSeverity.Error,
                        true),
                    Location.None));

                throw;
            }
        }
    }
}

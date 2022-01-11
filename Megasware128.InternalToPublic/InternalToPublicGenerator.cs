using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Megasware128.InternalToPublic
{
    [Generator]
    public class InternalToPublicGenerator : ISourceGenerator
    {
        private const string attributeText = @"
using System;
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
            var mainSyntaxTree = context.Compilation.SyntaxTrees
                                      .First(x => x.HasCompilationUnitRoot);

            var directory = Path.GetDirectoryName(mainSyntaxTree.FilePath);

            // Read project.assets.json
            var projectFile = Path.Combine(directory, "obj", "project.assets.json");

            using (var stream = File.OpenRead(projectFile))
            {
                var jsonDocument = JsonDocument.Parse(stream);

                var restore = jsonDocument.RootElement.GetProperty("project").GetProperty("restore");
                var packagesPath = restore.GetProperty("packagesPath").GetString();
                var targetFramework = jsonDocument.RootElement.GetProperty("targets").EnumerateObject().First().Value;

                var attributes = context.Compilation.Assembly.GetAttributes().Where(a => a.AttributeClass.Name == "InternalToPublicAttribute");

                foreach (var attribute in attributes)
                {
                    var assemblyName = attribute.ConstructorArguments[0].Value.ToString();
                    var typeName = attribute.ConstructorArguments[1].Value.ToString();

                    var assemblyId = context.Compilation.SourceModule.ReferencedAssemblies.FirstOrDefault(a => a.Name == assemblyName);

                    var assembly = default(Assembly);

                    try
                    {
                        assembly = Assembly.Load(assemblyId.GetDisplayName(fullKey: true));
                    }
                    catch
                    {
                        var library = targetFramework.EnumerateObject().FirstOrDefault(l => l.Name.StartsWith(assemblyName));

                        var assemblyPath = Path.Combine(packagesPath, library.Name, library.Value.GetProperty("runtime").EnumerateObject().First().Name);

                        assembly = Assembly.LoadFile(assemblyPath);
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
                        if (method.ReturnType.IsGenericType) continue;

                        var methodbuilder = new StringBuilder($"public static {(method.ReturnType.IsNotPublic ? "object" : method.ReturnType.FullName)} {method.Name}(");

                        methodbuilder.Replace("System.Void", "void");

                        var parameters = method.GetParameters();

                        for (var i = 0; i < parameters.Length; i++)
                        {
                            var parameter = parameters[i];

                            if (parameter.ParameterType.IsGenericType) goto Skip;

                            methodbuilder.Append($"{(parameter.ParameterType.IsNotPublic ? "object" : parameter.ParameterType.FullName)} {parameter.Name}");

                            if (i < parameters.Length - 1)
                            {
                                methodbuilder.Append(", ");
                            }
                        }

                        methodbuilder.Append(")");

                        methodbuilder.AppendLine("{");
                        if (method.ReturnType != typeof(void))
                        {
                            methodbuilder.Append("return ");
                        }
                        if (!method.ReturnType.IsNotPublic && method.ReturnType != typeof(void))
                        {
                            methodbuilder.Append($"({method.ReturnType.FullName})");
                        }
                        methodbuilder.Append($"internalType.GetMethod(\"{method.Name}\", BindingFlags.Static | BindingFlags.NonPublic, null, new Type[] {{");

                        for (var i = 0; i < parameters.Length; i++)
                        {
                            var parameter = parameters[i];

                            methodbuilder.Append($"{parameter.Name}.GetType()");

                            if (i < parameters.Length - 1)
                            {
                                methodbuilder.Append(", ");
                            }
                        }

                        methodbuilder.Append("}, null)");

                        methodbuilder.Append(".Invoke(null, new object[]{");

                        for (var i = 0; i < parameters.Length; i++)
                        {
                            var parameter = parameters[i];

                            methodbuilder.Append($"{parameter.Name}");

                            if (i < parameters.Length - 1)
                            {
                                methodbuilder.Append(", ");
                            }
                        }

                        methodbuilder.Append("});");

                        methodbuilder.AppendLine("}");

                        stringBuilder.AppendLine(methodbuilder.ToString());

                    Skip: continue;
                    }

                    stringBuilder.AppendLine("}");
                    stringBuilder.Append("}");

                    context.AddSource(internalType.Name + ".g.cs", stringBuilder.ToString());
                }
            }
        }
    }
}

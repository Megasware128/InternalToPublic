using System;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

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
    sealed class InternalToPublicAttribute : Attribute
    {
        public InternalToPublicAttribute()
        {
        }
        public InternalToPublicAttribute(string assemblyName, string typeName)
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies().First(a => a.FullName == assemblyName);
            var type = assembly.GetType(typeName);
            InternalType = type;
        }
        public Type InternalType { get; set; }
    }
}
";

        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForPostInitialization(i => i.AddSource("InternalToPublic", attributeText));
        }

        public void Execute(GeneratorExecutionContext context)
        {
            var attributes = context.Compilation.Assembly.GetAttributes().Where(a => a.AttributeClass.Name == "InternalToPublicAttribute");

            foreach (var attribute in attributes)
            {
                var internalType = (Type)attribute.ConstructorArguments[0].Value;

                var stringBuilder = new StringBuilder(@"using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;


namespace Megasware128.InternalToPublic
{
    public static class ");
                stringBuilder.Append(internalType.Name);
                stringBuilder.Append("{");

                foreach(var method in internalType.GetMethods())
                {
                    stringBuilder.Append("public static ");
                    stringBuilder.Append(method.ReturnType.Name);
                    stringBuilder.Append(" ");
                    stringBuilder.Append(method.Name);
                    stringBuilder.Append("(");
                    var parameters = method.GetParameters();
                    for(int i = 0; i < parameters.Length; i++)
                    {
                        if(i > 0)
                        {
                            stringBuilder.Append(", ");
                        }
                        stringBuilder.Append(parameters[i].ParameterType.Name);
                        stringBuilder.Append(" ");
                        stringBuilder.Append(parameters[i].Name);
                    }
                    stringBuilder.Append(")");
                    stringBuilder.Append("{");
                    stringBuilder.Append("return (");
                    stringBuilder.Append(method.ReturnType.Name);
                    stringBuilder.Append(")");
                    stringBuilder.Append(internalType.FullName);
                    stringBuilder.Append(".GetMethod(\"");
                    stringBuilder.Append(method.Name);
                    stringBuilder.Append("\").Invoke(");
                    stringBuilder.Append(internalType.FullName);
                    stringBuilder.Append(".GetType(), new object[] {");
                    for(int i = 0; i < parameters.Length; i++)
                    {
                        if(i > 0)
                        {
                            stringBuilder.Append(", ");
                        }
                        stringBuilder.Append(parameters[i].Name);
                    }
                    stringBuilder.Append("});");
                    stringBuilder.Append("}");
                    stringBuilder.Append("\n");
                }

                stringBuilder.Append("}");
                stringBuilder.Append("\n");
                stringBuilder.Append("}");

                context.AddSource(internalType.FullName, stringBuilder.ToString());
            }
        }
    }
}

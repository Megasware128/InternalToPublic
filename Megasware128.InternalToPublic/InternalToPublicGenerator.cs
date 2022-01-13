using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

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

        public string PublicType { get; set; }
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

            if (!File.Exists(projectFile))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor("InternalToPublicGenerator_MissingProjectFile",
                        "Missing project.assets.json",
                        "Have you forgotten to run 'dotnet restore' before running this generator?",
                        "InternalToPublicGenerator",
                        DiagnosticSeverity.Error,
                        true),
                    Location.None));
            }

            using (var stream = File.OpenRead(projectFile))
            {
                var jsonDocument = JsonDocument.Parse(stream);

                var restore = jsonDocument.RootElement.GetProperty("project").GetProperty("restore");
                var packagesPath = restore.GetProperty("packagesPath").GetString();
                var targetFramework = jsonDocument.RootElement.GetProperty("targets").EnumerateObject().First().Value;
                var libraries = jsonDocument.RootElement.GetProperty("libraries");

                var attributes = context.Compilation.Assembly.GetAttributes().Where(a => a.AttributeClass.Name is "InternalToPublicAttribute");

                foreach (var attribute in attributes)
                {
                    var assemblyName = attribute.ConstructorArguments[0].Value.ToString();
                    var typeName = attribute.ConstructorArguments[1].Value.ToString();
                    var publicTypeName = attribute.NamedArguments.FirstOrDefault(a => a.Key is "PublicType").Value;

                    var assemblyId = context.Compilation.SourceModule.ReferencedAssemblies.FirstOrDefault(a => a.Name == assemblyName);

                    var assembly = default(Assembly);

                    try
                    {
                        assembly = Assembly.Load(assemblyId.GetDisplayName(fullKey: true));
                    }
                    catch
                    {
                        var targetLibrary = targetFramework.EnumerateObject().FirstOrDefault(l => l.Name.StartsWith(assemblyName));

                        if (string.IsNullOrEmpty(targetLibrary.Name))
                        {
                            context.ReportDiagnostic(Diagnostic.Create(
                                new DiagnosticDescriptor("InternalToPublicGenerator_MissingLibrary",
                                    $"Missing library '{assemblyName}'",
                                    $"Have you spelled the assembly name correctly?",
                                    "InternalToPublicGenerator",
                                    DiagnosticSeverity.Error,
                                    true), attribute.ApplicationSyntaxReference.GetSyntax().GetLocation()));
                        }

                        var library = libraries.GetProperty(targetLibrary.Name);

                        var assemblyPath = Path.Combine(packagesPath, library.GetProperty("path").GetString(), targetLibrary.Value.GetProperty("runtime").EnumerateObject().Single().Name);

                        try
                        {
                            assembly = Assembly.LoadFile(assemblyPath);
                        }
                        catch (Exception ex)
                        {
                            context.ReportDiagnostic(Diagnostic.Create(
                                new DiagnosticDescriptor("InternalToPublicGenerator_MissingAssembly",
                                    $"Missing assembly '{assemblyPath}' ({ex.Message})",
                                    $"Have you spelled the assembly name correctly?",
                                    "InternalToPublicGenerator",
                                    DiagnosticSeverity.Error,
                                    true), attribute.ApplicationSyntaxReference.GetSyntax().GetLocation()));
                        }
                    }

                    var internalType = assembly.GetType(typeName);

                    if (internalType is null)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            new DiagnosticDescriptor("InternalToPublicGenerator_MissingType",
                                $"Missing type '{typeName}' in assembly '{assemblyName}'",
                                "Have you spelled the type name correctly?",
                                "InternalToPublicGenerator",
                                DiagnosticSeverity.Error,
                                true), attribute.ApplicationSyntaxReference.GetSyntax().GetLocation()));

                        continue;
                    }

                    var publicType = publicTypeName.IsNull ? assembly.GetTypes().First(t => t.IsPublic) : assembly.GetType(publicTypeName.ToCSharpString().Trim('"'));

                    if (publicType is null)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            new DiagnosticDescriptor("InternalToPublicGenerator_MissingPublicType",
                                $"Missing public type '{publicTypeName}' in assembly '{assemblyName}'",
                                "Have you spelled the public type name correctly?",
                                "InternalToPublicGenerator",
                                DiagnosticSeverity.Error,
                                true), attribute.ApplicationSyntaxReference.GetSyntax().GetLocation()));

                        continue;
                    }

                    var publicTypeSyntax = ConvertTypeToSyntax(publicType);

                    TypeSyntax ConvertTypeToSyntax(Type type)
                    {
                        // If type is void, return PredefinedTypeSyntax
                        if (type == typeof(void))
                        {
                            return PredefinedType(Token(SyntaxKind.VoidKeyword));
                        }

                        var typeNamespace = type.Namespace;
                        // Convert namespace to QualifiedNameSyntax
                        var qualifiedName = ConvertNamespaceToQualifiedName(typeNamespace);

                        NameSyntax ConvertNamespaceToQualifiedName(string namespaceName)
                        {
                            var parts = namespaceName.Split('.');

                            NameSyntax current = AliasQualifiedName(IdentifierName(Token(SyntaxKind.GlobalKeyword)), IdentifierName(parts[0]));

                            for (var i = 1; i < parts.Length; i++)
                            {
                                current = QualifiedName(current, IdentifierName(parts[i]));
                            }

                            return current;
                        }

                        if (type.IsGenericType)
                        {
                            var genericType = type.GetGenericTypeDefinition();

                            var genericTypeSyntax = GenericName(Regex.Replace(genericType.Name, @"`\d+$", string.Empty));

                            var genericTypeArguments = type.GetGenericArguments();

                            var genericTypeArgumentsSyntax = genericTypeArguments.Select(ConvertTypeToSyntax).ToArray();

                            var name = genericTypeSyntax.WithTypeArgumentList(TypeArgumentList(SeparatedList(genericTypeArgumentsSyntax)));

                            return QualifiedName(qualifiedName, name);
                        }

                        if (type.IsArray)
                        {
                            var arrayType = type.GetElementType();

                            var arrayTypeSyntax = ArrayType(ConvertTypeToSyntax(arrayType));

                            return arrayTypeSyntax;
                        }

                        return QualifiedName(qualifiedName, IdentifierName(type.Name));
                    }

                    var internalTypeDeclarator = VariableDeclarator(Identifier(nameof(internalType)))
                        .WithInitializer(EqualsValueClause(InvocationExpression(
                            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                    TypeOfExpression(publicTypeSyntax), IdentifierName(nameof(Type.Assembly))),
                                IdentifierName(nameof(Assembly.GetType))),
                        ArgumentList(SingletonSeparatedList(Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(typeName))))))));

                    var internalTypeSyntax = FieldDeclaration(VariableDeclaration(ConvertTypeToSyntax(typeof(Type)), SingletonSeparatedList(internalTypeDeclarator)))
                                                .WithModifiers(TokenList(Token(SyntaxKind.PrivateKeyword), Token(SyntaxKind.StaticKeyword)));

                    var methodDeclarationsSyntax = internalType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                                                               .Where(m => !m.GetParameters().Any(p => p.ParameterType.IsByRef))
                                                               .Where(m => m.ReturnType.IsPublic && m.GetParameters().All(p => p.ParameterType.IsPublic))
                                                               .Where(m => !m.Name.StartsWith("<"))
                                                               .Where(m => !m.IsGenericMethodDefinition)
                                                               .Select(m => ConvertMethodToSyntax(m))
                                                               .ToArray();

                    MethodDeclarationSyntax ConvertMethodToSyntax(MethodInfo method)
                    {
                        var methodName = method.Name;

                        var methodSyntax = MethodDeclaration(ConvertTypeToSyntax(method.ReturnType), Identifier(methodName))
                            .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword)))
                            .WithParameterList(ParameterList(SeparatedList(method.GetParameters().Select(p => Parameter(Identifier(p.Name)).WithType(ConvertTypeToSyntax(p.ParameterType))))));

                        // internalType.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, parameters, null).Invoke(null, arguments);

                        var invocationExpression =
                            InvocationExpression(
                                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                    InvocationExpression(
                                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                            IdentifierName(nameof(internalType)),
                                            IdentifierName(nameof(Type.GetMethod))),
                                        ArgumentList(SeparatedList(new[]
                                        {
                                            Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(methodName))),
                                            Argument(BinaryExpression(SyntaxKind.BitwiseOrExpression,
                                                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(nameof(BindingFlags)), IdentifierName(nameof(BindingFlags.Public))),
                                                BinaryExpression(SyntaxKind.BitwiseOrExpression,
                                                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(nameof(BindingFlags)), IdentifierName(nameof(BindingFlags.NonPublic))),
                                                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(nameof(BindingFlags)), IdentifierName(nameof(BindingFlags.Static)))))),
                                            Argument(LiteralExpression(SyntaxKind.NullLiteralExpression)),
                                            Argument(ArrayCreationExpression(
                                                ArrayType(ConvertTypeToSyntax(typeof(Type)), SingletonList(ArrayRankSpecifier(SingletonSeparatedList<ExpressionSyntax>(OmittedArraySizeExpression())))),
                                                InitializerExpression(SyntaxKind.ArrayInitializerExpression,
                                                    SeparatedList<ExpressionSyntax>(method.GetParameters().Select(p => TypeOfExpression(ConvertTypeToSyntax(p.ParameterType))))))),
                                            Argument(LiteralExpression(SyntaxKind.NullLiteralExpression))
                                        }))),
                                    IdentifierName(nameof(MethodBase.Invoke))),
                                ArgumentList(SeparatedList(
                                    new[]
                                    {
                                        Argument(LiteralExpression(SyntaxKind.NullLiteralExpression)),
                                        Argument(ArrayCreationExpression(
                                            ArrayType(ConvertTypeToSyntax(typeof(object)), SingletonList(ArrayRankSpecifier(SingletonSeparatedList<ExpressionSyntax>(OmittedArraySizeExpression())))),
                                            InitializerExpression(SyntaxKind.ArrayInitializerExpression,
                                                SeparatedList<ExpressionSyntax>(method.GetParameters().Select(p => IdentifierName(p.Name))))))
                                    })));

                        var block = Block(method.ReturnType == typeof(void) ? ExpressionStatement(invocationExpression) :
                            ReturnStatement(CastExpression(ConvertTypeToSyntax(method.ReturnType), invocationExpression)));

                        return methodSyntax.WithBody(block);
                    }

                    var internalTypeDeclaration = ClassDeclaration(internalType.Name)
                                                    .WithModifiers(TokenList(Token(SyntaxKind.InternalKeyword), Token(SyntaxKind.StaticKeyword)))
                                                    .AddMembers(internalTypeSyntax)
                                                    .AddMembers(methodDeclarationsSyntax);

                    var namespaceDeclaration = NamespaceDeclaration(QualifiedName(IdentifierName(nameof(Megasware128)), IdentifierName(nameof(InternalToPublic))))
                                                .AddMembers(internalTypeDeclaration);

                    var compilationUnit = CompilationUnit()
                        .WithUsings(SingletonList(UsingDirective(QualifiedName(IdentifierName(nameof(System)), IdentifierName(nameof(System.Reflection))))))
                                            .AddMembers(namespaceDeclaration);

                    context.AddSource($"{assemblyName}.{typeName}.g.cs", compilationUnit.NormalizeWhitespace().ToFullString());
                }
            }
        }
    }
}

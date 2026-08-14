using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Arch.Systems.SourceGenerator;

/// <summary>
/// Generates queries.
/// </summary>
[Generator]
public class QueryGenerator : IIncrementalGenerator
{
    /// <inheritdoc cref="IIncrementalGenerator.Initialize"/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        //if (!Debugger.IsAttached) Debugger.Launch();

        // Do a simple filter for methods marked with update
        IncrementalValuesProvider<MethodDeclarationSyntax> methodDeclarations = context.SyntaxProvider.CreateSyntaxProvider(
                 static (s, _) => s is MethodDeclarationSyntax { AttributeLists.Count: > 0 },
                 static (ctx, _) => GetMethodSymbolIfAttributeof(ctx, "Arch.Systems.QueryAttribute")
        ).Where(static m => m is not null)!; // filter out attributed methods that we don't care about

        // Combine the selected enums with the `Compilation`
        IncrementalValueProvider<(Compilation, ImmutableArray<MethodDeclarationSyntax>)> compilationAndMethods = context.CompilationProvider.Combine(methodDeclarations.WithComparer(Comparer.Instance).Collect());
        context.RegisterSourceOutput(compilationAndMethods, static (spc, source) => Generate(source.Item1, source.Item2, spc));
    }

    /// <summary>
    ///     Adds a <see cref="IMethodSymbol"/> to its class.
    /// </summary>
    /// <param name="classToMethods">The dictionary mapping classes to their methods.</param>
    /// <param name="methodSymbol">The <see cref="IMethodSymbol"/> which will be added/mapped to its class.</param>
    private static void AddMethodToClass(Dictionary<ISymbol, List<IMethodSymbol>> classToMethods, IMethodSymbol methodSymbol)
    {
        if (!classToMethods.TryGetValue(methodSymbol.ContainingSymbol, out var list))
        {
            list = new List<IMethodSymbol>();
            classToMethods[methodSymbol.ContainingSymbol] = list;
        }
        list.Add(methodSymbol);
    }
    
    /// <summary>
    ///     Returns a <see cref="MethodDeclarationSyntax"/> if it's annotated with an attribute of <paramref name="name"/>.
    /// </summary>
    /// <param name="context">Its <see cref="GeneratorSyntaxContext"/>.</param>
    /// <param name="name">The attributes name.</param>
    /// <returns></returns>
    private static MethodDeclarationSyntax? GetMethodSymbolIfAttributeof(GeneratorSyntaxContext context, string name)
    {
        // we know the node is a EnumDeclarationSyntax thanks to IsSyntaxTargetForGeneration
        var enumDeclarationSyntax = (MethodDeclarationSyntax)context.Node;

        // loop through all the attributes on the method
        foreach (var attributeListSyntax in enumDeclarationSyntax.AttributeLists)
        {
            foreach (var attributeSyntax in attributeListSyntax.Attributes)
            {
                if (ModelExtensions.GetSymbolInfo(context.SemanticModel, attributeSyntax).Symbol is not IMethodSymbol attributeSymbol) continue;
                
                var attributeContainingTypeSymbol = attributeSymbol.ContainingType;
                var fullName = attributeContainingTypeSymbol.ToDisplayString();

                // Is the attribute the [EnumExtensions] attribute?
                if (fullName != name) continue;
                return enumDeclarationSyntax;
            }
        }

        // we didn't find the attribute we were looking for
        return null;
    }

    /// <summary>
    ///     Generates queries and partial classes for the found marked methods.
    /// </summary>
    /// <param name="compilation">The <see cref="Compilation"/>.</param>
    /// <param name="methods">The <see cref="ImmutableArray{MethodDeclarationSyntax}"/> array, the methods which we will generate queries and classes for.</param>
    /// <param name="context">The <see cref="SourceProductionContext"/>.</param>
    private static void Generate(Compilation compilation, ImmutableArray<MethodDeclarationSyntax> methods, SourceProductionContext context)
    {
        if (methods.IsDefaultOrEmpty) return;
        
        // Generate Query methods and map them to their classes
        var classToMethods = new Dictionary<ISymbol, List<IMethodSymbol>>(512, SymbolEqualityComparer.Default);
        foreach (var methodSyntax in methods)
        {
            IMethodSymbol? methodSymbol = null;
            try
            {
                var semanticModel = compilation.GetSemanticModel(methodSyntax.SyntaxTree);
                methodSymbol = ModelExtensions.GetDeclaredSymbol(semanticModel, methodSyntax) as IMethodSymbol;
            }
            catch
            {
                //not update,skip
                continue;
            }

            AddMethodToClass(classToMethods, methodSymbol!);

            var sb = new StringBuilder();
            var method = sb.AppendQueryMethod(methodSymbol!);
            var fileName = methodSymbol!.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat).Replace('<', '{').Replace('>', '}');
            context.AddSource($"{fileName}.g.cs",CSharpSyntaxTree.ParseText(method.ToString()).GetRoot().NormalizeWhitespace().ToFullString());
        }

        // Creating class that calls the created methods after another.
        foreach (var classToMethod in classToMethods)
        {
            var template = new StringBuilder().AppendBaseSystem(classToMethod).ToString();
            if (string.IsNullOrEmpty(template)) continue;
            
            var fileName = ((INamedTypeSymbol)classToMethod.Key).ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat).Replace('<', '{').Replace('>', '}');
            context.AddSource($"{fileName}.g.cs",
                CSharpSyntaxTree.ParseText(template).GetRoot().NormalizeWhitespace().ToFullString());
        }
    }

    /// <summary>
    /// Compares <see cref="MethodDeclarationSyntax"/>s to remove duplicates. 
    /// </summary>
    class Comparer : IEqualityComparer<MethodDeclarationSyntax>
    {
        public static readonly Comparer Instance = new();

        public bool Equals(MethodDeclarationSyntax x, MethodDeclarationSyntax y)
        {
            return x.Equals(y);
        }

        public int GetHashCode(MethodDeclarationSyntax obj)
        {
            return obj.GetHashCode();
        }
    }
}
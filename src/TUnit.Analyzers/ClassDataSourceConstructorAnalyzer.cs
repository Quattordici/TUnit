using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using TUnit.Analyzers.Extensions;
using TUnit.Analyzers.Helpers;

namespace TUnit.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ClassDataSourceConstructorAnalyzer : ConcurrentDiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rules.NoAccessibleConstructor);

    protected override void InitializeInternal(AnalysisContext context)
    {
        context.RegisterSymbolAction(AnalyzeProperty, SymbolKind.Property);
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
        context.RegisterSymbolAction(AnalyzeClass, SymbolKind.NamedType);
    }

    private void AnalyzeProperty(SymbolAnalysisContext context)
    {
        if (context.Symbol is not IPropertySymbol propertySymbol)
        {
            return;
        }

        foreach (var attribute in propertySymbol.GetAttributes())
        {
            CheckClassDataSourceAttribute(context, attribute);
        }
    }

    private void AnalyzeMethod(SymbolAnalysisContext context)
    {
        if (context.Symbol is not IMethodSymbol methodSymbol)
        {
            return;
        }

        // Check method-level attributes
        foreach (var attribute in methodSymbol.GetAttributes())
        {
            CheckClassDataSourceAttribute(context, attribute);
        }

        // Check parameter-level attributes
        foreach (var parameter in methodSymbol.Parameters)
        {
            foreach (var attribute in parameter.GetAttributes())
            {
                CheckClassDataSourceAttribute(context, attribute);
            }
        }
    }

    private void AnalyzeClass(SymbolAnalysisContext context)
    {
        if (context.Symbol is not INamedTypeSymbol namedTypeSymbol)
        {
            return;
        }

        foreach (var attribute in namedTypeSymbol.GetAttributes())
        {
            CheckClassDataSourceAttribute(context, attribute);
        }
    }

    private void CheckClassDataSourceAttribute(SymbolAnalysisContext context, AttributeData attribute)
    {
        if (attribute.AttributeClass is null)
        {
            return;
        }

        // Check if this is ClassDataSourceAttribute<T>
        // Cheap name check first: building the display string for every attribute is expensive.
        if (!attribute.AttributeClass.Name.StartsWith("ClassDataSourceAttribute", StringComparison.Ordinal) ||
            !attribute.AttributeClass.ToDisplayString().StartsWith("TUnit.Core.ClassDataSourceAttribute<", StringComparison.Ordinal))
        {
            return;
        }

        // Get the type argument T from ClassDataSource<T>
        if (attribute.AttributeClass is not INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: > 0 } genericAttribute)
        {
            return;
        }

        var dataSourceType = genericAttribute.TypeArguments[0];

        // Skip if the type is abstract - it can't be instantiated directly anyway
        if (dataSourceType is INamedTypeSymbol { IsAbstract: true })
        {
            return;
        }

        // Skip type parameters - they can't be validated at compile time
        if (dataSourceType is ITypeParameterSymbol)
        {
            return;
        }

        if (dataSourceType is not INamedTypeSymbol namedType)
        {
            return;
        }

        // Check if there's an accessible parameterless constructor, or a constructor whose
        // arguments are supplied by a class-level ClassDataSource attribute on the type itself
        if (!HasAccessibleParameterlessConstructor(namedType, context.Compilation)
            && !HasConstructorDataSource(namedType))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    Rules.NoAccessibleConstructor,
                    attribute.GetLocation() ?? context.Symbol.Locations.FirstOrDefault(),
                    namedType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }
    }

    /// <summary>
    /// A type without a parameterless constructor can still be created when it declares exactly one
    /// class-level ClassDataSource attribute and has a public constructor taking that many parameters,
    /// e.g. <c>[ClassDataSource&lt;Network&gt;] public class Container(Network network)</c>.
    /// </summary>
    private static bool HasConstructorDataSource(INamedTypeSymbol type)
    {
        int? dependencyCount = null;

        foreach (var attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass is not { Name: "ClassDataSourceAttribute" } attributeClass
                || attributeClass.ContainingNamespace?.ToDisplayString() != "TUnit.Core")
            {
                continue;
            }

            var count = attributeClass.IsGenericType
                ? attributeClass.TypeArguments.Length
                : CountTypeArguments(attribute);

            if (count == 0)
            {
                continue;
            }

            if (dependencyCount is not null)
            {
                // Several class-level data sources describe several data rows, not one set of constructor arguments
                return false;
            }

            dependencyCount = count;
        }

        return dependencyCount is not null
            && type.InstanceConstructors.Any(c => c.DeclaredAccessibility == Accessibility.Public
                && c.Parameters.Length == dependencyCount);
    }

    private static int CountTypeArguments(AttributeData attribute)
    {
        var count = 0;

        foreach (var argument in attribute.ConstructorArguments)
        {
            count += argument.Kind == TypedConstantKind.Array ? argument.Values.Length : 1;
        }

        return count;
    }

    private static bool HasAccessibleParameterlessConstructor(INamedTypeSymbol type, Compilation compilation)
    {
        // For structs, there's always an implicit parameterless constructor
        if (type.IsValueType)
        {
            return true;
        }

        // If there are no explicit constructors, the compiler generates a public parameterless constructor
        var hasAnyExplicitConstructor = type.InstanceConstructors
            .Any(c => !c.IsImplicitlyDeclared);

        if (!hasAnyExplicitConstructor)
        {
            return true;
        }

        // Check for an explicit accessible parameterless constructor
        return type.InstanceConstructors
            .Where(c => c.Parameters.Length == 0)
            .Any(c => c.DeclaredAccessibility is Accessibility.Public
                or Accessibility.Internal
                or Accessibility.ProtectedOrInternal);
    }
}

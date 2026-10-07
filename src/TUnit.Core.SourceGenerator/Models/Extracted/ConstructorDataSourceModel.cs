namespace TUnit.Core.SourceGenerator.Models.Extracted;

/// <summary>
/// Primitive-only model for a ClassDataSource type whose constructor arguments are supplied by a
/// class-level ClassDataSource attribute on the type itself.
/// </summary>
internal sealed record ConstructorDataSourceModel
{
    /// <summary>
    /// Fully qualified type name (e.g., "global::MyNamespace.MyClass")
    /// </summary>
    public required string TypeFullyQualified { get; init; }

    /// <summary>
    /// Safe type name for use in identifiers (dots/generics replaced with underscores)
    /// </summary>
    public required string SafeTypeName { get; init; }

    /// <summary>
    /// Fully qualified attribute type, including type arguments
    /// </summary>
    public required string AttributeTypeName { get; init; }

    /// <summary>
    /// Formatted attribute constructor arguments
    /// </summary>
    public required EquatableArray<string> ConstructorArgs { get; init; }

    /// <summary>
    /// Formatted attribute named arguments
    /// </summary>
    public required EquatableArray<NamedArgModel> NamedArgs { get; init; }

    /// <summary>
    /// Fully qualified constructor parameter types, in order
    /// </summary>
    public required EquatableArray<string> ParameterTypes { get; init; }
}

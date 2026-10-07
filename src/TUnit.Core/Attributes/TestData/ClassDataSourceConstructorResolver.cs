using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace TUnit.Core;

/// <summary>
/// Implemented by the <c>ClassDataSource</c> attribute family so that a class-level attribute on a
/// data source type can supply that type's constructor arguments synchronously.
/// </summary>
internal interface IClassDataSourceArgumentProvider
{
    /// <summary>
    /// The types this attribute produces, in constructor parameter order.
    /// </summary>
    Type[] DependencyTypes { get; }

    /// <summary>
    /// Resolves (or reuses, according to the attribute's sharing settings) one value per <see cref="DependencyTypes"/> entry.
    /// </summary>
    object?[] CreateArguments(DataGeneratorMetadata dataGeneratorMetadata);
}

/// <summary>
/// Resolves constructor arguments for <c>ClassDataSource</c> types that have no public parameterless
/// constructor and instead declare their own dependencies with a class-level <c>ClassDataSource</c> attribute:
/// <code>
/// [ClassDataSource&lt;DockerNetwork&gt;(Shared = SharedType.PerTestSession)]
/// public class NatsContainer(DockerNetwork network) : IAsyncInitializer { ... }
/// </code>
/// Uses the source-generated <see cref="ClassDataSourceConstructorRegistry"/> when available, falling back to reflection.
/// The created dependencies are recorded per instance so that object graph discovery can initialize them
/// before, and dispose them after, the object that depends on them.
/// </summary>
internal static class ClassDataSourceConstructorResolver
{
    private sealed class ConstructorPlan(IClassDataSourceArgumentProvider provider, Func<object?[], object>? createInstance)
    {
        public IClassDataSourceArgumentProvider Provider { get; } = provider;

        /// <summary>
        /// Source-generated constructor call, or <c>null</c> to use reflection.
        /// </summary>
        public Func<object?[], object>? CreateInstance { get; } = createInstance;
    }

    private static readonly ConcurrentDictionary<Type, ConstructorPlan?> PlansByType = new();
    private static readonly ConcurrentDictionary<Type, byte> ConstructedTypes = new();
    private static readonly ConditionalWeakTable<object, object?[]> DependenciesByInstance = new();
    private static volatile bool _anyConstructed;

    /// <summary>
    /// Returns <c>true</c> when instances of <paramref name="type"/> must be constructed with arguments resolved
    /// from its class-level <c>ClassDataSource</c> attribute. Memoized per type.
    /// </summary>
    public static bool HasConstructorDataSources([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type type)
        => GetPlan(type) is not null;

    /// <summary>
    /// Returns <c>true</c> when at least one instance of <paramref name="type"/> was created by <see cref="CreateInstance"/>,
    /// so instances of it may carry constructor dependencies. Cheap when the feature is unused.
    /// </summary>
    public static bool IsConstructedType(Type type)
        => _anyConstructed && ConstructedTypes.ContainsKey(type);

    /// <summary>
    /// Returns the dependencies that were passed to <paramref name="instance"/>'s constructor, if it was created by
    /// <see cref="CreateInstance"/>.
    /// </summary>
    public static bool TryGetDependencies(object instance, [NotNullWhen(true)] out object?[]? dependencies)
    {
        if (!_anyConstructed)
        {
            dependencies = null;
            return false;
        }

        return DependenciesByInstance.TryGetValue(instance, out dependencies);
    }

    /// <summary>
    /// Creates an instance of <paramref name="type"/>, resolving its constructor arguments from its class-level
    /// <c>ClassDataSource</c> attribute. Callers must check <see cref="HasConstructorDataSources"/> first.
    /// </summary>
    public static object CreateInstance(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type type,
        DataGeneratorMetadata dataGeneratorMetadata)
    {
        var plan = GetPlan(type)!;

        ThrowIfCircular(plan.Provider, [type]);

        var arguments = plan.Provider.CreateArguments(dataGeneratorMetadata);

#if NET
        TraceScopeRegistry.RegisterFromDataSource((IDataSourceAttribute) plan.Provider, arguments);
#endif

        var instance = plan.CreateInstance is { } createInstance
            ? createInstance(arguments)
            : Activator.CreateInstance(type, arguments)!;

        DependenciesByInstance.Add(instance, arguments);
        ConstructedTypes.TryAdd(type, 0);
        _anyConstructed = true;

        return instance;
    }

    private static ConstructorPlan? GetPlan([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type type)
        => PlansByType.TryGetValue(type, out var plan)
            ? plan
            : PlansByType.GetOrAdd(type, CreatePlan(type));

    private static ConstructorPlan? CreatePlan([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type type)
    {
        if (ClassDataSourceConstructorRegistry.TryGet(type, out var createDataSource, out var createInstance))
        {
            return createDataSource() is IClassDataSourceArgumentProvider registeredProvider
                ? new ConstructorPlan(registeredProvider, createInstance)
                : null;
        }

        // Reflection fallback. Value types and types with a public parameterless constructor keep the plain Activator path.
        if (type.IsValueType || type.IsAbstract || type.GetConstructor(Type.EmptyTypes) is not null)
        {
            return null;
        }

        IClassDataSourceArgumentProvider? provider = null;

        foreach (var attribute in type.GetCustomAttributes(inherit: false))
        {
            if (attribute is not IClassDataSourceArgumentProvider candidate || candidate.DependencyTypes.Length == 0)
            {
                continue;
            }

            // Several class-level data sources describe several data rows (as on a test class), not one
            // set of constructor arguments, so leave such types on the default construction path.
            if (provider is not null)
            {
                return null;
            }

            provider = candidate;
        }

        return provider is null ? null : new ConstructorPlan(provider, createInstance: null);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2062",
        Justification = "Dependency types come from ClassDataSource type arguments / constructor parameters, which are annotated with DynamicallyAccessedMembers(PublicConstructors).")]
    [UnconditionalSuppressMessage("Trimming", "IL2072",
        Justification = "Dependency types come from ClassDataSource type arguments / constructor parameters, which are annotated with DynamicallyAccessedMembers(PublicConstructors).")]
    private static void ThrowIfCircular(IClassDataSourceArgumentProvider provider, List<Type> path)
    {
        foreach (var dependencyType in provider.DependencyTypes)
        {
            if (path.Contains(dependencyType))
            {
                path.Add(dependencyType);
                throw new InvalidOperationException(
                    $"Circular ClassDataSource constructor dependency detected: {string.Join(" -> ", path.Select(t => t.Name))}");
            }

            var nestedPlan = GetPlan(dependencyType);
            if (nestedPlan is null)
            {
                continue;
            }

            path.Add(dependencyType);
            ThrowIfCircular(nestedPlan.Provider, path);
            path.RemoveAt(path.Count - 1);
        }
    }
}

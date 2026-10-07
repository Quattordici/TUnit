using System.Collections.Concurrent;
using System.ComponentModel;

namespace TUnit.Core;

/// <summary>
/// Registry of source-generated constructors for <c>ClassDataSource</c> types whose constructor arguments are
/// supplied by a class-level <c>ClassDataSource</c> attribute on the type itself. Lets those types be created
/// without reflection, so they keep working when trimmed or compiled with Native AOT.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ClassDataSourceConstructorRegistry
{
    private static readonly ConcurrentDictionary<Type, (Func<IDataSourceAttribute> CreateDataSource, Func<object?[], object> CreateInstance)> Registrations = new();

    /// <summary>
    /// Registers how to create <paramref name="type"/>. Called by generated code from a static field
    /// initializer on the consolidated registration <c>.cctor</c>.
    /// Returns a dummy value for use as a static field initializer.
    /// </summary>
    /// <param name="type">The type that receives its constructor arguments from a class-level data source.</param>
    /// <param name="createDataSource">Creates the type's class-level <c>ClassDataSource</c> attribute.</param>
    /// <param name="createInstance">Invokes the type's constructor with the resolved arguments.</param>
    public static int Register(Type type, Func<IDataSourceAttribute> createDataSource, Func<object?[], object> createInstance)
    {
        Registrations[type] = (createDataSource, createInstance);
        return 0;
    }

    internal static bool TryGet(Type type, out Func<IDataSourceAttribute> createDataSource, out Func<object?[], object> createInstance)
    {
        if (Registrations.TryGetValue(type, out var registration))
        {
            (createDataSource, createInstance) = registration;
            return true;
        }

        createDataSource = null!;
        createInstance = null!;
        return false;
    }
}

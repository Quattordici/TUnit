using TUnit.Core.Discovery;
using TUnit.Core.Enums;

namespace TUnit.UnitTests;

public class ClassDataSourceConstructorResolverTests
{
    [Test]
    public async Task Type_With_Class_Level_ClassDataSource_And_Parameterized_Constructor_Is_Resolved()
    {
        await Assert.That(ClassDataSourceConstructorResolver.HasConstructorDataSources(typeof(ConstructorDependent))).IsTrue();
    }

    [Test]
    public async Task Accessible_Type_Is_Created_Through_Its_Source_Generated_Registration()
    {
        await Assert.That(ClassDataSourceConstructorRegistry.TryGet(typeof(ConstructorDependent), out _, out _)).IsTrue();

        var instance = (ConstructorDependent) ClassDataSources.Get(Guid.NewGuid().ToString())
            .Get(SharedType.None, typeof(ConstructorDependent), typeof(object), null, CreateMetadata())!;

        await AssertConstructorDependency(instance, instance.Dependency);
    }

    [Test]
    public async Task Type_Without_Registration_Is_Created_Through_Reflection()
    {
        // Private types are not reachable from generated code, so they use the reflection fallback
        await Assert.That(ClassDataSourceConstructorRegistry.TryGet(typeof(PrivateConstructorDependent), out _, out _)).IsFalse();

        var instance = (PrivateConstructorDependent) ClassDataSources.Get(Guid.NewGuid().ToString())
            .Get(SharedType.None, typeof(PrivateConstructorDependent), typeof(object), null, CreateMetadata())!;

        await AssertConstructorDependency(instance, instance.Dependency);
    }

    [Test]
    public async Task Type_With_Parameterless_Constructor_Keeps_Default_Construction()
    {
        await Assert.That(ClassDataSourceConstructorResolver.HasConstructorDataSources(typeof(ParameterlessWithAttribute))).IsFalse();
        await Assert.That(ClassDataSourceConstructorResolver.HasConstructorDataSources(typeof(Dependency))).IsFalse();
    }

    [Test]
    public async Task Type_With_Several_Class_Level_ClassDataSources_Keeps_Default_Construction()
    {
        // Several class-level data sources describe data rows (as on a test class), not constructor arguments
        await Assert.That(ClassDataSourceConstructorResolver.HasConstructorDataSources(typeof(SeveralDataSources))).IsFalse();
    }

    [Test]
    public async Task Circular_Constructor_Dependencies_Throw_Instead_Of_Overflowing()
    {
        var exception = await Assert.That(() => ClassDataSourceConstructorResolver.CreateInstance(typeof(CycleA), null!))
            .Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).Contains("CycleA -> CycleB -> CycleA");
    }

    private static async Task AssertConstructorDependency(object instance, object dependency)
    {
        await Assert.That(dependency).IsNotNull();
        await Assert.That(ClassDataSourceConstructorResolver.TryGetDependencies(instance, out var dependencies)).IsTrue();
        await Assert.That(dependencies!).IsEquivalentTo(new[] { dependency });
        await Assert.That(ClassDataSourceConstructorResolver.IsConstructedType(instance.GetType())).IsTrue();
        await Assert.That(ObjectGraphDiscoverer.MayHaveNestedObjects(instance.GetType())).IsTrue();
    }

    private static DataGeneratorMetadata CreateMetadata() => new()
    {
        TestBuilderContext = new TestBuilderContextAccessor(new TestBuilderContext { TestMetadata = TestContext.Current!.Metadata.TestDetails.MethodMetadata }),
        MembersToGenerate = [],
        TestInformation = null,
        Type = DataGeneratorType.ClassParameters,
        TestSessionId = Guid.NewGuid().ToString(),
        TestClassInstance = null,
        ClassInstanceArguments = null
    };

    public class Dependency;

    private sealed class PrivateDependency;

    [ClassDataSource<PrivateDependency>]
    private sealed class PrivateConstructorDependent(PrivateDependency dependency)
    {
        public PrivateDependency Dependency => dependency;
    }

    [ClassDataSource<Dependency>]
    public class ConstructorDependent(Dependency dependency)
    {
        public Dependency Dependency => dependency;
    }

#pragma warning disable TUnit0001 // Not a test class: only checks that a parameterless constructor keeps the default path
    [ClassDataSource<Dependency>]
    public class ParameterlessWithAttribute;
#pragma warning restore TUnit0001

    [ClassDataSource<Dependency>]
    [ClassDataSource<Dependency>]
    public class SeveralDataSources(Dependency dependency)
    {
        public Dependency Dependency => dependency;
    }

    [ClassDataSource<CycleB>]
    public class CycleA(CycleB b)
    {
        public CycleB B => b;
    }

    [ClassDataSource<CycleA>]
    public class CycleB(CycleA a)
    {
        public CycleA A => a;
    }
}

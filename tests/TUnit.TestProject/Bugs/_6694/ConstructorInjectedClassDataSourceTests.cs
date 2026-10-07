using TUnit.Core.Interfaces;
using TUnit.TestProject.Attributes;

namespace TUnit.TestProject.Bugs._6694;

// Regression tests for #6694: a ClassDataSource type can take its own dependencies through its
// constructor, supplied by a class-level [ClassDataSource] attribute on that type. The dependencies
// must be resolved with their own sharing, initialized before the parent, and disposed after it.

#region Fixtures

public sealed class Issue6694DockerNetwork : IAsyncInitializer, IAsyncDisposable
{
    public bool Initialized { get; private set; }
    public bool Disposed { get; private set; }

    public Task InitializeAsync()
    {
        Initialized = true;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return default;
    }
}

[ClassDataSource<Issue6694DockerNetwork>(Shared = SharedType.PerTestSession)]
public sealed class Issue6694NatsContainer(Issue6694DockerNetwork network) : IAsyncInitializer, IAsyncDisposable
{
    public Issue6694DockerNetwork Network => network;
    public bool Initialized { get; private set; }

    public Task InitializeAsync()
    {
        if (!network.Initialized)
        {
            throw new InvalidOperationException("Network wasn't initialized before the container");
        }

        Initialized = true;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (network.Disposed)
        {
            throw new InvalidOperationException("Network was disposed before the container");
        }

        return default;
    }
}

public sealed class Issue6694Leaf : IAsyncInitializer
{
    public bool Initialized { get; private set; }

    public Task InitializeAsync()
    {
        Initialized = true;
        return Task.CompletedTask;
    }
}

// Constructor-injected dependency that itself uses property injection
public sealed class Issue6694Middle : IAsyncInitializer
{
    [ClassDataSource<Issue6694Leaf>]
    public required Issue6694Leaf Leaf { get; init; }

    public bool Initialized { get; private set; }

    public Task InitializeAsync()
    {
        if (!Leaf.Initialized)
        {
            throw new InvalidOperationException("Leaf wasn't initialized before Middle");
        }

        Initialized = true;
        return Task.CompletedTask;
    }
}

[ClassDataSource<Issue6694Middle>]
public sealed class Issue6694Upper(Issue6694Middle middle) : IAsyncInitializer
{
    public Issue6694Middle Middle => middle;
    public bool Initialized { get; private set; }

    public Task InitializeAsync()
    {
        if (!middle.Initialized)
        {
            throw new InvalidOperationException("Middle wasn't initialized before Upper");
        }

        Initialized = true;
        return Task.CompletedTask;
    }
}

// Constructor injection chained through several levels
[ClassDataSource<Issue6694Upper>]
public sealed class Issue6694Top(Issue6694Upper upper) : IAsyncInitializer
{
    public Issue6694Upper Upper => upper;
    public bool Initialized { get; private set; }

    public Task InitializeAsync()
    {
        if (!upper.Initialized)
        {
            throw new InvalidOperationException("Upper wasn't initialized before Top");
        }

        Initialized = true;
        return Task.CompletedTask;
    }
}

// Several constructor parameters supplied by one multi-type attribute
[ClassDataSource<Issue6694Leaf, Issue6694DockerNetwork>(Shared = [SharedType.None, SharedType.PerTestSession])]
public sealed class Issue6694MultiDependency(Issue6694Leaf leaf, Issue6694DockerNetwork network) : IAsyncInitializer
{
    public Issue6694Leaf Leaf => leaf;
    public Issue6694DockerNetwork Network => network;
    public bool Initialized { get; private set; }

    public Task InitializeAsync()
    {
        if (!leaf.Initialized || !network.Initialized)
        {
            throw new InvalidOperationException("Dependencies weren't initialized before Issue6694MultiDependency");
        }

        Initialized = true;
        return Task.CompletedTask;
    }
}

#endregion

[EngineTest(ExpectedResult.Pass)]
[ClassDataSource<Issue6694NatsContainer>(Shared = SharedType.PerTestSession)]
public sealed class Issue6694ConstructorInjectionTests(Issue6694NatsContainer natsContainer)
{
    [ClassDataSource<Issue6694DockerNetwork>(Shared = SharedType.PerTestSession)]
    public required Issue6694DockerNetwork SessionNetwork { get; init; }

    [Test]
    public async Task Container_And_Its_Constructor_Dependency_Are_Initialized()
    {
        await Assert.That(natsContainer.Initialized).IsTrue();
        await Assert.That(natsContainer.Network.Initialized).IsTrue();
    }

    [Test]
    public async Task Constructor_Dependency_Respects_Its_Own_Sharing()
    {
        await Assert.That(natsContainer.Network).IsSameReferenceAs(SessionNetwork);
    }
}

[EngineTest(ExpectedResult.Pass)]
public sealed class Issue6694NestedConstructorInjectionTests
{
    [Test]
    [ClassDataSource<Issue6694Top>]
    public async Task Chained_Constructor_Dependencies_Are_Initialized_Deepest_First(Issue6694Top top)
    {
        await Assert.That(top.Initialized).IsTrue();
        await Assert.That(top.Upper.Initialized).IsTrue();
        await Assert.That(top.Upper.Middle.Initialized).IsTrue();
        await Assert.That(top.Upper.Middle.Leaf.Initialized).IsTrue();
    }

    [Test]
    [ClassDataSource(typeof(Issue6694Upper), typeof(Issue6694Upper))]
    public async Task Unshared_Parents_Get_Their_Own_Unshared_Dependencies(Issue6694Upper first, Issue6694Upper second)
    {
        await Assert.That(first).IsNotSameReferenceAs(second);
        await Assert.That(first.Middle).IsNotSameReferenceAs(second.Middle);
        await Assert.That(first.Initialized).IsTrue();
        await Assert.That(second.Initialized).IsTrue();
    }

    [Test]
    [ClassDataSource<Issue6694MultiDependency>]
    public async Task Multiple_Constructor_Dependencies_From_One_Attribute(Issue6694MultiDependency dependency)
    {
        await Assert.That(dependency.Initialized).IsTrue();
        await Assert.That(dependency.Leaf.Initialized).IsTrue();
        await Assert.That(dependency.Network.Initialized).IsTrue();
    }
}

using TUnit.Core.SourceGenerator.Generators;

namespace TUnit.Core.SourceGenerator.Tests;

/// <summary>
/// Types that take their own dependencies through a constructor, supplied by a class-level ClassDataSource attribute,
/// get a source-generated constructor registration so they work without reflection (#6694).
/// Test classes and types with a public parameterless constructor get none.
/// </summary>
internal class Issue6694ConstructorInjectedClassDataSourceTests
{
    private readonly TestsBase<PropertyInjectionSourceGenerator> _generator = new();

    [Test]
    public Task Test() => _generator.RunTest(Path.Combine(Git.TestsDirectory.FullName,
            "TUnit.TestProject",
            "Bugs",
            "_6694",
            "ConstructorInjectedClassDataSourceTests.cs"),
        async generatedFiles =>
        {
            await Assert.That(generatedFiles.Where(x => x.Contains("ClassDataSourceConstructorRegistry.Register("))).HasCount(4);
        });
}

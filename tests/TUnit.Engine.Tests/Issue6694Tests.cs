using Shouldly;
using TUnit.Engine.Tests.Enums;

namespace TUnit.Engine.Tests;

public class Issue6694Tests(TestMode testMode) : InvokableTestBase(testMode)
{
    [Test]
    public async Task ClassDataSource_Type_Can_Receive_Dependencies_Through_Its_Constructor()
    {
        await RunTestsWithFilter(
            "/*/*/Issue6694ConstructorInjectionTests/*",
            [
                result => result.ResultSummary.Outcome.ShouldBe("Completed"),
                result => result.ResultSummary.Counters.Total.ShouldBe(2),
                result => result.ResultSummary.Counters.Passed.ShouldBe(2),
                result => result.ResultSummary.Counters.Failed.ShouldBe(0),
                result => result.ResultSummary.Counters.NotExecuted.ShouldBe(0)
            ]);
    }

    [Test]
    public async Task Nested_Constructor_Dependencies_Are_Resolved_And_Initialized()
    {
        await RunTestsWithFilter(
            "/*/*/Issue6694NestedConstructorInjectionTests/*",
            [
                result => result.ResultSummary.Outcome.ShouldBe("Completed"),
                result => result.ResultSummary.Counters.Total.ShouldBe(3),
                result => result.ResultSummary.Counters.Passed.ShouldBe(3),
                result => result.ResultSummary.Counters.Failed.ShouldBe(0),
                result => result.ResultSummary.Counters.NotExecuted.ShouldBe(0)
            ]);
    }
}

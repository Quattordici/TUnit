#if NET10_0_OR_GREATER
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TUnit.AspNetCore;

namespace TUnit.AspNetCore.Tests;

/// <summary>
/// Regression coverage for thomhurst/TUnit#6955: a factory configured with
/// <c>WebApplicationFactory.UseKestrel</c> must give every test an isolated application that
/// listens on a real socket, and clients created for that test must target it. The isolated
/// factory comes from <c>WithWebHostBuilder</c>, which loses the Kestrel settings and delegates
/// <c>ConfigureClient</c> to the never-started parent (dotnet/aspnetcore#69655).
/// </summary>
public class KestrelWebApplicationTests : WebApplicationTest<KestrelTestFactory, Program>
{
    [Test]
    public async Task Isolated_Factory_Uses_Kestrel()
    {
        await Assert.That(Services.GetRequiredService<IServer>()).IsNotTypeOf<TestServer>();
    }

    [Test]
    public async Task CreateClient_Targets_The_Isolated_Server()
    {
        using var client = Factory.CreateClient();

        await AssertListensOnLoopback(client.BaseAddress);
        await Assert.That(await client.GetStringAsync("/ping")).IsEqualTo("pong");
    }

    [Test]
    public async Task CreateClient_Follows_Redirects_Over_The_Network()
    {
        using var client = Factory.CreateClient();

        var response = await client.GetAsync("/redirect");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.RequestMessage!.RequestUri!.Port).IsEqualTo(client.BaseAddress!.Port);
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("pong");
    }

    [Test]
    public async Task CreateDefaultClient_Targets_The_Isolated_Server()
    {
        using var client = Factory.CreateDefaultClient();

        await Assert.That(client.BaseAddress).IsEqualTo(Factory.Inner.ClientOptions.BaseAddress);
        await AssertListensOnLoopback(client.BaseAddress);
        await Assert.That(await client.GetStringAsync("/ping")).IsEqualTo("pong");
    }

    [Test]
    public async Task Inner_Factory_CreateClient_Targets_The_Isolated_Server()
    {
        using var client = Factory.Inner.CreateClient();

        await AssertListensOnLoopback(client.BaseAddress);
        await Assert.That(await client.GetStringAsync("/ping")).IsEqualTo("pong");
    }

    [Test]
    public async Task Server_Is_Reachable_Without_Factory_Handlers()
    {
        using var client = new HttpClient { BaseAddress = Factory.Inner.ClientOptions.BaseAddress };

        await Assert.That(await client.GetStringAsync("/ping")).IsEqualTo("pong");
    }

    internal static async Task AssertListensOnLoopback(Uri? address)
    {
        await Assert.That(address).IsNotNull();
        await Assert.That(IPAddress.Parse(address!.Host)).IsEqualTo(IPAddress.Loopback);
        await Assert.That(address.Port).IsGreaterThan(0);
    }
}

/// <summary>
/// <c>UseKestrel</c> called from <c>ConfigureWebHost</c>, as in the original report, rather than
/// from the constructor: the parent only switches to Kestrel while the isolated host is built.
/// </summary>
public class KestrelConfiguredInConfigureWebHostTests
    : WebApplicationTest<KestrelInConfigureWebHostTestFactory, Program>
{
    [Test]
    public async Task CreateClient_Targets_The_Isolated_Kestrel_Server()
    {
        await Assert.That(Services.GetRequiredService<IServer>()).IsNotTypeOf<TestServer>();

        using var client = Factory.CreateClient();

        await KestrelWebApplicationTests.AssertListensOnLoopback(client.BaseAddress);
        await Assert.That(await client.GetStringAsync("/ping")).IsEqualTo("pong");
    }
}

/// <summary>
/// The <c>UseKestrel(Action&lt;KestrelServerOptions&gt;)</c> callback must reach the isolated server.
/// </summary>
public class KestrelOptionsTests : WebApplicationTest<KestrelOptionsTestFactory, Program>
{
    [Test]
    public async Task Kestrel_Options_Apply_To_The_Isolated_Server()
    {
        using var client = Factory.CreateClient();

        var response = await client.GetAsync("/ping");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.Contains("Server")).IsFalse();
    }
}

public class KestrelPerTestFactoryTests : PerTestWebApplicationTest<KestrelTestFactory, Program>
{
    [Test]
    public async Task Each_Created_Factory_Listens_On_Its_Own_Address()
    {
        var first = await CreateFactoryAsync();
        var second = await CreateFactoryAsync();

        using var firstClient = first.CreateClient();
        using var secondClient = second.CreateClient();

        await Assert.That(firstClient.BaseAddress).IsNotEqualTo(secondClient.BaseAddress);
        await Assert.That(await firstClient.GetStringAsync("/ping")).IsEqualTo("pong");
        await Assert.That(await secondClient.GetStringAsync("/ping")).IsEqualTo("pong");
    }
}

public class KestrelTestFactory : TestWebApplicationFactory<Program>
{
    public KestrelTestFactory() => UseKestrel(0);
}

public class KestrelInConfigureWebHostTestFactory : TestWebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        UseKestrel(0);
    }
}

public class KestrelOptionsTestFactory : TestWebApplicationFactory<Program>
{
    public KestrelOptionsTestFactory()
    {
        UseKestrel(0);
        UseKestrel(options => options.AddServerHeader = false);
    }
}
#endif

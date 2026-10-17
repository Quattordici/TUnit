#if NET10_0_OR_GREATER
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace TUnit.AspNetCore;

/// <summary>
/// Reads the Kestrel settings that <see cref="WebApplicationFactory{TEntryPoint}.UseKestrel()"/> stores
/// in private fields. <see cref="WebApplicationFactory{TEntryPoint}.WithWebHostBuilder"/> does not copy
/// them to the derived factory, which then starts a <c>TestServer</c> and delegates
/// <c>ConfigureClient</c> to the parent (dotnet/aspnetcore#69655). TUnit builds every isolated factory
/// with <c>WithWebHostBuilder</c>, so it carries these settings over itself.
/// </summary>
/// <remarks>
/// If a later Microsoft.AspNetCore.Mvc.Testing release renames the fields, the accessors throw
/// <see cref="MissingFieldException"/>; the helpers then report "not Kestrel" and change nothing.
/// </remarks>
internal static class KestrelFactoryState<TEntryPoint> where TEntryPoint : class
{
    private static volatile bool _fieldsMissing;

    /// <summary>
    /// Makes <paramref name="derived"/> use Kestrel with the settings of <paramref name="parent"/>, if
    /// the parent uses Kestrel and the derived factory does not. Must run while the derived host is
    /// being configured: after the parent's <c>ConfigureWebHost</c>, which may call <c>UseKestrel</c>,
    /// and before the derived factory decides which server to register.
    /// </summary>
    public static void Inherit(WebApplicationFactory<TEntryPoint> parent, WebApplicationFactory<TEntryPoint> derived)
    {
        if (!UsesKestrel(parent) || UsesKestrel(derived))
        {
            return;
        }

        derived.UseKestrel();

        if (KestrelPort(parent) is { } port)
        {
            derived.UseKestrel(port);
        }

        if (ConfigureKestrelOptions(parent) is { } configureKestrelOptions)
        {
            derived.UseKestrel(configureKestrelOptions);
        }
    }

    /// <summary>
    /// Returns whether <paramref name="factory"/> is configured to use Kestrel.
    /// </summary>
    public static bool UsesKestrel(WebApplicationFactory<TEntryPoint> factory) =>
        TryRead(factory, static f => UseKestrelField(f), out var useKestrel) && useKestrel;

    /// <summary>
    /// Returns the address <paramref name="factory"/>'s Kestrel server listens on, or <c>null</c> when
    /// the factory has not started a Kestrel server.
    /// </summary>
    public static Uri? ServerAddress(WebApplicationFactory<TEntryPoint> factory) =>
        TryRead(factory, static f => WebHostAddressField(f), out var address) ? address : null;

    private static int? KestrelPort(WebApplicationFactory<TEntryPoint> factory) =>
        TryRead(factory, static f => KestrelPortField(f), out var port) ? port : null;

    private static Action<KestrelServerOptions>? ConfigureKestrelOptions(WebApplicationFactory<TEntryPoint> factory) =>
        TryRead(factory, static f => ConfigureKestrelOptionsField(f), out var configure) ? configure : null;

    private static bool TryRead<T>(
        WebApplicationFactory<TEntryPoint> factory,
        Func<WebApplicationFactory<TEntryPoint>, T> read,
        out T value)
    {
        if (!_fieldsMissing)
        {
            try
            {
                value = read(factory);
                return true;
            }
            catch (MissingFieldException)
            {
                _fieldsMissing = true;
            }
        }

        value = default!;
        return false;
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_useKestrel")]
    private static extern ref bool UseKestrelField(WebApplicationFactory<TEntryPoint> factory);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_kestrelPort")]
    private static extern ref int? KestrelPortField(WebApplicationFactory<TEntryPoint> factory);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_configureKestrelOptions")]
    private static extern ref Action<KestrelServerOptions>? ConfigureKestrelOptionsField(WebApplicationFactory<TEntryPoint> factory);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_webHostAddress")]
    private static extern ref Uri? WebHostAddressField(WebApplicationFactory<TEntryPoint> factory);
}
#endif

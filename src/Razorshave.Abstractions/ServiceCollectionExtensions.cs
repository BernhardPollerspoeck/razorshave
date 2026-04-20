using Microsoft.Extensions.DependencyInjection;

namespace Razorshave.Abstractions;

/// <summary>
/// Registers Razorshave-provided services (<see cref="IStore{T}"/>, <see cref="ILocalStorage"/>,
/// <see cref="ISessionStorage"/>, <see cref="ICookieStore"/>) into a
/// <see cref="IServiceCollection"/> so components resolve cleanly when the app runs as a
/// Blazor Server dev host. In the transpiled SPA these registrations are ignored — the JS
/// runtime provides its own implementations — so there is no risk of drift.
/// </summary>
public static class RazorshaveServiceCollectionExtensions
{
    /// <summary>
    /// Adds the built-in Razorshave services as singletons. <see cref="IStore{T}"/> is
    /// registered as an open generic so <c>@inject IStore&lt;Todo&gt;</c>, <c>@inject IStore&lt;User&gt;</c>,
    /// and so on all resolve to distinct singletons without further configuration.
    /// </summary>
    public static IServiceCollection AddRazorshave(this IServiceCollection services)
    {
        services.AddSingleton(typeof(IStore<>), typeof(InMemoryStore<>));
        services.AddSingleton<ILocalStorage, InMemoryLocalStorage>();
        services.AddSingleton<ISessionStorage, InMemorySessionStorage>();
        services.AddSingleton<ICookieStore, InMemoryCookieStore>();
        return services;
    }
}

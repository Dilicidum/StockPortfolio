using System.Reflection;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

using StockPortfolio.Modules.Identity.Infrastructure;

namespace StockPortfolio.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Attaches an EF Core interceptor to a module's <c>DbContext</c> from outside the module.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is not two lines.</b> <c>IdentityDbContext</c> is <see langword="internal"/> to
/// <c>Identity.Infrastructure</c> — that is the accessibility rule in <c>CLAUDE.md</c>, and it is the
/// right rule. The consequence is that no test outside that assembly can write
/// <c>DbContextOptions&lt;IdentityDbContext&gt;</c>, so neither
/// <c>optionsBuilder.AddInterceptors(...)</c> nor an <c>IDbContextOptionsConfiguration&lt;T&gt;</c>
/// registration is expressible here.
/// </para>
/// <para>
/// The seam that <i>is</i> reachable is the service descriptor.
/// <c>AddDbContext&lt;TContext&gt;</c> registers <c>DbContextOptions&lt;TContext&gt;</c> with an
/// implementation factory, and everything downstream — the context itself and the non-generic
/// <c>DbContextOptions</c> — resolves through it. Replacing that one descriptor with a factory that
/// calls the original and then adds interceptors leaves the module's own configuration (provider,
/// connection string, migrations history table) exactly as the module wrote it.
/// </para>
/// <para>
/// The type is found by reflection over the module's assembly rather than by name, so a rename or a
/// namespace move cannot turn this into a silently-skipped no-op. It fails loudly instead: an
/// interceptor that quietly stops being attached would make
/// <c>Queries_NeverInlineUserInput_IntoCommandText</c> pass by recording nothing, which is the one
/// failure mode a parameterisation proof must not have.
/// </para>
/// <para>
/// <b>If this ever stops working</b>, the honest fix is a test-only seam in the module, not a weaker
/// assertion — say so rather than dropping the interceptor.
/// </para>
/// </remarks>
internal static class ModuleDbContextInterceptors
{
    /// <summary>
    /// Adds <paramref name="interceptors"/> to the Identity module's <c>DbContext</c> registration.
    /// </summary>
    /// <param name="services">The container being configured, after the module has registered itself.</param>
    /// <param name="interceptors">The interceptors to attach.</param>
    public static void AddToIdentity(IServiceCollection services, params IInterceptor[] interceptors)
    {
        ArgumentNullException.ThrowIfNull(services);

        var contextType = IdentityDbContextType();

        var attach = typeof(ModuleDbContextInterceptors)
            .GetMethod(nameof(AddToContext), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(contextType);

        attach.Invoke(null, [services, interceptors]);
    }

    /// <summary>
    /// Finds the single <see cref="DbContext"/> declared by <c>Identity.Infrastructure</c>.
    /// </summary>
    /// <returns>The context type, which is <see langword="internal"/> and cannot be named in source.</returns>
    /// <remarks>
    /// <c>IdentityModule</c> is the module's one public type, so it is the only handle a test has on
    /// the assembly — the same seam <c>Program.cs</c> uses.
    /// </remarks>
    public static Type IdentityDbContextType()
    {
        var candidates = typeof(IdentityModule).Assembly
            .GetTypes()
            .Where(type => typeof(DbContext).IsAssignableFrom(type) && !type.IsAbstract)
            .ToArray();

        return candidates.Length == 1
            ? candidates[0]
            : throw new InvalidOperationException(
                $"Expected exactly one DbContext in {typeof(IdentityModule).Assembly.GetName().Name}, found "
                + $"{candidates.Length}. The integration tests reach the context by reflection because it is "
                + "internal to the module; update ModuleDbContextInterceptors if the module grew a second one.");
    }

    private static void AddToContext<TContext>(IServiceCollection services, IInterceptor[] interceptors)
        where TContext : DbContext
    {
        var descriptor = services.LastOrDefault(service => service.ServiceType == typeof(DbContextOptions<TContext>))
            ?? throw new InvalidOperationException(
                $"No DbContextOptions<{typeof(TContext).Name}> is registered. AddIdentityModule must run "
                + "before the interceptor is attached — ConfigureTestServices runs after the application's "
                + "own registrations, which is why it is the right hook.");

        var original = descriptor.ImplementationFactory
            ?? throw new InvalidOperationException(
                $"DbContextOptions<{typeof(TContext).Name}> is registered without an implementation factory. "
                + "AddDbContext always registers one; a different registration shape means this helper is "
                + "wrapping the wrong descriptor.");

        services.Remove(descriptor);

        services.Add(new ServiceDescriptor(
            typeof(DbContextOptions<TContext>),
            provider => new DbContextOptionsBuilder<TContext>((DbContextOptions<TContext>)original(provider))
                .AddInterceptors(interceptors)
                .Options,
            descriptor.Lifetime));
    }
}

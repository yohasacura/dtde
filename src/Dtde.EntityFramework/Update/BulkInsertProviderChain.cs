namespace Dtde.EntityFramework.Update;

/// <summary>
/// Resolved chain of <see cref="IBulkInsertProvider"/> instances in
/// precedence order — provider-specific implementations first, fallback
/// (<see cref="DefaultBulkInsertProvider"/>) last. The chain is exposed as
/// a single DI service because EF Core's <c>context.GetService&lt;T&gt;</c>
/// resolves single types; <c>IEnumerable&lt;T&gt;</c> resolution doesn't
/// consistently fall through to the application service provider.
/// </summary>
public sealed class BulkInsertProviderChain
{
    /// <summary>
    /// Initializes a new chain.
    /// </summary>
    /// <param name="providers">All registered providers, in DI registration order.</param>
    public BulkInsertProviderChain(IEnumerable<IBulkInsertProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        // Move the default to the tail so a more specific provider always
        // wins. Stable order is important for predictability when multiple
        // custom providers are registered.
        Providers = providers
            .OrderBy(p => p is DefaultBulkInsertProvider ? 1 : 0)
            .ToArray();
    }

    /// <summary>
    /// Providers in dispatch order.
    /// </summary>
    public IReadOnlyList<IBulkInsertProvider> Providers { get; }
}

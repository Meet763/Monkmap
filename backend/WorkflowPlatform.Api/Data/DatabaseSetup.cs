using Microsoft.EntityFrameworkCore;

namespace WorkflowPlatform.Api.Data;

/// <summary>
/// Chooses the platform-storage database from configuration and wires up the DbContext.
///
/// Configuration (appsettings.json, or environment variables Database__Provider /
/// Database__ConnectionString, or user-secrets):
///
///   "Database": {
///     "Provider": "Sqlite",                              // see KnownProviders for options
///     "ConnectionString": "Data Source=workflowplatform.db"
///   }
///
/// Defaults to SQLite so the project runs with zero setup.
/// </summary>
public static class DatabaseSetup
{
    /// <summary>
    /// The databases this build can use. Add a new <see cref="IDatabaseProvider"/> here to
    /// support another database — see DatabaseProviders.cs for a template.
    /// </summary>
    public static readonly IReadOnlyList<IDatabaseProvider> KnownProviders = new IDatabaseProvider[]
    {
        new SqliteDatabaseProvider(),
        new PostgresDatabaseProvider(),
        new SqlServerDatabaseProvider(),
    };

    public const string DefaultSqliteConnectionString = "Data Source=workflowplatform.db";

    public static IServiceCollection AddApplicationDatabase(
        this IServiceCollection services, IConfiguration config)
    {
        var providerKey = (config["Database:Provider"] ?? "Sqlite").Trim();

        var connectionString =
            config["Database:ConnectionString"]
            ?? config.GetConnectionString("Default")
            ?? DefaultSqliteConnectionString;

        var provider = KnownProviders.FirstOrDefault(p =>
            p.Keys.Contains(providerKey, StringComparer.OrdinalIgnoreCase));

        if (provider is null)
        {
            var available = string.Join(", ", KnownProviders.Select(p =>
                $"{p.Name} ({string.Join("/", p.Keys)})"));
            throw new InvalidOperationException(
                $"Unknown Database:Provider '{providerKey}'. Available providers: {available}. " +
                "To add another database, implement IDatabaseProvider and register it in " +
                "DatabaseSetup.KnownProviders (see Data/DatabaseProviders.cs).");
        }

        services.AddDbContext<AppDbContext>(opt => provider.Configure(opt, connectionString));
        return services;
    }
}

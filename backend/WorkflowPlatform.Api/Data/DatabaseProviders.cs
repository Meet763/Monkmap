using Microsoft.EntityFrameworkCore;

namespace WorkflowPlatform.Api.Data;

/// <summary>
/// A pluggable platform-storage backend. To support a new database, implement this interface
/// and add the class to the list in <see cref="DatabaseSetup.KnownProviders"/>.
///
/// Each provider only needs to know two things:
///   • which config key(s) select it (e.g. "postgres", "postgresql")
///   • how to configure EF Core's DbContextOptionsBuilder for its database
///
/// See the commented MySQL example at the bottom of this file for a step-by-step template.
/// </summary>
public interface IDatabaseProvider
{
    /// <summary>Canonical name shown in docs/errors, e.g. "Postgres".</summary>
    string Name { get; }

    /// <summary>Config values (case-insensitive) that select this provider. Include aliases.</summary>
    IReadOnlyCollection<string> Keys { get; }

    /// <summary>Wire EF Core up to this database.</summary>
    void Configure(DbContextOptionsBuilder options, string connectionString);
}

public sealed class SqliteDatabaseProvider : IDatabaseProvider
{
    public string Name => "SQLite";
    public IReadOnlyCollection<string> Keys => new[] { "sqlite" };
    public void Configure(DbContextOptionsBuilder options, string cs) => options.UseSqlite(cs);
}

public sealed class PostgresDatabaseProvider : IDatabaseProvider
{
    public string Name => "PostgreSQL";
    public IReadOnlyCollection<string> Keys => new[] { "postgres", "postgresql", "npgsql" };
    public void Configure(DbContextOptionsBuilder options, string cs) => options.UseNpgsql(cs);
}

public sealed class SqlServerDatabaseProvider : IDatabaseProvider
{
    public string Name => "SQL Server";
    public IReadOnlyCollection<string> Keys => new[] { "sqlserver", "mssql" };
    public void Configure(DbContextOptionsBuilder options, string cs) => options.UseSqlServer(cs);
}

/*
 * ────────────────────────────────────────────────────────────────────────────
 * HOW TO ADD A NEW DATABASE (contributor guide)
 * ────────────────────────────────────────────────────────────────────────────
 * 1. Add the EF Core provider package to WorkflowPlatform.Api.csproj, e.g. MySQL:
 *        dotnet add package Pomelo.EntityFrameworkCore.MySql
 *
 * 2. Implement IDatabaseProvider (uncomment and adapt):
 *
 *    public sealed class MySqlDatabaseProvider : IDatabaseProvider
 *    {
 *        public string Name => "MySQL";
 *        public IReadOnlyCollection<string> Keys => new[] { "mysql", "mariadb" };
 *        public void Configure(DbContextOptionsBuilder options, string cs) =>
 *            options.UseMySql(cs, ServerVersion.AutoDetect(cs));
 *    }
 *
 * 3. Register it by adding `new MySqlDatabaseProvider()` to DatabaseSetup.KnownProviders.
 *
 * 4. In appsettings.json set:  "Database": { "Provider": "MySql", "ConnectionString": "..." }
 *
 * That's the whole change — nothing else in the app needs to know about the new database.
 * ────────────────────────────────────────────────────────────────────────────
 */

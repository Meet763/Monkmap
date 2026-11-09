using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;

namespace WorkflowPlatform.Api.Engine.Nodes;

/// <summary>
/// Connects to an external SQL database and runs a query or statement as a workflow step.
/// Supports PostgreSQL, MySQL, SQL Server and SQLite via ADO.NET.
///
/// NOTE: this executes raw SQL by design (it's the user's own database). The connection string
/// is stored in the workflow graph for the MVP; move it into the (encrypted) credentials store
/// before exposing this to untrusted users — see plan §5 / §13.
/// </summary>
public class DatabaseNode : INode
{
    public NodeDefinition Definition => new()
    {
        Type = "Database",
        DisplayName = "Database (SQL)",
        Group = "Apps",
        Icon = "database",
        OutputPorts = new[] { "main" },
        Parameters = new()
        {
            new NodeParameter
            {
                Name = "provider", DisplayName = "Provider", Type = "options",
                Options = new[] { "PostgreSQL", "MySQL", "SqlServer", "Sqlite" },
                Default = "PostgreSQL"
            },
            new NodeParameter
            {
                Name = "connectionString", DisplayName = "Connection string", Type = "string",
                Required = true,
                Placeholder = "Host=localhost;Database=mydb;Username=postgres;Password=..."
            },
            new NodeParameter
            {
                Name = "operation", DisplayName = "Operation", Type = "options",
                Options = new[] { "Query", "Execute" }, Default = "Query"
            },
            new NodeParameter
            {
                Name = "sql", DisplayName = "SQL", Type = "text", Required = true,
                Placeholder = "SELECT * FROM users WHERE id = {{$json.id}}"
            }
        }
    };

    public async Task<NodeResult> ExecuteAsync(NodeExecutionContext ctx)
    {
        var provider = ctx.GetString("provider", "PostgreSQL");
        var connectionString = ctx.ResolveString("connectionString");
        var operation = ctx.GetString("operation", "Query");
        var sql = ctx.ResolveString("sql");

        if (string.IsNullOrWhiteSpace(connectionString))
            return NodeResult.Fail("Connection string is required.");
        if (string.IsNullOrWhiteSpace(sql))
            return NodeResult.Fail("SQL is required.");

        DbConnection conn;
        try { conn = CreateConnection(provider, connectionString); }
        catch (Exception ex) { return NodeResult.Fail($"Invalid provider/connection: {ex.Message}"); }

        try
        {
            await conn.OpenAsync(ctx.CancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;

            if (operation.Equals("Execute", StringComparison.OrdinalIgnoreCase))
            {
                var affected = await cmd.ExecuteNonQueryAsync(ctx.CancellationToken);
                return NodeResult.Ok(JsonHelpers.FromObject(new { affectedRows = affected }));
            }

            var rows = new List<Dictionary<string, object?>>();
            await using var reader = await cmd.ExecuteReaderAsync(ctx.CancellationToken);
            while (await reader.ReadAsync(ctx.CancellationToken))
            {
                var row = new Dictionary<string, object?>();
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var value = await reader.IsDBNullAsync(i, ctx.CancellationToken)
                        ? null
                        : reader.GetValue(i);
                    row[reader.GetName(i)] = value;
                }
                rows.Add(row);
            }

            return NodeResult.Ok(JsonHelpers.FromObject(new { count = rows.Count, rows }));
        }
        catch (Exception ex)
        {
            return NodeResult.Fail($"Database error: {ex.Message}");
        }
        finally
        {
            await conn.DisposeAsync();
        }
    }

    private static DbConnection CreateConnection(string provider, string cs) =>
        provider.ToLowerInvariant() switch
        {
            "postgresql" or "postgres" => new NpgsqlConnection(cs),
            "mysql" => new MySqlConnection(cs),
            "sqlserver" or "mssql" => new SqlConnection(cs),
            "sqlite" => new SqliteConnection(cs),
            _ => throw new InvalidOperationException($"Unknown provider '{provider}'.")
        };
}

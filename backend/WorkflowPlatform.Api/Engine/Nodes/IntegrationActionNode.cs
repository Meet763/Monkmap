using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WorkflowPlatform.Api.Data;
using WorkflowPlatform.Api.Integrations;

namespace WorkflowPlatform.Api.Engine.Nodes;

/// <summary>
/// Runs a reusable integration action. Three shapes feed into the same code:
///   • The generic "IntegrationAction" node — pick an action, a connection, and a JSON input.
///   • An n8n-style app node ("app.&lt;provider&gt;.&lt;service&gt;", e.g. app.google.gmail) — pick the
///     Operation from a dropdown and fill the fields that operation needs; the account comes from a
///     dropdown. Fields are namespaced "&lt;actionKey&gt;::&lt;field&gt;" so operations never collide.
///   • (Back-compat) a single-action node "app.&lt;actionKey&gt;" with bare field names.
/// Token refresh and API calls are handled centrally — no integration code here.
/// </summary>
public sealed class IntegrationActionNode : INode
{
    public const string AppPrefix = "app.";
    public const string FieldSeparator = "::";

    private readonly IntegrationActionRegistry _actions;

    public IntegrationActionNode(IntegrationActionRegistry actions) => _actions = actions;

    public NodeDefinition Definition => new()
    {
        Type = "IntegrationAction",
        DisplayName = "Integration Action",
        Group = "Apps",
        Icon = "plug",
        OutputPorts = new[] { "main" },
        Parameters = new()
        {
            new NodeParameter
            {
                Name = "action", DisplayName = "Action", Type = "options",
                Options = _actions.All.Select(a => a.Key).DefaultIfEmpty("(none registered)").ToArray(),
                Default = _actions.All.Select(a => a.Key).FirstOrDefault()
            },
            new NodeParameter
            {
                Name = "connectionId", DisplayName = "Connection id", Type = "string", Required = true,
                Placeholder = "copy from the Connections page"
            },
            new NodeParameter
            {
                Name = "input", DisplayName = "Input (JSON)", Type = "json", Default = "{}",
                Placeholder = "{ \"summary\": \"Meeting\", \"start\": \"...\", \"end\": \"...\" }"
            }
        }
    };

    /// <summary>
    /// Build one n8n-style palette node per app/service: the actions are grouped by their service
    /// (the second segment of the key, e.g. google.<b>gmail</b>.send). Each node has an Operation
    /// dropdown plus the union of every operation's fields, each shown only for its operation.
    /// </summary>
    public static IEnumerable<NodeDefinition> BuildServiceDefinitions(IEnumerable<IIntegrationAction> actions)
    {
        var groups = actions
            .GroupBy(a => (a.Provider, Service: ServiceKey(a.Key)))
            .OrderBy(g => g.Key.Provider).ThenBy(g => g.Key.Service);

        foreach (var group in groups)
        {
            var ops = group.OrderBy(a => a.DisplayName).ToList();

            var parameters = new List<NodeParameter>
            {
                new()
                {
                    Name = "connectionId", DisplayName = "Account", Type = "connection", Required = true,
                    Placeholder = group.Key.Provider   // the config panel reads the provider from here
                },
                new()
                {
                    Name = "operation", DisplayName = "Operation", Type = "options",
                    Options = ops.Select(a => a.Key).ToArray(),
                    OptionLabels = ops.Select(a => OperationLabel(a.DisplayName)).ToArray(),
                    Default = ops[0].Key
                }
            };

            // Each operation contributes its own fields, visible only when that operation is chosen.
            foreach (var action in ops)
                foreach (var field in FieldsFromSchema(action.InputSchema))
                {
                    field.Name = action.Key + FieldSeparator + field.Name;
                    field.VisibleWhen = new ParamVisibility("operation", new[] { action.Key });
                    parameters.Add(field);
                }

            yield return new NodeDefinition
            {
                Type = AppPrefix + group.Key.Provider + "." + group.Key.Service,
                DisplayName = AppDisplayName(ops[0].DisplayName, group.Key.Service),
                Group = "Apps",
                Icon = IconForService(group.Key.Service),
                OutputPorts = new[] { "main" },
                Parameters = parameters
            };
        }
    }

    public async Task<NodeResult> ExecuteAsync(NodeExecutionContext ctx)
    {
        var type = ctx.Node.Type;
        var appMode = type.StartsWith(AppPrefix, StringComparison.OrdinalIgnoreCase);

        // Resolve the action and how its fields are named:
        //   generic node         → action from "action" param,    input from JSON blob
        //   app.<actionKey>       → action from the type,          fields are bare names (back-compat)
        //   app.<provider>.<svc>  → action from "operation" param, fields are "<actionKey>::<field>"
        string actionKey;
        string fieldPrefix;
        if (!appMode)
        {
            actionKey = ctx.GetString("action");
            fieldPrefix = "";
        }
        else
        {
            var remainder = type[AppPrefix.Length..];
            if (_actions.TryGet(remainder, out _))
            {
                actionKey = remainder;          // app.<actionKey> (single-action, back-compat)
                fieldPrefix = "";
            }
            else
            {
                actionKey = ctx.GetString("operation");   // app.<provider>.<service>
                fieldPrefix = actionKey + FieldSeparator;
            }
        }

        if (!_actions.TryGet(actionKey, out var action))
            return NodeResult.Fail($"Unknown action '{actionKey}'.");

        if (!Guid.TryParse(ctx.ResolveString("connectionId"), out var connectionId))
            return NodeResult.Fail(appMode ? "Select a connected account first." : "A valid Connection id is required.");

        var db = ctx.Services.GetRequiredService<AppDbContext>();
        var connection = await db.Connections.FirstOrDefaultAsync(
            c => c.Id == connectionId &&
                 (ctx.OwnerUserId == null || c.UserId == ctx.OwnerUserId),
            ctx.CancellationToken);
        if (connection is null)
            return NodeResult.Fail("Connection not found (or not owned by this user).");
        if (!connection.Provider.Equals(action.Provider, StringComparison.OrdinalIgnoreCase))
            return NodeResult.Fail($"This connection is '{connection.Provider}', but the action needs '{action.Provider}'.");

        JsonElement input;
        try
        {
            input = appMode
                ? BuildInputFromFields(action, ctx, fieldPrefix)
                : BuildInputFromJson(ctx);
        }
        catch (Exception ex) { return NodeResult.Fail($"Input is not valid: {ex.Message}"); }

        try
        {
            var tokenManager = ctx.Services.GetRequiredService<ITokenManager>();
            var token = await tokenManager.GetAccessTokenAsync(connection, ctx.CancellationToken);

            var actionCtx = new IntegrationActionContext
            {
                AccessToken = token,
                HttpClientFactory = ctx.Services.GetRequiredService<IHttpClientFactory>(),
                Logger = ctx.Logger,
                CancellationToken = ctx.CancellationToken
            };
            var result = await action.ExecuteAsync(actionCtx, input);
            return NodeResult.Ok(result);
        }
        catch (Exception ex)
        {
            return NodeResult.Fail(ex.Message);
        }
    }

    // ---- Generic node: one JSON blob with {{ }} resolved inside string values ----

    private static JsonElement BuildInputFromJson(NodeExecutionContext ctx)
    {
        var template = JsonNode.Parse(ctx.GetString("input", "{}") is { Length: > 0 } s ? s : "{}");
        return JsonHelpers.Parse(ResolveStrings(template, ctx).ToJsonString());
    }

    // ---- App node: assemble a typed JSON object from the individual form fields ----

    private static JsonElement BuildInputFromFields(IIntegrationAction action, NodeExecutionContext ctx, string prefix)
    {
        var obj = new JsonObject();
        if (action.InputSchema.TryGetProperty("properties", out var props) &&
            props.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in props.EnumerateObject())
            {
                var paramName = prefix + prop.Name;
                if (ctx.GetParam(paramName) is null) continue;
                var raw = ctx.GetString(paramName);
                if (string.IsNullOrWhiteSpace(raw)) continue;

                var resolved = ExpressionResolver.Resolve(raw, ctx.FirstInput, ctx.NodeOutputs);
                var type = prop.Value.TryGetProperty("type", out var t) ? t.GetString() : "string";
                obj[prop.Name] = Coerce(resolved, type);
            }
        }
        return JsonHelpers.Parse(obj.ToJsonString());
    }

    /// <summary>Turn a resolved string into the JSON type the action's schema expects.</summary>
    private static JsonNode? Coerce(string value, string? schemaType) => schemaType switch
    {
        "integer" when long.TryParse(value, out var l) => JsonValue.Create(l),
        "number" when double.TryParse(value, out var d) => JsonValue.Create(d),
        "boolean" when bool.TryParse(value, out var b) => JsonValue.Create(b),
        "array" or "object" => TryParseJson(value),
        _ => JsonValue.Create(value)
    };

    private static JsonNode? TryParseJson(string value)
    {
        try { return JsonNode.Parse(value); }
        catch { return JsonValue.Create(value); }
    }

    // ---- Schema → form fields ----

    private static List<NodeParameter> FieldsFromSchema(JsonElement schema)
    {
        var fields = new List<NodeParameter>();
        if (!schema.TryGetProperty("properties", out var props) || props.ValueKind != JsonValueKind.Object)
            return fields;

        var required = new HashSet<string>(StringComparer.Ordinal);
        if (schema.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.Array)
            foreach (var r in req.EnumerateArray())
                if (r.GetString() is { } name) required.Add(name);

        foreach (var prop in props.EnumerateObject())
        {
            var p = prop.Value;
            var type = p.TryGetProperty("type", out var t) ? t.GetString() : "string";
            var description = p.TryGetProperty("description", out var d) ? d.GetString() : null;

            string[]? options = null;
            if (p.TryGetProperty("enum", out var en) && en.ValueKind == JsonValueKind.Array)
                options = en.EnumerateArray().Select(JsonHelpers.AsString).ToArray();

            var placeholder = description;
            if (type is "array" or "object")
                placeholder = $"{description} (JSON, e.g. [\"a\",\"b\"])".Trim();

            fields.Add(new NodeParameter
            {
                Name = prop.Name,
                DisplayName = Humanize(prop.Name),
                Type = MapType(type, options is not null),
                Required = required.Contains(prop.Name),
                Default = p.TryGetProperty("default", out var def) ? JsonHelpers.AsString(def) : null,
                Options = options,
                Placeholder = placeholder
            });
        }
        return fields;
    }

    private static string MapType(string? schemaType, bool hasEnum) => hasEnum ? "options" : schemaType switch
    {
        "boolean" => "boolean",
        "array" or "object" => "json",
        _ => "string"   // integer/number are entered as text and coerced at run time
    };

    // ---- Naming helpers ----

    /// <summary>Second segment of a dotted key: google.<b>calendar</b>.createEvent → "calendar".</summary>
    private static string ServiceKey(string actionKey)
    {
        var parts = actionKey.Split('.');
        return parts.Length > 1 ? parts[1] : actionKey;
    }

    /// <summary>"Gmail - Send email" → "Send email"; falls back to the whole label.</summary>
    private static string OperationLabel(string actionDisplayName)
    {
        var idx = actionDisplayName.IndexOf(" - ", StringComparison.Ordinal);
        return idx >= 0 ? actionDisplayName[(idx + 3)..] : actionDisplayName;
    }

    /// <summary>"Gmail - Send email" → "Gmail"; falls back to the humanized service key.</summary>
    private static string AppDisplayName(string actionDisplayName, string service)
    {
        var idx = actionDisplayName.IndexOf(" - ", StringComparison.Ordinal);
        return idx >= 0 ? actionDisplayName[..idx] : Humanize(service);
    }

    private static string Humanize(string name)
    {
        var spaced = Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])", " ");
        spaced = spaced.Replace('_', ' ').Replace('-', ' ').Trim();
        return spaced.Length == 0 ? name : char.ToUpper(spaced[0]) + spaced[1..];
    }

    private static string IconForService(string service) => service switch
    {
        "calendar" => "calendar",
        "gmail" => "mail",
        "sheets" => "table",
        "drive" => "hard-drive",
        _ => "plug"
    };

    /// <summary>Copy the JSON tree, replacing {{ }} expressions inside every string value only.</summary>
    private static JsonNode? ResolveStrings(JsonNode? node, NodeExecutionContext ctx)
    {
        switch (node)
        {
            case JsonValue v when v.TryGetValue<string>(out var text):
                return JsonValue.Create(ExpressionResolver.Resolve(text, ctx.FirstInput, ctx.NodeOutputs));
            case JsonObject o:
                var obj = new JsonObject();
                foreach (var kv in o)
                    obj[kv.Key] = ResolveStrings(kv.Value, ctx);
                return obj;
            case JsonArray a:
                var arr = new JsonArray();
                foreach (var item in a)
                    arr.Add(ResolveStrings(item, ctx));
                return arr;
            default:
                return node?.DeepClone();
        }
    }
}

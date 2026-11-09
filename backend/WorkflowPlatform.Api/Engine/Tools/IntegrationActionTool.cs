using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WorkflowPlatform.Api.Data;
using WorkflowPlatform.Api.Integrations;

namespace WorkflowPlatform.Api.Engine.Tools;

/// <summary>
/// Adapts an IIntegrationAction into an IAgentTool so the AI agent can call it. It auto-resolves
/// the owner's connected account for the action's provider, so the model never deals with
/// connection ids — it just calls e.g. google_calendar_createEvent with the event details.
/// </summary>
public sealed class IntegrationActionTool : IAgentTool
{
    private readonly IIntegrationAction _action;

    public IntegrationActionTool(IIntegrationAction action) => _action = action;

    // Tool names allow letters/digits/_/- only — turn dots into underscores.
    public string Name => _action.Key.Replace('.', '_');
    public string Description => $"{_action.Description} (uses the user's connected {_action.Provider} account)";
    public JsonElement ParametersSchema => _action.InputSchema;

    public async Task<string> InvokeAsync(JsonElement args, AgentToolContext ctx)
    {
        var db = ctx.Services.GetRequiredService<AppDbContext>();
        var connection = await db.Connections.FirstOrDefaultAsync(
            c => c.Provider == _action.Provider &&
                 (ctx.OwnerUserId == null || c.UserId == ctx.OwnerUserId),
            ctx.CancellationToken);
        if (connection is null)
            return JsonSerializer.Serialize(new { error = $"No connected {_action.Provider} account. Connect one first." });

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
            var result = await _action.ExecuteAsync(actionCtx, args);
            return result.GetRawText();
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }
}

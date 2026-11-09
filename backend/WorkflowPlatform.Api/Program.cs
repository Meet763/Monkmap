using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using WorkflowPlatform.Api.Data;
using WorkflowPlatform.Api.Engine;
using WorkflowPlatform.Api.Engine.Nodes;
using WorkflowPlatform.Api.Engine.Providers;
using WorkflowPlatform.Api.Engine.Tools;
using WorkflowPlatform.Api.Hubs;
using WorkflowPlatform.Api.Integrations;
using WorkflowPlatform.Api.Integrations.Actions;
using WorkflowPlatform.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ---- Database (provider chosen from config; see Data/DatabaseSetup.cs) ----
builder.Services.AddApplicationDatabase(builder.Configuration);

// ---- JWT auth ----
// The JWT signing key must be a secret. Outside Development it has to come from configuration
// (user-secrets, environment variable Jwt__Key, or your host's secret store). In Development a
// fixed local-only key is used so the app runs with zero setup.
const string JwtPlaceholder = "REPLACE_WITH_A_LONG_RANDOM_SECRET";
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey == JwtPlaceholder)
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException(
            "Jwt:Key is not set. Configure a long random secret (Jwt__Key environment variable or user-secrets).");
    jwtKey = "dev-only-super-secret-key-change-me-please-32bytes!";
}

var jwt = new JwtOptions
{
    Key = jwtKey,
    Issuer = builder.Configuration["Jwt:Issuer"] ?? "WorkflowPlatform",
    Audience = builder.Configuration["Jwt:Audience"] ?? "WorkflowPlatform"
};
builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton<JwtTokenService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key))
        };
        // Allow SignalR to pass the token via query string on the hub route.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var accessToken = ctx.Request.Query["access_token"];
                var path = ctx.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    ctx.Token = accessToken;
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

// ---- Execution engine ----
builder.Services.AddSingleton<INode, ManualTriggerNode>();
builder.Services.AddSingleton<INode, WebhookTriggerNode>();
builder.Services.AddSingleton<INode, SetNode>();
builder.Services.AddSingleton<INode, HttpRequestNode>();
builder.Services.AddSingleton<INode, IfNode>();
builder.Services.AddSingleton<INode, DatabaseNode>();
builder.Services.AddSingleton<INode, AiAgentNode>();
builder.Services.AddSingleton<INode, IntegrationActionNode>();
builder.Services.AddSingleton<NodeRegistry>();

// ---- Chat model providers for the AI Agent node (add a provider = register it here) ----
builder.Services.AddSingleton<IChatModelProvider, OpenAiCompatibleProvider>();
builder.Services.AddSingleton<IChatModelProvider, AnthropicProvider>();
builder.Services.AddSingleton<ChatModelProviderRegistry>();

// ---- Tools the AI Agent (Agent mode) can call (add a tool = register it here) ----
builder.Services.AddSingleton<IAgentTool, HttpRequestTool>();
builder.Services.AddSingleton<IAgentTool, RunWorkflowTool>();
builder.Services.AddSingleton<ToolRegistry>();

// ---- Centralized integration system (OAuth + encrypted tokens + reusable actions) ----
builder.Services.AddDataProtection();
builder.Services.AddSingleton<CredentialProtector>();
builder.Services.AddSingleton<IOAuthProvider, GoogleOAuthProvider>();
builder.Services.AddSingleton<OAuthProviderRegistry>();
builder.Services.AddScoped<ITokenManager, TokenManager>();
// Google integration actions (add an action = register it here; it becomes a node AND an agent tool)
builder.Services.AddSingleton<IIntegrationAction, GoogleCalendarCreateEventAction>();
builder.Services.AddSingleton<IIntegrationAction, GoogleCalendarListEventsAction>();
builder.Services.AddSingleton<IIntegrationAction, GmailSendEmailAction>();
builder.Services.AddSingleton<IIntegrationAction, GoogleSheetsAppendRowAction>();
builder.Services.AddSingleton<IntegrationActionRegistry>();
// Singleton: it creates its own DB scope per run (see WorkflowExecutor.RunAsync).
builder.Services.AddSingleton<WorkflowExecutor>();

// ---- Queue + background worker (in-process; swap for RabbitMQ later) ----
builder.Services.AddSingleton<IExecutionQueue, InProcessExecutionQueue>();
builder.Services.AddHostedService<ExecutionBackgroundService>();

// ---- Real-time ----
builder.Services.AddSignalR();
builder.Services.AddSingleton<IExecutionNotifier, SignalRExecutionNotifier>();

// ---- HTTP client used by the HttpRequest node ----
builder.Services.AddHttpClient("node-http", c => c.Timeout = TimeSpan.FromSeconds(30));

// ---- HTTP client for the AI Agent node (model calls can be slow) ----
builder.Services.AddHttpClient("node-ai", c => c.Timeout = TimeSpan.FromSeconds(120));

// ---- Accounts, team permissions and workflow locks ----
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentAccount>();
builder.Services.AddScoped<WorkflowAccess>();
builder.Services.AddScoped<PermissionCatalog>();

// ---- Web ----
builder.Services.AddControllers();
builder.Services.AddOpenApi();

const string CorsPolicy = "frontend";
builder.Services.AddCors(o => o.AddPolicy(CorsPolicy, p => p
    .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

// Create/update the database on startup (MVP; use migrations for production).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // Versioned schema: applies any pending migrations (creates the database if needed).
    db.Database.Migrate();
}

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseCors(CorsPolicy);

// Turn permission and lock refusals into proper HTTP responses (403 / 423) with a readable message.
app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch (AccessDeniedException ex)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { message = ex.Message });
    }
    catch (WorkflowLockedException ex)
    {
        context.Response.StatusCode = 423;
        await context.Response.WriteAsJsonAsync(new { message = ex.Message });
    }
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ExecutionHub>("/hubs/executions");
app.MapGet("/", () => Results.Ok(new { status = "ok", service = "WorkflowPlatform.Api" }));

app.Run();

using System.Text;
using System.Text.Json;
using WorkflowPlatform.Api.Engine;

namespace WorkflowPlatform.Api.Integrations.Actions;

/// <summary>Send a plain-text email from the user's Gmail account.</summary>
public sealed class GmailSendEmailAction : IIntegrationAction
{
    public string Key => "google.gmail.sendEmail";
    public string Provider => "google";
    public string DisplayName => "Gmail - Send email";
    public string Description => "Send a plain-text email from the user's Gmail account.";

    public JsonElement InputSchema => JsonHelpers.FromObject(new
    {
        type = "object",
        properties = new
        {
            to = new { type = "string", description = "Recipient email address" },
            subject = new { type = "string" },
            body = new { type = "string", description = "Plain-text body" }
        },
        required = new[] { "to", "subject", "body" }
    });

    public Task<JsonElement> ExecuteAsync(IntegrationActionContext ctx, JsonElement input)
    {
        var to = GoogleApi.Str(input, "to");
        var subject = GoogleApi.Str(input, "subject");
        var body = GoogleApi.Str(input, "body");

        var mime = $"To: {to}\r\nSubject: {subject}\r\nContent-Type: text/plain; charset=UTF-8\r\n\r\n{body}";
        var raw = Base64Url(Encoding.UTF8.GetBytes(mime));

        return GoogleApi.CallAsync(ctx, HttpMethod.Post,
            "https://gmail.googleapis.com/gmail/v1/users/me/messages/send", new { raw });
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>Append a row of values to a Google Sheet.</summary>
public sealed class GoogleSheetsAppendRowAction : IIntegrationAction
{
    public string Key => "google.sheets.appendRow";
    public string Provider => "google";
    public string DisplayName => "Google Sheets - Append row";
    public string Description => "Append a row of values to a Google Sheet.";

    public JsonElement InputSchema => JsonHelpers.FromObject(new
    {
        type = "object",
        properties = new
        {
            spreadsheetId = new { type = "string", description = "The id from the sheet URL" },
            range = new { type = "string", description = "Sheet/range, e.g. Sheet1", @default = "Sheet1" },
            values = new { type = "array", items = new { type = "string" }, description = "Cell values, left to right" }
        },
        required = new[] { "spreadsheetId", "values" }
    });

    public Task<JsonElement> ExecuteAsync(IntegrationActionContext ctx, JsonElement input)
    {
        var spreadsheetId = GoogleApi.Str(input, "spreadsheetId");
        var range = GoogleApi.Str(input, "range", "Sheet1");

        var row = new List<object?>();
        if (input.TryGetProperty("values", out var vals) && vals.ValueKind == JsonValueKind.Array)
            foreach (var v in vals.EnumerateArray())
                row.Add(JsonHelpers.AsString(v));

        var url = $"https://sheets.googleapis.com/v4/spreadsheets/{Uri.EscapeDataString(spreadsheetId)}" +
                  $"/values/{Uri.EscapeDataString(range)}:append?valueInputOption=USER_ENTERED";

        return GoogleApi.CallAsync(ctx, HttpMethod.Post, url, new { values = new[] { row } });
    }
}

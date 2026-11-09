using System.Text.Json;
using WorkflowPlatform.Api.Engine;

namespace WorkflowPlatform.Api.Integrations.Actions;

/// <summary>Create an event on the user's primary Google Calendar.</summary>
public sealed class GoogleCalendarCreateEventAction : IIntegrationAction
{
    public string Key => "google.calendar.createEvent";
    public string Provider => "google";
    public string DisplayName => "Google Calendar - Create event";
    public string Description => "Create an event on the user's primary Google Calendar.";

    public JsonElement InputSchema => JsonHelpers.FromObject(new
    {
        type = "object",
        properties = new
        {
            summary = new { type = "string", description = "Event title" },
            description = new { type = "string" },
            start = new { type = "string", description = "ISO 8601 start, e.g. 2026-11-12T10:00:00" },
            end = new { type = "string", description = "ISO 8601 end, e.g. 2026-11-12T10:30:00" },
            timeZone = new { type = "string", description = "IANA TZ, e.g. Asia/Kolkata", @default = "UTC" },
            attendees = new { type = "array", items = new { type = "string" }, description = "Attendee emails" }
        },
        required = new[] { "summary", "start", "end" }
    });

    public Task<JsonElement> ExecuteAsync(IntegrationActionContext ctx, JsonElement input)
    {
        var tz = GoogleApi.Str(input, "timeZone", "UTC");
        var attendees = new List<object>();
        if (input.TryGetProperty("attendees", out var att) && att.ValueKind == JsonValueKind.Array)
            foreach (var a in att.EnumerateArray())
                attendees.Add(new { email = JsonHelpers.AsString(a) });

        var body = new
        {
            summary = GoogleApi.Str(input, "summary"),
            description = GoogleApi.Str(input, "description"),
            start = new { dateTime = GoogleApi.Str(input, "start"), timeZone = tz },
            end = new { dateTime = GoogleApi.Str(input, "end"), timeZone = tz },
            attendees
        };

        return GoogleApi.CallAsync(ctx, HttpMethod.Post,
            "https://www.googleapis.com/calendar/v3/calendars/primary/events", body);
    }
}

/// <summary>List upcoming events from the user's primary Google Calendar.</summary>
public sealed class GoogleCalendarListEventsAction : IIntegrationAction
{
    public string Key => "google.calendar.listEvents";
    public string Provider => "google";
    public string DisplayName => "Google Calendar - List events";
    public string Description => "List upcoming events from the user's primary Google Calendar.";

    public JsonElement InputSchema => JsonHelpers.FromObject(new
    {
        type = "object",
        properties = new
        {
            maxResults = new { type = "integer", @default = 10 },
            timeMin = new { type = "string", description = "ISO 8601 lower bound (default: now)" },
            q = new { type = "string", description = "Free-text search" }
        }
    });

    public async Task<JsonElement> ExecuteAsync(IntegrationActionContext ctx, JsonElement input)
    {
        var max = input.TryGetProperty("maxResults", out var m) && m.TryGetInt32(out var mi) ? mi : 10;
        var timeMin = GoogleApi.Str(input, "timeMin", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        var q = GoogleApi.Str(input, "q");

        var url = $"https://www.googleapis.com/calendar/v3/calendars/primary/events" +
                  $"?singleEvents=true&orderBy=startTime&maxResults={max}" +
                  $"&timeMin={Uri.EscapeDataString(timeMin)}";
        if (!string.IsNullOrWhiteSpace(q)) url += $"&q={Uri.EscapeDataString(q)}";

        return await GoogleApi.CallAsync(ctx, HttpMethod.Get, url);
    }
}

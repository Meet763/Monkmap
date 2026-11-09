using System.Text.Json;

namespace WorkflowPlatform.Api.Engine.Providers;

/// <summary>Who said a message in a chat conversation.</summary>
public enum ChatRole { System, User, Assistant, Tool }

/// <summary>A tool the model is allowed to call (name + description + JSON-schema of its inputs).</summary>
public sealed record ToolSpec(string Name, string Description, JsonElement Parameters);

/// <summary>A request from the model to run a tool with the given JSON arguments.</summary>
public sealed record ToolCall(string Id, string Name, string ArgumentsJson);

/// <summary>One turn in the conversation we send to / receive from the model.</summary>
public sealed class ChatMessage
{
    public ChatRole Role { get; init; }
    public string? Content { get; init; }

    /// <summary>On an assistant turn: tools the model asked us to run.</summary>
    public IReadOnlyList<ToolCall>? ToolCalls { get; init; }

    /// <summary>On a tool turn: which tool call this result answers.</summary>
    public string? ToolCallId { get; init; }

    /// <summary>On a tool turn: the tool's name (some providers need it).</summary>
    public string? ToolName { get; init; }

    public static ChatMessage System(string text) => new() { Role = ChatRole.System, Content = text };
    public static ChatMessage User(string text) => new() { Role = ChatRole.User, Content = text };
    public static ChatMessage Assistant(string? text, IReadOnlyList<ToolCall>? calls = null) =>
        new() { Role = ChatRole.Assistant, Content = text, ToolCalls = calls };
    public static ChatMessage ToolResult(string callId, string name, string content) =>
        new() { Role = ChatRole.Tool, ToolCallId = callId, ToolName = name, Content = content };
}

/// <summary>Everything the provider needs to build one HTTP call to the model.</summary>
public sealed record ChatCompletionRequest(
    string Model,
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyList<ToolSpec> Tools,
    double Temperature,
    int MaxTokens);

/// <summary>What the model replied: free text and/or a set of tool calls to run.</summary>
public sealed record ChatCompletionResult(string? Text, IReadOnlyList<ToolCall> ToolCalls)
{
    public bool WantsTools => ToolCalls.Count > 0;
}

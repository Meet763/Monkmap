using System.Text.Json;

namespace WorkflowPlatform.Api.Engine.Providers;

/// <summary>
/// Adapts one family of chat-model HTTP APIs. Mirrors the IDatabaseProvider registry:
/// add a provider = implement this interface and register it in Program.cs.
///
/// It now speaks a messages + tools conversation (not just a single prompt), so the same
/// provider powers both the one-shot "Chat" mode and the tool-using "Agent" mode of the AI node.
///
/// The OpenAI-compatible provider covers OpenAI plus most self-hosted/in-house servers
/// (Ollama, vLLM, LM Studio, llama.cpp, LocalAI, Together, Groq, OpenRouter...), so an in-house
/// model is usually just a custom Base URL + model name on that provider.
/// </summary>
public interface IChatModelProvider
{
    /// <summary>Display name shown in the node's Provider dropdown.</summary>
    string Name { get; }

    /// <summary>Accepted values for the Provider parameter (case-insensitive).</summary>
    IReadOnlyCollection<string> Keys { get; }

    /// <summary>Base URL used when the node leaves the field blank.</summary>
    string DefaultBaseUrl { get; }

    /// <summary>Build the HTTP request for one model call (messages + optional tools).</summary>
    HttpRequestMessage BuildRequest(string baseUrl, string? apiKey, ChatCompletionRequest request);

    /// <summary>Extract the assistant text and any tool calls from the provider's JSON response.</summary>
    ChatCompletionResult ParseResult(JsonElement response);
}

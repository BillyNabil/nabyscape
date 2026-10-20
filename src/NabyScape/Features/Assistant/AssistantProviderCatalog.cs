using System;
using System.Collections.Generic;

namespace NabyScape;

internal static class AssistantProviderCatalog
{
    internal static IReadOnlyList<string> Presets { get; } = Array.AsReadOnly(new[]
    {
        "OpenAI", "Anthropic", "Google Gemini", "Ollama", "LM Studio", "xAI", "Hermes Agent",
        "Custom Chat Completions", "Custom Responses", "Custom Anthropic", "Custom Gemini"
    });

    // Model and modality support are explicit user choices, never inferred from a brand.
    internal static AssistantProvider Create(string name)
    {
        var (protocol, url) = name switch
        {
            "OpenAI" => ("Responses", "https://api.openai.com/v1/"),
            "Anthropic" => ("Anthropic", "https://api.anthropic.com/v1/"),
            "Google Gemini" => ("Gemini", "https://generativelanguage.googleapis.com/v1beta/"),
            "Ollama" => ("ChatCompletions", "http://localhost:11434/v1/"),
            "LM Studio" => ("ChatCompletions", "http://localhost:1234/v1/"),
            "xAI" => ("ChatCompletions", "https://api.x.ai/v1/"),
            "Hermes Agent" => ("ChatCompletions", "http://127.0.0.1:8642/v1/"),
            "Custom Chat Completions" => ("ChatCompletions", ""),
            "Custom Responses" => ("Responses", ""),
            "Custom Anthropic" => ("Anthropic", ""),
            "Custom Gemini" => ("Gemini", ""),
            _ => throw new ArgumentException("Unknown provider preset.", nameof(name))
        };
        return new AssistantProvider { Name = name, Protocol = protocol, BaseUrl = url, Model = name=="Hermes Agent"?"hermes-agent":"", Backend=name=="Hermes Agent"?"Hermes":"", Vision = false, Tools = false };
    }
}

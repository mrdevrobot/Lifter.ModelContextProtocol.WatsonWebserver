using System.ComponentModel;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.WatsonWebserver.Tests;

[McpServerToolType]
public sealed class TestTools
{
    [McpServerTool(Name = "echo")]
    [Description("Echoes the message back to the caller.")]
    public static string Echo(string message) => $"echo: {message}";

    [McpServerTool(Name = "add")]
    [Description("Adds two numbers.")]
    public static int Add(int left, int right) => left + right;

    [McpServerTool(Name = "caller_header")]
    [Description("Returns the X-Test-Caller header of the HTTP request that carried the call.")]
    public static string CallerHeader() =>
        McpWatsonRequestContext.Current?.Header("X-Test-Caller") ?? "(no watson context)";

    [McpServerTool(Name = "session_id")]
    [Description("Returns the MCP session the call belongs to.")]
    public static string SessionId() => McpWatsonRequestContext.Current?.SessionId ?? "(stateless)";
}

[McpServerPromptType]
public sealed class TestPrompts
{
    [McpServerPrompt(Name = "greeting")]
    [Description("A greeting for the given name.")]
    public static string Greeting(string name) => $"Hello, {name}!";
}

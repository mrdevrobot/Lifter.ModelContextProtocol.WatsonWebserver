using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.WatsonWebserver;
using WatsonWebserver;
using WatsonWebserver.Core;
using HttpMethod = WatsonWebserver.Core.HttpMethod;

var serverOptions = new McpServerOptions
{
    ServerInfo = new Implementation { Name = "watson-sample", Version = "1.0.0" },
    ServerInstructions = "A sample MCP server hosted on WatsonWebserver.",
};

serverOptions.WithTools<SampleTools>().WithPrompts<SamplePrompts>();

var webserver = new Webserver(new WebserverSettings("127.0.0.1", 9000), DefaultRouteAsync);

// Any other Watson route keeps working: the MCP endpoint is just three more routes.
webserver.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/health",
    context => context.Response.Send("ok", context.Token));

await using var mcp = webserver.MapMcp(serverOptions, options => options.Path = "/mcp");

webserver.Start();

Console.WriteLine("MCP endpoint: http://127.0.0.1:9000/mcp");
Console.WriteLine("Connect with: claude mcp add --transport http watson-sample http://127.0.0.1:9000/mcp");
Console.WriteLine("Press Ctrl+C to stop.");

var stopped = new TaskCompletionSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    stopped.TrySetResult();
};

await stopped.Task;
webserver.Stop();

static Task DefaultRouteAsync(HttpContextBase context)
{
    context.Response.StatusCode = 404;
    return context.Response.Send(context.Token);
}

[McpServerToolType]
internal sealed class SampleTools
{
    [McpServerTool(Name = "echo")]
    [Description("Echoes the message back to the caller.")]
    public static string Echo(string message) => $"echo: {message}";

    [McpServerTool(Name = "caller")]
    [Description("Describes the HTTP request that carried this call.")]
    public static string Caller()
    {
        var context = McpWatsonRequestContext.Current;
        return context is null
            ? "no HTTP request in scope"
            : $"{context.HttpContext.Request.Source.IpAddress} on session {context.SessionId ?? "(stateless)"}";
    }
}

[McpServerPromptType]
internal sealed class SamplePrompts
{
    [McpServerPrompt(Name = "greeting")]
    [Description("A greeting for the given name.")]
    public static string Greeting(string name) => $"Hello, {name}!";
}

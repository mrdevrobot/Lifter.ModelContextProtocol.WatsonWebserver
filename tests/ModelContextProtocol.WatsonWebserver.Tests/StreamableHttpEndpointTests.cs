using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ModelContextProtocol.Protocol;
using Xunit;

namespace ModelContextProtocol.WatsonWebserver.Tests;

public class StreamableHttpEndpointTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Initialize_reports_the_server_info()
    {
        await using var server = McpWatsonTestServer.Start();
        await using var client = await server.ConnectAsync();

        Assert.Equal("watson-test-server", client.ServerInfo.Name);
        Assert.Equal("A server hosted on WatsonWebserver.", client.ServerInstructions);
        Assert.False(string.IsNullOrEmpty(client.SessionId));
    }

    [Fact]
    public async Task Tools_list_returns_the_registered_tools()
    {
        await using var server = McpWatsonTestServer.Start();
        await using var client = await server.ConnectAsync();

        var tools = await client.ListToolsAsync();

        Assert.Contains(tools, tool => tool.Name == "echo");
        Assert.Contains(tools, tool => tool.Name == "add");
    }

    [Fact]
    public async Task Tools_call_returns_the_result()
    {
        await using var server = McpWatsonTestServer.Start();
        await using var client = await server.ConnectAsync();

        var result = await client.CallToolAsync("echo", new Dictionary<string, object?> { ["message"] = "watson" });

        Assert.False(result.IsError ?? false);
        Assert.Equal("echo: watson", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }

    [Fact]
    public async Task Prompts_get_returns_the_prompt()
    {
        await using var server = McpWatsonTestServer.Start();
        await using var client = await server.ConnectAsync();

        var prompts = await client.ListPromptsAsync();
        Assert.Contains(prompts, prompt => prompt.Name == "greeting");

        var result = await client.GetPromptAsync("greeting", new Dictionary<string, object?> { ["name"] = "Watson" });
        var block = Assert.IsType<TextContentBlock>(Assert.Single(result.Messages).Content);
        Assert.Contains("Hello, Watson!", block.Text);
    }

    [Fact]
    public async Task A_session_survives_more_than_one_request()
    {
        await using var server = McpWatsonTestServer.Start();
        await using var client = await server.ConnectAsync();

        var first = await client.CallToolAsync("session_id");
        var second = await client.CallToolAsync("session_id");

        var firstId = Assert.IsType<TextContentBlock>(Assert.Single(first.Content)).Text;
        var secondId = Assert.IsType<TextContentBlock>(Assert.Single(second.Content)).Text;

        Assert.Equal(firstId, secondId);
        Assert.Equal(client.SessionId, firstId);
        Assert.Equal(1, server.ActiveSessionCount);
    }

    [Fact]
    public async Task A_tool_reads_the_watson_request_that_carried_the_call()
    {
        await using var server = McpWatsonTestServer.Start();
        await using var client = await server.ConnectAsync(
            new Dictionary<string, string> { ["X-Test-Caller"] = "integration-test" });

        var result = await client.CallToolAsync("caller_header");

        Assert.Equal("integration-test", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }

    [Fact]
    public async Task Delete_ends_the_session_and_a_later_request_does_not_find_it()
    {
        await using var server = McpWatsonTestServer.Start();
        await using var client = await server.ConnectAsync();
        var sessionId = client.SessionId!;

        using var http = server.CreateHttpClient();

        using var delete = new HttpRequestMessage(HttpMethod.Delete, server.Uri);
        delete.Headers.Add("Mcp-Session-Id", sessionId);
        using var deleted = await http.SendAsync(delete);

        using var again = new HttpRequestMessage(HttpMethod.Delete, server.Uri);
        again.Headers.Add("Mcp-Session-Id", sessionId);
        using var notFound = await http.SendAsync(again);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        Assert.Equal(0, server.ActiveSessionCount);
    }

    [Fact]
    public async Task Delete_without_a_session_id_is_a_bad_request()
    {
        await using var server = McpWatsonTestServer.Start();
        using var http = server.CreateHttpClient();

        using var response = await http.DeleteAsync(server.Uri);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Stateless_mode_serves_calls_without_a_session()
    {
        await using var server = McpWatsonTestServer.Start(options => options.Stateless = true);
        await using var client = await server.ConnectAsync();

        var result = await client.CallToolAsync("add", new Dictionary<string, object?> { ["left"] = 2, ["right"] = 3 });

        Assert.Equal("5", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        Assert.Equal(0, server.ActiveSessionCount);
    }

    [Fact]
    public async Task Stateless_mode_refuses_the_server_to_client_stream()
    {
        await using var server = McpWatsonTestServer.Start(options => options.Stateless = true);
        using var http = server.CreateHttpClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, server.Uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        using var response = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal("POST", Assert.Single(response.Content.Headers.Allow.Count > 0
            ? response.Content.Headers.Allow
            : response.Headers.GetValues("Allow")));
    }

    [Fact]
    public async Task Put_is_not_allowed_on_the_endpoint()
    {
        await using var server = McpWatsonTestServer.Start();
        using var http = server.CreateHttpClient();

        using var response = await http.PutAsync(server.Uri, new StringContent("{}"));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task A_post_that_does_not_accept_both_media_types_is_not_acceptable()
    {
        await using var server = McpWatsonTestServer.Start();
        using var http = server.CreateHttpClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, server.Uri)
        {
            Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotAcceptable, response.StatusCode);
    }

    [Fact]
    public async Task A_post_that_is_not_json_is_a_bad_request()
    {
        await using var server = McpWatsonTestServer.Start();
        using var http = server.CreateHttpClient();

        using var response = await PostAsync(http, server.Uri, "this is not json", sessionId: null);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"error\"", body);
    }

    [Fact]
    public async Task A_non_initialize_post_without_a_session_is_a_bad_request()
    {
        await using var server = McpWatsonTestServer.Start();
        using var http = server.CreateHttpClient();

        using var response = await PostAsync(http, server.Uri,
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}", sessionId: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_session_is_not_found()
    {
        await using var server = McpWatsonTestServer.Start();
        using var http = server.CreateHttpClient();

        using var response = await PostAsync(http, server.Uri,
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}", sessionId: "no-such-session");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("-32001", body);
    }

    [Fact]
    public async Task An_unsupported_protocol_version_header_is_rejected_with_the_supported_list()
    {
        await using var server = McpWatsonTestServer.Start();
        using var http = server.CreateHttpClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, server.Uri)
        {
            Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Add("MCP-Protocol-Version", "1999-01-01");

        using var response = await http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("2025-11-25", body);
    }

    [Fact]
    public async Task The_server_to_client_stream_delivers_unsolicited_notifications()
    {
        await using var server = McpWatsonTestServer.Start();
        await using var client = await server.ConnectAsync(standaloneGetStream: true);

        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var registration = client.RegisterNotificationHandler(
            NotificationMethods.ToolListChangedNotification,
            (_, _) =>
            {
                received.TrySetResult();
                return default;
            });

        await client.ListToolsAsync();

        // The standalone GET stream is opened in the background, so the change is raised again
        // until the notification comes back rather than once into a stream that may not be up yet.
        using var deadline = new CancellationTokenSource(Timeout);
        for (var attempt = 0; !received.Task.IsCompleted && !deadline.IsCancellationRequested; attempt++)
        {
            server.ServerOptions.ToolCollection!.Add(Server.McpServerTool.Create(
                () => "late",
                new Server.McpServerToolCreateOptions { Name = $"late_tool_{attempt}" }));

            await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromMilliseconds(250)));
        }

        await received.Task.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Ending_a_session_closes_a_stream_that_is_still_open()
    {
        await using var server = McpWatsonTestServer.Start();
        await using var client = await server.ConnectAsync();
        var sessionId = client.SessionId!;

        using var http = server.CreateHttpClient();

        using var get = new HttpRequestMessage(HttpMethod.Get, server.Uri);
        get.Headers.Add("Mcp-Session-Id", sessionId);
        get.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var opened = await http.SendAsync(get, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);

        await using var stream = await opened.Content.ReadAsStreamAsync();
        var buffer = new byte[64];

        // The endpoint writes a comment as soon as the stream is up, so this returns once it is.
        Assert.True(await stream.ReadAsync(buffer).AsTask().WaitAsync(Timeout) > 0);

        using var delete = new HttpRequestMessage(HttpMethod.Delete, server.Uri);
        delete.Headers.Add("Mcp-Session-Id", sessionId);
        using var deleted = await http.SendAsync(delete);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        // The stream ends because the session was closed, not because the socket broke.
        var read = await ReadToEndAsync(stream, buffer).WaitAsync(Timeout);

        Assert.Equal(0, read);
        Assert.Equal(0, server.ActiveSessionCount);
        Assert.Empty(server.CapturedFailures());
    }

    private static async Task<int> ReadToEndAsync(Stream stream, byte[] buffer)
    {
        int read;
        do
        {
            read = await stream.ReadAsync(buffer);
        }
        while (read > 0);

        return read;
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient http, Uri uri, string body, string? sessionId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        if (sessionId is not null)
        {
            request.Headers.Add("Mcp-Session-Id", sessionId);
        }

        return http.SendAsync(request);
    }
}

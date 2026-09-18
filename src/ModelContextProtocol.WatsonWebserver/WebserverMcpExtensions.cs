using global::WatsonWebserver.Core;
using global::WatsonWebserver.Core.Routing;
using ModelContextProtocol.Server;
using HttpMethod = global::WatsonWebserver.Core.HttpMethod;

namespace ModelContextProtocol.WatsonWebserver;

/// <summary>Mounts an MCP server on a Watson route.</summary>
public static class WebserverMcpExtensions
{
    /// <summary>
    /// Registers the Streamable HTTP endpoint of an MCP server on <paramref name="server"/>.
    /// </summary>
    /// <param name="server">The Watson server the routes are added to.</param>
    /// <param name="serverOptions">The MCP server every session runs with.</param>
    /// <param name="configure">Configures how the endpoint is mounted.</param>
    /// <returns>The endpoint; dispose it to end every live session.</returns>
    public static McpWatsonEndpoint MapMcp(
        this WebserverBase server,
        McpServerOptions serverOptions,
        Action<McpWatsonOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(serverOptions);

        var options = new McpWatsonOptions();
        configure?.Invoke(options);

        return Mount(server, serverOptions, options);
    }

    /// <summary>
    /// Registers the Streamable HTTP endpoint of an MCP server whose tools, prompts and resources
    /// resolve their dependencies from <paramref name="services"/>.
    /// </summary>
    /// <param name="server">The Watson server the routes are added to.</param>
    /// <param name="serverOptions">The MCP server every session runs with.</param>
    /// <param name="services">The service provider handed to every session's server.</param>
    /// <param name="configure">Configures how the endpoint is mounted.</param>
    /// <returns>The endpoint; dispose it to end every live session.</returns>
    public static McpWatsonEndpoint MapMcp(
        this WebserverBase server,
        McpServerOptions serverOptions,
        IServiceProvider services,
        Action<McpWatsonOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(serverOptions);
        ArgumentNullException.ThrowIfNull(services);

        var options = new McpWatsonOptions { Services = services };
        configure?.Invoke(options);
        options.Services ??= services;

        return Mount(server, serverOptions, options);
    }

    private static McpWatsonEndpoint Mount(WebserverBase server, McpServerOptions serverOptions, McpWatsonOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Path))
        {
            throw new ArgumentException("The MCP endpoint needs a route path.", nameof(options));
        }

        var handler = new McpWatsonHandler(serverOptions, options);
        var group = options.RequireAuthentication
            ? server.Routes.PostAuthentication
            : server.Routes.PreAuthentication;

        group.Static.Add(HttpMethod.POST, options.Path, handler.HandlePostAsync);
        group.Static.Add(HttpMethod.GET, options.Path, handler.HandleGetAsync);
        group.Static.Add(HttpMethod.DELETE, options.Path, handler.HandleDeleteAsync);
        group.Static.Add(HttpMethod.PUT, options.Path, handler.HandleUnsupportedMethodAsync);
        group.Static.Add(HttpMethod.PATCH, options.Path, handler.HandleUnsupportedMethodAsync);
        group.Static.Add(HttpMethod.HEAD, options.Path, handler.HandleUnsupportedMethodAsync);

        return new McpWatsonEndpoint(options.Path, handler);
    }
}

# ModelContextProtocol.WatsonWebserver

**Streamable HTTP transport for the official [Model Context Protocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) on [WatsonWebserver](https://github.com/jchristn/WatsonWebserver).**
Host an MCP server on any Watson route, in any process, without ASP.NET Core.

[![NuGet](https://img.shields.io/nuget/v/ModelContextProtocol.WatsonWebserver?label=nuget&color=red)](https://www.nuget.org/packages/ModelContextProtocol.WatsonWebserver)
[![NuGet Downloads](https://img.shields.io/nuget/dt/ModelContextProtocol.WatsonWebserver?label=downloads)](https://www.nuget.org/packages/ModelContextProtocol.WatsonWebserver)
![License](https://img.shields.io/badge/license-MIT-blue)
![Platform](https://img.shields.io/badge/platform-.NET%208%20%7C%209%20%7C%2010-purple)

> Work in progress. The package is being built; the API below is the target.

## Why

The official SDK ships two ways to host a server: standard input/output, and `ModelContextProtocol.AspNetCore`.
Plenty of .NET processes already serve HTTP with WatsonWebserver and have no ASP.NET Core in them:
desktop applications, embedded devices, point-of-sale terminals, services that must stay small.
This package lets those processes speak MCP over Streamable HTTP with the same `McpServer`, the same
tool and prompt attributes, and the same client compatibility as the ASP.NET Core host.

## Install

```bash
dotnet add package ModelContextProtocol.WatsonWebserver
```

## Use

```csharp
using ModelContextProtocol.Server;
using ModelContextProtocol.WatsonWebserver;
using WatsonWebserver;

var options = new McpServerOptions
{
    ServerInfo = new() { Name = "my-server", Version = "1.0.0" },
};

var server = new Webserver(new WebserverSettings("127.0.0.1", 9000), DefaultRoute);
server.MapMcp(options, mcp =>
{
    mcp.Path = "/mcp";
    mcp.WithTools<MyTools>();
});
server.Start();
```

A client connects to `http://127.0.0.1:9000/mcp` with the SDK's `HttpClientTransport`, Claude Code
(`claude mcp add --transport http my-server http://127.0.0.1:9000/mcp`), or any other MCP client.

## What it does

- `POST /mcp` — JSON-RPC over HTTP, answering with `application/json` or `text/event-stream` as the
  request allows, exactly as the specification describes for Streamable HTTP.
- `GET /mcp` — the server-to-client event stream for a session.
- `DELETE /mcp` — ends a session.
- Sessions by `Mcp-Session-Id`, with an idle timeout; or fully stateless, one server per request,
  for hosts that scale horizontally or restart often.
- `MCP-Protocol-Version` negotiation and the `Origin` checks the specification asks for.
- Authentication is yours: Watson's own authentication route runs before the endpoint, and the
  endpoint hands the request's `HttpContextBase` to your tools through `IHttpContextAccessor`-style
  access, so a tool can read who is calling.

## What it does not do

- It is not a web framework and it does not do OAuth. Put your bearer validation on the Watson
  authentication route, as you would for any other route.
- It does not replace `ModelContextProtocol.AspNetCore` where you already run ASP.NET Core.

## Compatibility

| Package | Version |
|---|---|
| `ModelContextProtocol.Core` | 2.2.x |
| `Watson` | 7.0.x |
| .NET | 8, 9, 10 |

## License

MIT. See [LICENSE](LICENSE).

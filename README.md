# 🔌 ModelContextProtocol.WatsonWebserver
### Streamable HTTP transport for the official MCP C# SDK, hosted on WatsonWebserver

[![NuGet](https://img.shields.io/nuget/v/ModelContextProtocol.WatsonWebserver?label=nuget&color=red)](https://www.nuget.org/packages/ModelContextProtocol.WatsonWebserver)
[![NuGet Downloads](https://img.shields.io/nuget/dt/ModelContextProtocol.WatsonWebserver?label=downloads)](https://www.nuget.org/packages/ModelContextProtocol.WatsonWebserver)
![License](https://img.shields.io/badge/license-MIT-blue)
![Platform](https://img.shields.io/badge/platform-.NET%208%20%7C%209%20%7C%2010-purple)
![AOT](https://img.shields.io/badge/AOT-compatible-brightgreen)

Host a [Model Context Protocol](https://modelcontextprotocol.io) server on any
[WatsonWebserver](https://github.com/jchristn/WatsonWebserver) route, in any process, **without ASP.NET Core**.

```csharp
await using var mcp = webserver.MapMcp(serverOptions);
```

That is the whole integration. Everything else — the tools, the prompts, the protocol — is the
official [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk).

---

## 🚀 Why

The official SDK ships two ways to host a server: standard input/output, and
`ModelContextProtocol.AspNetCore`. Plenty of .NET processes already serve HTTP with WatsonWebserver
and have no ASP.NET Core in them: desktop applications, embedded devices, point-of-sale terminals,
background services that must stay small, single-file AOT binaries.

This package is the glue between Watson and the SDK's own
`ModelContextProtocol.Server.StreamableHttpServerTransport` — the very object
`ModelContextProtocol.AspNetCore` drives. It is not a second implementation of the protocol: the
sessions, the JSON-RPC framing, the SSE streams and the tool invocation are all the SDK's, so a
client cannot tell the difference between a server hosted here and one hosted on Kestrel.

- **No ASP.NET Core.** One dependency on `Watson`, one on `ModelContextProtocol.Core`.
- **AOT-compatible.** No reflection-based JSON: the SDK's `McpJsonUtilities` contexts do the work.
- **Mounts on an existing server.** Your other Watson routes keep working, on the same listener.
- **Authentication stays yours.** The endpoint sits behind Watson's own authentication route.

---

## 📦 Install

```bash
dotnet add package ModelContextProtocol.WatsonWebserver
```

---

## ⚡ Use

```csharp
using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.WatsonWebserver;
using WatsonWebserver;
using WatsonWebserver.Core;

var serverOptions = new McpServerOptions
{
    ServerInfo = new Implementation { Name = "watson-sample", Version = "1.0.0" },
};

serverOptions.WithTools<SampleTools>();

var webserver = new Webserver(new WebserverSettings("127.0.0.1", 9000), DefaultRouteAsync);
await using var mcp = webserver.MapMcp(serverOptions, options => options.Path = "/mcp");
webserver.Start();

[McpServerToolType]
internal sealed class SampleTools
{
    [McpServerTool(Name = "echo"), Description("Echoes the message back.")]
    public static string Echo(string message) => $"echo: {message}";
}
```

A runnable version of this is in [`samples/McpWatsonSample`](samples/McpWatsonSample).

---

## 🧰 Registering tools and prompts

The SDK's `WithTools<T>()` builder lives in the hosting package, which exists only for
dependency-injection hosts and drags ASP.NET Core's generic host in with it. This package offers the
same shape as extension methods on `McpServerOptions`, built on the SDK's own
`McpServerTool.Create` / `McpServerPrompt.Create`:

```csharp
serverOptions
    .WithTools<SampleTools>()                     // every [McpServerTool] method on the type
    .WithTools(typeof(OtherTools), services)      // instance methods get their target from the container
    .WithTool(([Description("Adds.")] int a, int b) => a + b)
    .WithPrompts<SamplePrompts>();
```

Static methods need nothing. Instance methods get a target per call: from the `IServiceProvider` you
pass (through `ActivatorUtilities`, so constructor injection works) or from the type's public
constructor when you pass none.

To hand the whole server a container — so tools can take services as parameters — use the
`IServiceProvider` overload of `MapMcp`:

```csharp
await using var mcp = webserver.MapMcp(serverOptions, services, options => options.Path = "/mcp");
```

---

## 🗂️ Sessions vs stateless

| | Sessions (default) | `Stateless = true` |
|---|---|---|
| `Mcp-Session-Id` | issued on `initialize`, required afterwards | ignored, never issued |
| Server instance | one per session, alive between requests | one per request |
| `GET` stream | server-to-client notifications | 405 |
| `DELETE` | ends the session | 405 |
| State in a tool | survives across calls | does not |
| Idle sessions | disposed after `IdleTimeout` (default 2h) | nothing to dispose |

Stateless is what you want when the process is restarted often, when several processes sit behind a
load balancer, or when the tools are pure functions. Sessions are what you want when a tool holds
something between calls, or when the server has to push notifications to the client.

```csharp
webserver.MapMcp(serverOptions, options =>
{
    options.Stateless = true;
    options.Path = "/mcp";
});
```

---

## ⚙️ Options

| Option | Default | What it does |
|---|---|---|
| `Path` | `/mcp` | The route the endpoint answers on |
| `Stateless` | `false` | One server per request, no session |
| `IdleTimeout` | 2 hours | How long a quiet session lives |
| `IdleSweepInterval` | 5 seconds | How often idle sessions are swept |
| `AllowedOrigins` | empty (all) | `Origin` values accepted; populate when a browser can reach the listener |
| `SupportedProtocolVersions` | `2024-11-05` … `2025-11-25` | Revisions accepted in `MCP-Protocol-Version` |
| `RequireAuthentication` | `true` | Register after Watson's authentication phase |
| `Services` | `null` | Service provider handed to each session's `McpServer` |
| `LoggerFactory` | `null` | Logger factory for the transport and the server |
| `ConfigureSession` | `null` | Configure a session's server options from the request that opened it |

---

## 🔐 Authentication

This package does not do OAuth, and does not want to. With `RequireAuthentication` left at `true`
the endpoint is registered in Watson's post-authentication routing group, so
`Routes.AuthenticateRequest` runs first and can reject the request before MCP ever sees it:

```csharp
webserver.Routes.AuthenticateRequest = async context =>
{
    if (context.Request.Headers["Authorization"] != $"Bearer {token}")
    {
        context.Response.StatusCode = 401;
        await context.Response.Send(context.Token);
    }
};
```

From inside a tool, the request that carried the call is reachable through
`McpWatsonRequestContext.Current` — the execution context of the HTTP request flows into the
server's handling of the message it carried:

```csharp
[McpServerTool(Name = "whoami"), Description("Says who is calling.")]
public static string WhoAmI() =>
    McpWatsonRequestContext.Current?.Header("X-Tenant") ?? "(anonymous)";
```

It exposes the Watson `HttpContextBase` itself (`HttpContext`) and the session id, and is `null`
outside an HTTP request — in a notification the server raises on its own, for instance.

---

## 🤖 Connecting Claude Code

```bash
claude mcp add --transport http watson-sample http://127.0.0.1:9000/mcp
```

Any MCP client works the same way: the SDK's `HttpClientTransport` with
`HttpTransportMode.StreamableHttp`, the TypeScript SDK, MCP Inspector.

---

## ✅ What it does

- `POST` — JSON-RPC in, `text/event-stream` out, `202 Accepted` for a body that produced no response.
- `GET` — the standalone server-to-client SSE stream of a session.
- `DELETE` — ends a session (`204`), `404` for one that is already gone.
- `Mcp-Session-Id` issued on `initialize` and echoed on every answer; `404` with JSON-RPC `-32001`
  for an unknown one, `400` for a non-`initialize` request that carries none.
- `406` when the client does not accept both `application/json` and `text/event-stream`,
  `400` for a body that is not a JSON-RPC message, `405` (with `Allow`) for any other method.
- `MCP-Protocol-Version` validated against `SupportedProtocolVersions`, answering an unsupported one
  with the supported list so the client can negotiate down.
- `Origin` checked against `AllowedOrigins` when you populate it.

## 🚫 What it does not do

- **Resumability.** `Last-Event-ID` replay needs an event store; the SDK has the hook
  (`StreamableHttpServerTransport.EventStreamStore`) and this package does not wire it up yet.
- **Session migration** between processes, and the `2026-07-28` per-request-metadata revision.
- **OAuth.** Authorization is a Watson route, see above.
- **Replace `ModelContextProtocol.AspNetCore`** where you already run ASP.NET Core. Use that one.

---

## 📐 Compatibility

| Package | Version |
|---|---|
| `ModelContextProtocol.Core` | 2.2.x |
| `Watson` | 7.0.x |
| .NET | 8, 9, 10 |
| MCP protocol | 2024-11-05, 2025-03-26, 2025-06-18, 2025-11-25 |

The tests drive the endpoint with the official `ModelContextProtocol` client over a real Watson
listener on a loopback port, so what passes there works against a real MCP client.

---

## 🤝 Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). In short: Conventional Commits, zero warnings
(`TreatWarningsAsErrors` is on), and a test in `tests/` for anything that changes behaviour.

## 📄 License

MIT. See [LICENSE](LICENSE).

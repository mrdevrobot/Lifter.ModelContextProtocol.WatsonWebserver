# ModelContextProtocol.WatsonWebserver — Claude Code guide

Single NuGet package: the Streamable HTTP transport of the official MCP C# SDK, hosted on
WatsonWebserver instead of ASP.NET Core. `src/` is the package, `tests/` drives it through the
official `ModelContextProtocol` client over a real Watson listener.

- Code comments in English only, concise, no `TODO`: an open item goes in an issue.
- Commits follow Conventional Commits; versionize builds `CHANGELOG.md` from them.
- Never add ASP.NET Core packages. The point of this package is not needing them.
- `TreatWarningsAsErrors` is on and the package is AOT-compatible: no reflection-based JSON, only
  the SDK's `McpJsonUtilities` contexts.
- The MCP specification is the authority on transport behaviour (Streamable HTTP, protocol
  version header, session ids, SSE resumability). The SDK's ASP.NET Core handler is the reference
  implementation to mirror, not to copy.

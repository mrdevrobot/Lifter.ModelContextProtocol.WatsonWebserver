using ModelContextProtocol.WatsonWebserver;
using Xunit;

namespace ModelContextProtocol.WatsonWebserver.Tests;

public class OptionsTests
{
    [Fact]
    public void Defaults_mount_on_mcp_and_keep_sessions()
    {
        var options = new McpWatsonOptions();

        Assert.Equal("/mcp", options.Path);
        Assert.False(options.Stateless);
    }
}

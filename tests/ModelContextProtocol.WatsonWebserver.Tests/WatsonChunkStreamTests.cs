using global::WatsonWebserver.Core;
using Xunit;

namespace ModelContextProtocol.WatsonWebserver.Tests;

public class WatsonChunkStreamTests
{
    [Fact]
    public async Task Writing_after_dispose_throws_and_completing_does_not()
    {
        var started = false;
        using var context = new HttpContextBase();
        var stream = new WatsonChunkStream(context, () => started = true);

        await stream.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await stream.WriteAsync(new byte[] { 1, 2, 3 }));

        await stream.CompleteAsync();

        Assert.False(started);
    }
}

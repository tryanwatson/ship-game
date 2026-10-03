using ShipGame.Net;

namespace ShipGame.Net.Tests;

public class ServerAddressTests
{
    [Theory]
    [InlineData("example.com", "example.com", Protocol.DefaultPort)]
    [InlineData("  203.0.113.5  ", "203.0.113.5", Protocol.DefaultPort)]
    [InlineData("203.0.113.5:9000", "203.0.113.5", 9000)]
    [InlineData("localhost:7778", "localhost", 7778)]
    [InlineData("::1", "::1", Protocol.DefaultPort)]
    [InlineData("[::1]", "::1", Protocol.DefaultPort)]
    [InlineData("[2001:db8::7]:9000", "2001:db8::7", 9000)]
    public void Parses(string text, string host, int port)
    {
        Assert.True(ServerAddress.TryParse(text, out var address));
        Assert.Equal(new ServerAddress(host, port), address);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(":7777")]
    [InlineData("host:")]
    [InlineData("host:abc")]
    [InlineData("host:0")]
    [InlineData("host:70000")]
    [InlineData("my host")]
    [InlineData("[]")]
    [InlineData("[::1")]
    [InlineData("[::1]7777")]
    public void Rejects(string? text) => Assert.False(ServerAddress.TryParse(text, out _));

    [Theory]
    [InlineData("example.com")]
    [InlineData("example.com:9000")]
    [InlineData("[::1]:9000")]
    [InlineData("::1")]
    public void RoundTrips_ThroughToString(string text)
    {
        Assert.True(ServerAddress.TryParse(text, out var address));
        Assert.True(ServerAddress.TryParse(address.ToString(), out var again));
        Assert.Equal(address, again);
    }
}

using ShipGame.Net;
using ShipGame.Server;

namespace ShipGame.Net.Tests;

public class ServerOptionsTests
{
    private static ServerOptions Parse(string[] args, params (string Name, string Value)[] environment) =>
        ServerOptions.Parse(args, name => environment.FirstOrDefault(e => e.Name == name).Value);

    [Fact]
    public void Defaults_WhenNothingIsSet()
    {
        var options = Parse([]);
        Assert.Equal(Protocol.DefaultPort, options.Port);
        Assert.True(options.FriendlyFire);
    }

    [Fact]
    public void ReadsArguments()
    {
        var options = Parse(["--port", "9000", "--no-friendly-fire"]);
        Assert.Equal(9000, options.Port);
        Assert.False(options.FriendlyFire);
    }

    [Theory]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("Off", false)]
    [InlineData("true", true)]
    [InlineData("1", true)]
    public void ReadsEnvironment(string friendlyFire, bool expected)
    {
        var options = Parse([], ("SHIPGAME_PORT", "8123"), ("SHIPGAME_FRIENDLY_FIRE", friendlyFire));
        Assert.Equal(8123, options.Port);
        Assert.Equal(expected, options.FriendlyFire);
    }

    [Fact]
    public void ArgumentsOverrideEnvironment()
    {
        var options = Parse(["--port", "9000", "--friendly-fire"], ("SHIPGAME_PORT", "8123"), ("SHIPGAME_FRIENDLY_FIRE", "false"));
        Assert.Equal(9000, options.Port);
        Assert.True(options.FriendlyFire);
    }

    [Fact]
    public void EmptyEnvironmentValues_AreIgnored()
    {
        var options = Parse([], ("SHIPGAME_PORT", ""), ("SHIPGAME_FRIENDLY_FIRE", ""));
        Assert.Equal(ServerOptions.Default, options);
    }

    [Fact]
    public void Password_FromEnvironmentOrArgument_BlankMeansNone()
    {
        Assert.Null(Parse([]).Password);
        Assert.Equal("hunter2", Parse([], ("SHIPGAME_PASSWORD", "hunter2")).Password);
        Assert.Equal("arg", Parse(["--password", "arg"], ("SHIPGAME_PASSWORD", "env")).Password);
        Assert.Null(Parse(["--password", ""], ("SHIPGAME_PASSWORD", "env")).Password);
    }

    [Theory]
    [InlineData("--password")]
    [InlineData("--port")]
    [InlineData("--port", "abc")]
    [InlineData("--port", "70000")]
    [InlineData("--prot", "7777")]
    public void BadArguments_Throw(params string[] args) =>
        Assert.Throws<FormatException>(() => Parse(args));

    [Fact]
    public void BadEnvironment_Throws()
    {
        Assert.Throws<FormatException>(() => Parse([], ("SHIPGAME_PORT", "seven")));
        Assert.Throws<FormatException>(() => Parse([], ("SHIPGAME_FRIENDLY_FIRE", "maybe")));
    }
}

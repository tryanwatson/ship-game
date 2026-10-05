namespace ShipGame.Launcher.Tests;

public sealed class ReleaseFeedTests
{
    private const string Release = """
        {
          "tag_name": "v0.5.0",
          "assets": [
            { "name": "ShipGame-0.5.0-linux-x64.zip", "size": 38, "browser_download_url": "https://example.com/linux.zip" },
            { "name": "ShipGame-0.5.0-osx-arm64.zip", "size": 36, "browser_download_url": "https://example.com/osx.zip" },
            { "name": "ShipGame-Launcher-osx-arm64.zip", "size": 20, "browser_download_url": "https://example.com/launcher.zip" }
          ]
        }
        """;

    [Fact]
    public void PicksThisPlatformsGameZip()
    {
        var release = ReleaseFeed.Parse(Release, "osx-arm64");

        Assert.Equal(new GameRelease("0.5.0", new Uri("https://example.com/osx.zip"), 36), release);
    }

    [Fact]
    public void NoBuildForThePlatformIsNull() => Assert.Null(ReleaseFeed.Parse(Release, "win-x64"));

    [Fact]
    public void RejectsTagsThatCantBeDirectoryNames() =>
        Assert.Null(ReleaseFeed.Parse(Release.Replace("v0.5.0", "v../../evil"), "osx-arm64"));
}

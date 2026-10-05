using System.IO.Compression;

namespace ShipGame.Launcher.Tests;

public sealed class GameInstallTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"shipgame-launcher-{Guid.NewGuid():N}");
    private GameInstall Install => new(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void NothingInstalledAtFirst() => Assert.Null(Install.CurrentVersion);

    [Fact]
    public void InstallingMakesTheVersionCurrent()
    {
        Install.Install("0.5.0", MakeReleaseZip("game"));

        Assert.Equal("0.5.0", Install.CurrentVersion);
    }

    [Fact]
    public void InstallingANewVersionRemovesTheOldOne()
    {
        Install.Install("0.4.0", MakeReleaseZip("old"));
        Install.Install("0.5.0", MakeReleaseZip("new"));

        Assert.Equal("0.5.0", Install.CurrentVersion);
        Assert.Equal(["0.5.0"], Directory.GetDirectories(Path.Combine(_root, "versions")).Select(Path.GetFileName));
    }

    [Fact]
    public void AZipWithoutTheGameIsRejectedAndTheOldVersionKept()
    {
        Install.Install("0.4.0", MakeReleaseZip("old"));
        var bogus = Path.Combine(_root, "bogus.zip");
        using (var zip = ZipFile.Open(bogus, ZipArchiveMode.Create))
            zip.CreateEntry("readme.txt");

        Assert.Throws<InvalidDataException>(() => Install.Install("0.5.0", bogus));
        Assert.Equal("0.4.0", Install.CurrentVersion);
    }

    [Fact]
    public void AVersionWhoseFilesAreGoneIsNotInstalled()
    {
        Install.Install("0.5.0", MakeReleaseZip("game"));
        Directory.Delete(Path.Combine(_root, "versions", "0.5.0"), recursive: true);

        Assert.Null(Install.CurrentVersion);
    }

    [Theory]
    [InlineData("0.5.0", true)]
    [InlineData("1.0.0-beta.2", true)]
    [InlineData("..", false)]
    [InlineData("0.5.0/../x", false)]
    [InlineData("", false)]
    public void VersionsMustBeSafeDirectoryNames(string version, bool safe) =>
        Assert.Equal(safe, GameInstall.IsSafeVersion(version));

    /// <summary>A zip laid out like this platform's release zip.</summary>
    private string MakeReleaseZip(string contents)
    {
        var game = OperatingSystem.IsMacOS() ? "ShipGame.app/Contents/MacOS/ShipGame.Client"
            : OperatingSystem.IsWindows() ? "ShipGame/ShipGame.Client.exe"
            : "ShipGame/ShipGame.Client";
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, $"{Guid.NewGuid():N}.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using var writer = new StreamWriter(zip.CreateEntry(game).Open());
        writer.Write(contents);
        return path;
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;

namespace ShipGame.Launcher;

/// <summary>
/// The game builds the launcher has installed, under <c>root/versions/&lt;version&gt;</c>, each unpacked exactly as its
/// release zip. <c>root/current</c> names the one to run; it's only written once a build is fully unpacked.
/// </summary>
public sealed class GameInstall
{
    private readonly string _root;

    public GameInstall(string root) => _root = root;

    /// <summary>
    /// %LocalAppData%\ShipGame, ~/Library/Application Support/ShipGame or ~/.local/share/ShipGame, unless
    /// SHIPGAME_INSTALL_DIR says otherwise (for trying the launcher out without touching the real install).
    /// </summary>
    public static string DefaultRoot =>
        Environment.GetEnvironmentVariable("SHIPGAME_INSTALL_DIR") is { Length: > 0 } dir ? dir
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShipGame");

    /// <summary>This machine's runtime identifier, as the release zips are named: win-x64, osx-arm64, linux-x64.</summary>
    public static string Rid
    {
        get
        {
            var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
            return $"{os}-{RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}";
        }
    }

    private string VersionsDir => Path.Combine(_root, "versions");
    private string CurrentFile => Path.Combine(_root, "current");
    public string DownloadPath => Path.Combine(_root, "download.zip");

    /// <summary>The installed version, or null if there isn't one or its files have gone.</summary>
    public string? CurrentVersion
    {
        get
        {
            try
            {
                var version = File.ReadAllText(CurrentFile).Trim();
                return IsSafeVersion(version) && Exists(GamePath(Path.Combine(VersionsDir, version))) ? version : null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    /// <summary>Versions become directory names, so they may only hold the characters a release tag would.</summary>
    public static bool IsSafeVersion(string version) =>
        version.Length is > 0 and <= 64 && version.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '+')
        && !version.StartsWith('.');

    /// <summary>Unpacks a release zip, makes it the current version, and removes the versions it replaces.</summary>
    public void Install(string version, string zipPath)
    {
        if (!IsSafeVersion(version))
            throw new ArgumentException($"Bad version '{version}'.", nameof(version));

        var target = Path.Combine(VersionsDir, version);
        var staging = target + ".partial";
        DeleteDirectory(staging);
        Directory.CreateDirectory(staging);
        Extract(zipPath, staging);
        if (!Exists(GamePath(staging)))
            throw new InvalidDataException("The download does not contain the game.");

        DeleteDirectory(target); // a broken earlier install of the same version
        Directory.Move(staging, target);
        var next = CurrentFile + ".new";
        File.WriteAllText(next, version);
        File.Move(next, CurrentFile, overwrite: true);
        RemoveVersionsExcept(version);
    }

    /// <summary>Starts the installed game, passing <paramref name="args"/> through to it.</summary>
    public void Launch(string version, IEnumerable<string> args)
    {
        var game = GamePath(Path.Combine(VersionsDir, version));
        ProcessStartInfo start;
        if (OperatingSystem.IsMacOS())
        {
            start = new ProcessStartInfo("open") { ArgumentList = { "-n", game, "--args" } };
        }
        else
        {
            start = new ProcessStartInfo(game) { WorkingDirectory = Path.GetDirectoryName(game)! };
        }
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        using var _ = Process.Start(start);
    }

    /// <summary>Where a release zip puts the thing to run (see the Release workflow's packaging steps).</summary>
    private static string GamePath(string versionDir) =>
        OperatingSystem.IsMacOS() ? Path.Combine(versionDir, "ShipGame.app")
        : OperatingSystem.IsWindows() ? Path.Combine(versionDir, "ShipGame", "ShipGame.Client.exe")
        : Path.Combine(versionDir, "ShipGame", "ShipGame.Client");

    private static void Extract(string zipPath, string destination)
    {
        if (OperatingSystem.IsMacOS())
        {
            // The macOS zip keeps the app's code signature in AppleDouble entries that only ditto (and Finder) restore.
            using var ditto = Process.Start(new ProcessStartInfo("/usr/bin/ditto") { ArgumentList = { "-x", "-k", zipPath, destination } })!;
            ditto.WaitForExit();
            if (ditto.ExitCode != 0)
                throw new InvalidDataException($"Could not unpack the download (ditto exited with {ditto.ExitCode}).");
        }
        else
        {
            // Keeps Unix permissions, so the Linux build stays executable.
            ZipFile.ExtractToDirectory(zipPath, destination);
        }
    }

    /// <summary>Best effort: on Windows an old build that's still running can't be deleted until next time.</summary>
    private void RemoveVersionsExcept(string keep)
    {
        foreach (var dir in Directory.EnumerateDirectories(VersionsDir))
        {
            if (Path.GetFileName(dir) != keep)
                TryDeleteDirectory(dir);
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            DeleteDirectory(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
}

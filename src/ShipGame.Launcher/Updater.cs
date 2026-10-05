using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ShipGame.Launcher;

public enum LauncherPhase { Working, Ready, Failed }

/// <param name="Version">The installed version to launch, once <see cref="LauncherPhase.Ready"/>.</param>
/// <param name="Note">Why an older installed version is being launched instead of the latest.</param>
public sealed record LauncherState(LauncherPhase Phase, string Message, float? Progress = null, string? Version = null,
    string? Note = null);

/// <summary>
/// Brings the installed game up to the latest release, falling back to the installed one when GitHub can't be reached.
/// Runs on a background task; the window polls <see cref="State"/>.
/// </summary>
public sealed class Updater
{
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

    private readonly GameInstall _install;
    private readonly HttpClient _http;
    private readonly string _rid;

    public Updater(GameInstall install, HttpClient http, string rid)
    {
        _install = install;
        _http = http;
        _rid = rid;
    }

    public LauncherState State { get; private set; } = new(LauncherPhase.Working, "Checking for updates");

    public async Task RunAsync(CancellationToken cancellation)
    {
        var installed = _install.CurrentVersion;
        GameRelease? latest;
        try
        {
            latest = await ReleaseFeed.FetchLatestAsync(_http, _rid, cancellation);
        }
        catch (Exception e) when (!cancellation.IsCancellationRequested)
        {
            Settle(installed, $"Could not check for updates: {Describe(e)}");
            return;
        }

        if (latest is null)
        {
            Settle(installed, $"The latest release has no build for {_rid}");
            return;
        }
        if (latest.Version == installed)
        {
            State = Ready(installed);
            return;
        }

        try
        {
            await DownloadAsync(latest, cancellation);
            State = new LauncherState(LauncherPhase.Working, $"Installing ShipGame {latest.Version}");
            await Task.Run(() => _install.Install(latest.Version, _install.DownloadPath), cancellation);
            File.Delete(_install.DownloadPath);
            State = Ready(latest.Version);
        }
        catch (Exception e) when (!cancellation.IsCancellationRequested)
        {
            Settle(installed, $"Could not update to {latest.Version}: {Describe(e)}");
        }
    }

    /// <summary>Starts the game this updater got ready, or reports why it couldn't.</summary>
    public bool Launch(IEnumerable<string> args)
    {
        try
        {
            _install.Launch(State.Version!, args);
            return true;
        }
        catch (Exception e)
        {
            State = new LauncherState(LauncherPhase.Failed, $"Could not start the game: {e.Message}");
            return false;
        }
    }

    private async Task DownloadAsync(GameRelease release, CancellationToken cancellation)
    {
        var label = $"Downloading ShipGame {release.Version}";
        State = new LauncherState(LauncherPhase.Working, label, 0f);

        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        stall.CancelAfter(StallTimeout);
        using var response = await _http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, stall.Token);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(stall.Token);

        Directory.CreateDirectory(Path.GetDirectoryName(_install.DownloadPath)!);
        long received = 0;
        await using (var file = File.Create(_install.DownloadPath))
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await source.ReadAsync(buffer, stall.Token)) > 0)
            {
                stall.CancelAfter(StallTimeout); // only a download that stops moving times out
                await file.WriteAsync(buffer.AsMemory(0, read), cancellation);
                received += read;
                State = new LauncherState(LauncherPhase.Working,
                    $"{label}  {received / 1e6:0.0} / {release.Size / 1e6:0.0} MB", (float)received / release.Size);
            }
        }
        if (received != release.Size)
            throw new IOException("the download was cut short");
    }

    /// <summary>After a failed check or update: play the installed version if there is one.</summary>
    private void Settle(string? installed, string problem) =>
        State = installed is null ? new LauncherState(LauncherPhase.Failed, problem) : Ready(installed) with { Note = problem };

    private static LauncherState Ready(string version) =>
        new(LauncherPhase.Ready, $"Starting ShipGame {version}", Version: version);

    private static string Describe(Exception e) =>
        e is TaskCanceledException or OperationCanceledException ? "timed out" : e.Message;
}

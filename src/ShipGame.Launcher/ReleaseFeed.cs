using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ShipGame.Launcher;

/// <summary>A released game build for this platform: the zip the Release workflow attached to a GitHub release.</summary>
public sealed record GameRelease(string Version, Uri DownloadUrl, long Size);

/// <summary>Finds the latest game release on GitHub.</summary>
public static class ReleaseFeed
{
    private const string LatestUrl = "https://api.github.com/repos/tryanwatson/ship-game/releases/latest";

    public static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan }; // downloads are long; each call sets its own limit
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ShipGame-Launcher", "1.0")); // GitHub requires one
        return http;
    }

    /// <summary>The latest release, or null if it has no build for <paramref name="rid"/>.</summary>
    public static async Task<GameRelease?> FetchLatestAsync(HttpClient http, string rid, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await http.SendAsync(request, timeout.Token);
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(timeout.Token), rid);
    }

    /// <summary>Picks this platform's zip out of a GitHub release (tag <c>v1.2.3</c>, asset <c>ShipGame-1.2.3-rid.zip</c>).</summary>
    internal static GameRelease? Parse(string json, string rid)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var version = tag.StartsWith('v') ? tag[1..] : tag;
        if (version.Length == 0 || !GameInstall.IsSafeVersion(version))
            return null;

        var assetName = $"ShipGame-{version}-{rid}.zip";
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() == assetName)
                return new GameRelease(version, new Uri(asset.GetProperty("browser_download_url").GetString()!),
                    asset.GetProperty("size").GetInt64());
        }
        return null;
    }
}

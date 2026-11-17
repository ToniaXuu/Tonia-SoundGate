using System.Reflection;
using System.Text.Json;

namespace ToniaSoundGate;

/// <summary>
/// 一次更新检查的结果。只描述"有没有新版本"，不负责下载或安装——
/// 后续动作完全交给用户决定，这是"零联网承诺"的实现基础。
/// </summary>
internal sealed record UpdateCheckResult(
    bool Success,
    bool HasUpdate,
    string? LatestVersion,
    string? ReleaseUrl,
    string? Error)
{
    public static UpdateCheckResult UpToDate(string local) => new(true, false, local, null, null);

    public static UpdateCheckResult Available(string latest, string url) => new(true, true, latest, url, null);

    public static UpdateCheckResult Failed(string error) => new(false, false, null, null, error);
}

/// <summary>
/// 更新检测：仅在用户主动点击「检查更新」时调用，程序任何其它路径都不会触网。
/// 数据源为 GitHub Releases latest，超时 8 秒，失败一律静默降级为一条提示。
/// </summary>
internal static class UpdateChecker
{
    private const string ReleasesApi =
        "https://api.github.com/repos/ToniaXuu/Tonia-SoundGate/releases/latest";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        // GitHub API 强制要求 User-Agent，缺失会直接返回 403
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Tonia-SoundGate");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await Http.GetAsync(ReleasesApi, ct);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            var root = doc.RootElement;
            var tag = root.GetProperty("tag_name").GetString();
            var url = root.GetProperty("html_url").GetString();

            if (string.IsNullOrWhiteSpace(tag) || !TryNormalize(tag, out var remote))
                return UpdateCheckResult.Failed("版本号格式无法识别，已跳过更新检查");

            var local = LocalVersion();
            if (remote <= local)
                return UpdateCheckResult.UpToDate(local.ToString(3));

            return UpdateCheckResult.Available(remote.ToString(3), url ?? ReleasesApi.Replace("/releases/latest", "/releases"));
        }
        catch (TaskCanceledException)
        {
            return UpdateCheckResult.Failed("连接超时，请稍后重试");
        }
        catch (HttpRequestException)
        {
            return UpdateCheckResult.Failed("网络不可用，请检查连接后重试");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"UpdateChecker: {ex.GetType().Name} {ex.Message}");
            return UpdateCheckResult.Failed("检查失败，请稍后重试");
        }
    }

    /// <summary>
    /// 当前程序版本。AssemblyVersion 形如 1.0.1.0，统一归一化为三段再比较。
    /// </summary>
    private static Version LocalVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
        return Normalize(v);
    }

    /// <summary>
    /// 接受 "v1.0.1" / "1.0.1" / "1.0.1.0"，统一成三段。
    /// 陷阱：Version("1.0.1") 的 Revision 是 -1，与 Version("1.0.1.0") 直接比较会被当成更旧。
    /// </summary>
    private static bool TryNormalize(string tag, out Version version)
    {
        var text = tag.Trim().TrimStart('v', 'V');
        if (Version.TryParse(text, out var parsed))
        {
            version = Normalize(parsed);
            return true;
        }
        version = new Version(0, 0, 0);
        return false;
    }

    private static Version Normalize(Version v) =>
        new(v.Major, v.Minor, Math.Max(v.Build, 0));
}

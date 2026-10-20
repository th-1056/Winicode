using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Winicode;

public record ReleaseCheckResult(bool UpdateAvailable, string Message, string? ReleaseUrl = null, string? Version = null);

public sealed class ReleaseChecker
{
    private const int MaximumBytes = 1024 * 1024;
    private static readonly HttpClient OfficialClient = new(new HttpClientHandler { AllowAutoRedirect = false })
    { Timeout = TimeSpan.FromSeconds(15) };
    private readonly string repositoryUrl;
    private readonly string currentVersion;
    private readonly HttpClient client;

    public ReleaseChecker(string repositoryUrl, string currentVersion) : this(repositoryUrl, currentVersion, OfficialClient) { }

    // Injection is internal so tests can exercise the complete HTTP path without network access.
    internal ReleaseChecker(string repositoryUrl, string currentVersion, HttpClient client)
    {
        this.repositoryUrl = repositoryUrl;
        this.currentVersion = currentVersion;
        this.client = client;
    }

    public async Task<ReleaseCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryUrl)) return new(false, "尚未配置官方发布仓库。请通过软件附带的帮助文档确认发布来源。");
        if (!TryRepository(repositoryUrl, out var owner, out var repository))
            return new(false, "发布仓库地址无效。仅支持 GitHub 官方 HTTPS 仓库地址。");
        var current = ReleaseVersion.Parse(currentVersion);
        if (current is null) return new(false, "当前软件版本格式无效，无法比较更新。");
        var endpoint = new Uri($"https://api.github.com/repos/{owner}/{repository}/releases?per_page=20");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var token = timeout.Token;
            token.ThrowIfCancellationRequested();
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.UserAgent.ParseAdd("Winicode/" + BuildInfo.Version);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.RequestMessage?.RequestUri is Uri actual && actual != endpoint)
                return new(false, "更新响应地址与官方仓库不符，已停止检查。");
            if (response.StatusCode == HttpStatusCode.NotFound) return new(false, "官方仓库尚无可用发布，或仓库暂时无法访问。");
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                return new(false, "GitHub 暂时限制了检查请求，请稍后重试。");
            if (!response.IsSuccessStatusCode) return new(false, "更新检查失败，请稍后重试。");
            if (response.Content.Headers.ContentLength > MaximumBytes)
                return new(false, "更新响应超过大小限制，已停止检查。");
            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var contents = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0,
                Math.Min(buffer.Length, MaximumBytes + 1 - (int)contents.Length)), token).ConfigureAwait(false)) > 0)
            {
                contents.Write(buffer, 0, read);
                if (contents.Length > MaximumBytes) return new(false, "更新响应超过大小限制，已停止检查。");
            }
            using var document = JsonDocument.Parse(contents.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return new(false, "官方更新响应格式无效，请稍后重试。");

            ReleaseVersion? latest = null;
            string? latestTag = null;
            string? latestUrl = null;
            foreach (var release in document.RootElement.EnumerateArray())
            {
                if (release.ValueKind != JsonValueKind.Object
                    || !release.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False
                    || !release.TryGetProperty("prerelease", out var prerelease)
                    || prerelease.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                    || !release.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String
                    || !release.TryGetProperty("html_url", out var url) || url.ValueKind != JsonValueKind.String) continue;
                var tagText = tag.GetString()!;
                var version = ReleaseVersion.Parse(tagText);
                if (version is null || current.Prerelease.Length == 0 && (version.Prerelease.Length != 0 || prerelease.GetBoolean())) continue;
                if (!TryReleaseUrl(url.GetString(), owner, repository, tagText, out var safeUrl))
                    return new(false, "发布链接与官方仓库不符，已停止检查。");
                if (latest is null || version.CompareTo(latest) > 0)
                {
                    latest = version;
                    latestTag = tagText;
                    latestUrl = safeUrl;
                }
            }
            if (latest is null) return new(false, "官方仓库尚无适用于当前版本的发布。");
            return latest.CompareTo(current) > 0
                ? new(true, "发现新版本，可打开官方发布页查看说明并手动下载。", latestUrl, latestTag)
                : new(false, "当前已是最新版本。", latestUrl, latestTag);
        }
        catch (OperationCanceledException)
        {
            return new(false, cancellationToken.IsCancellationRequested ? "已取消检查更新。" : "更新检查超时，请稍后重试。");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException or InvalidOperationException)
        {
            return new(false, "更新检查失败，请检查网络连接后重试。");
        }
    }

    private static bool TryRepository(string text, out string owner, out string repository)
    {
        owner = repository = "";
        if (!TryHttps(text, "github.com", out var uri)) return false;
        var path = uri.AbsolutePath.TrimEnd('/').Split('/');
        if (path.Length != 3 || path[0] != "") return false;
        owner = path[1];
        repository = path[2];
        return Regex.IsMatch(owner, @"\A[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?\z", RegexOptions.CultureInvariant)
            && !owner.Contains("--", StringComparison.Ordinal)
            && Regex.IsMatch(repository, @"\A[A-Za-z0-9._-]{1,100}\z", RegexOptions.CultureInvariant)
            && repository is not ("." or "..");
    }

    private static bool TryReleaseUrl(string? text, string owner, string repository, string tag, out string? safeUrl)
    {
        safeUrl = null;
        if (!TryHttps(text, "github.com", out var uri, allowEscapedTag: true)) return false;
        var expected = $"/{owner}/{repository}/releases/tag/{Uri.EscapeDataString(tag)}";
        // Owner/repository names are case insensitive; release tags retain their exact identity.
        var prefix = $"/{owner}/{repository}/releases/tag/";
        if (!uri.AbsolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || uri.AbsolutePath[prefix.Length..] != expected[prefix.Length..]
                && uri.AbsolutePath[prefix.Length..] != tag) return false;
        safeUrl = $"https://github.com/{owner}/{repository}/releases/tag/{Uri.EscapeDataString(tag)}";
        return true;
    }

    private static bool TryHttps(string? text, string host, out Uri uri, bool allowEscapedTag = false)
    {
        uri = null!;
        if (text is null || text.Any(char.IsWhiteSpace) || text.Contains('\\')
            || !Uri.TryCreate(text, UriKind.Absolute, out var parsed)
            || parsed.Scheme != Uri.UriSchemeHttps || !string.Equals(parsed.Host, host, StringComparison.OrdinalIgnoreCase)
            || !parsed.IsDefaultPort || parsed.UserInfo.Length != 0 || parsed.Query.Length != 0 || parsed.Fragment.Length != 0)
            return false;
        // Reject encoded separators and dot segments before Uri normalizes them.
        if (!allowEscapedTag && text.Contains('%') || text.Split('/').Any(part => part is "." or "..")) return false;
        uri = parsed;
        return true;
    }

    private sealed class ReleaseVersion
    {
        private readonly string[] core;
        internal readonly string[] Prerelease;
        private ReleaseVersion(string[] core, string[] prerelease) { this.core = core; Prerelease = prerelease; }

        internal static ReleaseVersion? Parse(string? text)
        {
            if (text is null || text.Length > 128) return null;
            if (text.StartsWith('v')) text = text[1..];
            var match = Regex.Match(text, @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?\z", RegexOptions.CultureInvariant);
            if (!match.Success) return null;
            var pre = match.Groups[4].Success ? match.Groups[4].Value.Split('.') : [];
            if (pre.Any(part => IsNumber(part) && part.Length > 1 && part[0] == '0')) return null;
            return new([match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value], pre);
        }

        internal int CompareTo(ReleaseVersion other)
        {
            for (var index = 0; index < 3; index++)
            {
                var comparison = CompareNumber(core[index], other.core[index]);
                if (comparison != 0) return comparison;
            }
            if (Prerelease.Length == 0 || other.Prerelease.Length == 0)
                return (Prerelease.Length == 0 ? 1 : 0).CompareTo(other.Prerelease.Length == 0 ? 1 : 0);
            for (var index = 0; index < Math.Min(Prerelease.Length, other.Prerelease.Length); index++)
            {
                var left = Prerelease[index];
                var right = other.Prerelease[index];
                var comparison = IsNumber(left) && IsNumber(right) ? CompareNumber(left, right)
                    : IsNumber(left) ? -1 : IsNumber(right) ? 1 : string.CompareOrdinal(left, right);
                if (comparison != 0) return comparison;
            }
            return Prerelease.Length.CompareTo(other.Prerelease.Length);
        }

        private static bool IsNumber(string text) => text.All(character => character is >= '0' and <= '9');
        private static int CompareNumber(string left, string right) => left.Length != right.Length
            ? left.Length.CompareTo(right.Length) : string.CompareOrdinal(left, right);
    }
}

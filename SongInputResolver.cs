using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace EasyMusicScore;

internal sealed record ResolvedSongInput(string Title, string Artist, bool IsAiSong, string Message);

internal static class SongInputResolver
{
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    // AI 제작 서비스명, 일반적인 AI 제작 문구, 독립 표기 AI를 폭넓게 확인합니다.
    // MP3의 태그 정보는 사용하지 않고 파일명만 판별 기준으로 사용합니다.
    private static readonly Regex[] AiNamePatterns =
    {
        new Regex(@"(?<![\p{L}\p{N}])(?:suno(?:\s*ai)?|udio|aiva|soundraw|boomy|loudly|mubert|beatoven(?:\.ai)?|stable[\s._-]*audio|musicgen|riffusion|udioai|donna[\s._-]*ai)(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new Regex(@"(?<![\p{L}\p{N}])(?:AI[\s._-]*(?:song|music|track|generated|generation|created|composition|composer)|(?:song|music|track)[\s._-]*AI)(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new Regex(@"(?:인공지능|AI)\s*(?:로\s*)?(?:생성|제작|작곡|만든|음악|곡)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new Regex(@"(?<![\p{L}\p{N}])AI(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new Regex(@"(?:생성형\s*음악|AI\s*보컬|AI\s*커버|AI\s*작곡)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
    };

    public static async Task<ResolvedSongInput> ResolveAsync(string input)
    {
        if (Uri.TryCreate(input, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            var streamTitle = await ReadIcyTitleAsync(input);
            if (string.IsNullOrWhiteSpace(streamTitle))
                return new ResolvedSongInput("", "", false,
                    "방송 주소는 확인했지만 현재 방송에서 곡 제목 정보를 가져오지 못했습니다. 방송국이 곡명 메타데이터를 제공하는지 확인해 주세요.");

            var parsed = ParseTitleArtist(streamTitle, artistFirst: true);
            return new ResolvedSongInput(parsed.Title, parsed.Artist,
                LooksLikeAiSong(streamTitle), "방송에서 현재 재생 중인 곡 정보를 가져왔습니다.");
        }

        if (File.Exists(input) && string.Equals(Path.GetExtension(input), ".mp3", StringComparison.OrdinalIgnoreCase))
        {
            // 태그가 비어 있거나 사용자가 삭제한 경우에도 동일하게 작동하도록
            // 곡 정보와 AI 여부를 파일명 하나에서만 판단합니다.
            var fileName = Path.GetFileNameWithoutExtension(input);
            var isAiSong = LooksLikeAiSong(fileName);
            var cleanName = RemoveAiMarkers(fileName);
            var parsed = ParseTitleArtist(cleanName);
            return new ResolvedSongInput(parsed.Title, parsed.Artist, isAiSong,
                "MP3 파일명에서 곡 정보를 확인했습니다.");
        }

        var looksLikePath = input.Contains('\\') || input.Contains('/') ||
                            input.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase);
        var text = looksLikePath ? Path.GetFileNameWithoutExtension(input) : input;
        var result = ParseTitleArtist(RemoveAiMarkers(text));
        return new ResolvedSongInput(result.Title, result.Artist,
            LooksLikeAiSong(text), looksLikePath
                ? "파일 경로 또는 파일명에서 곡 정보를 추출했습니다."
                : "입력한 곡 제목과 가수명을 검색어로 사용합니다.");
    }

    private static bool LooksLikeAiSong(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return AiNamePatterns.Any(pattern => pattern.IsMatch(value));
    }

    private static string RemoveAiMarkers(string value)
    {
        var result = value ?? "";
        foreach (var pattern in AiNamePatterns)
            result = pattern.Replace(result, " ");
        result = Regex.Replace(result, @"[\[\(\{]\s*[\]\)\}]", " ");
        result = Regex.Replace(result, @"\s{2,}", " ");
        result = Regex.Replace(result, @"^[\s\-_.()\[\]{}]+|[\s\-_.()\[\]{}]+$", "");
        return result.Trim();
    }

    private static (string Title, string Artist) ParseTitleArtist(string value, bool artistFirst = false)
    {
        var text = (value ?? "").Trim();
        text = Regex.Replace(text, @"^\s*\d{1,3}\s*[-._) ]+\s*", "");
        text = Regex.Replace(text, @"\.(mp3|wav|flac|m4a)$", "", RegexOptions.IgnoreCase).Trim();

        var separators = new[] { " - ", " – ", " — ", "－", "_" };
        foreach (var separator in separators)
        {
            var parts = text.Split(new[] { separator }, 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
                return artistFirst ? (parts[1].Trim(), parts[0].Trim()) : (parts[0].Trim(), parts[1].Trim());
        }
        return (text, "");
    }

    private static async Task<string> ReadIcyTitleAsync(string streamUrl)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(18));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, streamUrl);
            request.Headers.TryAddWithoutValidation("Icy-MetaData", "1");
            request.Headers.TryAddWithoutValidation("User-Agent", "EasyMusicScore/1.14");
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();

            if (!response.Headers.TryGetValues("icy-metaint", out var values) &&
                !response.Headers.TryGetValues("Icy-MetaInt", out values))
                return "";
            if (!int.TryParse(values.FirstOrDefault(), out var interval) || interval <= 0 || interval > 2_000_000)
                return "";

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var lengthByte = new byte[1];
            var declared = response.Content.Headers.ContentType?.CharSet?.Trim('"', '\'');
            while (!timeout.IsCancellationRequested)
            {
                var audio = new byte[interval];
                await ReadFullyAsync(stream, audio, timeout.Token);
                await ReadFullyAsync(stream, lengthByte, timeout.Token);
                var metadataLength = lengthByte[0] * 16;
                if (metadataLength <= 0) continue;

                var metadata = new byte[metadataLength];
                await ReadFullyAsync(stream, metadata, timeout.Token);
                var raw = DecodeMetadata(metadata, declared).Trim('\0', ' ', '\r', '\n', '\t');
                var match = Regex.Match(raw, @"(?i)StreamTitle\s*=\s*['""]([^'""]*)['""]");
                if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
                    return match.Groups[1].Value.Trim();
            }
            return "";
        }
        catch
        {
            return "";
        }
    }

    private static async Task ReadFullyAsync(System.IO.Stream stream, byte[] buffer, CancellationToken token)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), token);
            if (read <= 0) throw new EndOfStreamException();
            offset += read;
        }
    }

    private static string DecodeMetadata(byte[] bytes, string? declaredCharset)
    {
        var candidates = new[] { declaredCharset, "UTF-8", "MS949", "EUC-KR", "ISO-8859-1" }
            .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase);
        string best = "";
        var bestHangul = -1;
        foreach (var name in candidates)
        {
            try
            {
                var encoding = Encoding.GetEncoding(name!);
                var text = encoding.GetString(bytes);
                var hangul = text.Count(c => c >= '\uAC00' && c <= '\uD7A3');
                if (hangul > bestHangul) { best = text; bestHangul = hangul; }
            }
            catch { }
        }
        return best;
    }
}
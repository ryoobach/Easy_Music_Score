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

    public static async Task<ResolvedSongInput> ResolveAsync(string input)
    {
        if (Uri.TryCreate(input, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            var streamTitle = await ReadIcyTitleAsync(input);
            if (string.IsNullOrWhiteSpace(streamTitle))
                return new ResolvedSongInput("", "", false,
                    "방송 주소는 확인했지만 현재 방송에서 곡 제목 정보를 가져오지 못했습니다. 방송국이 곡명 메타데이터를 제공하는지 확인해 주세요.");

            var parsed = ParseTitleArtist(streamTitle);
            return new ResolvedSongInput(parsed.Title, parsed.Artist,
                LooksLikeAiSong(streamTitle), "방송에서 현재 재생 중인 곡 정보를 가져왔습니다.");
        }

        if (File.Exists(input) && string.Equals(Path.GetExtension(input), ".mp3", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var taggedFile = TagLib.File.Create(input);
                var taggedTitle = taggedFile.Tag.Title?.Trim() ?? "";
                var taggedArtist = taggedFile.Tag.FirstPerformer?.Trim() ?? "";
                var fallback = ParseTitleArtist(Path.GetFileNameWithoutExtension(input));
                var title = string.IsNullOrWhiteSpace(taggedTitle) ? fallback.Title : taggedTitle;
                var artist = string.IsNullOrWhiteSpace(taggedArtist) ? fallback.Artist : taggedArtist;
                return new ResolvedSongInput(title, artist,
                    LooksLikeAiSong(input + " " + taggedTitle + " " + taggedArtist),
                    "MP3 파일명과 음악 정보를 확인했습니다.");
            }
            catch
            {
                var fallback = ParseTitleArtist(Path.GetFileNameWithoutExtension(input));
                return new ResolvedSongInput(fallback.Title, fallback.Artist,
                    LooksLikeAiSong(input), "MP3 파일의 태그를 읽지 못해 파일명에서 곡 정보를 추출했습니다.");
            }
        }

        var looksLikePath = input.Contains('\\') || input.Contains('/') ||
                            input.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase);
        var text = looksLikePath ? Path.GetFileNameWithoutExtension(input) : input;
        var result = ParseTitleArtist(text);
        return new ResolvedSongInput(result.Title, result.Artist,
            LooksLikeAiSong(input), looksLikePath
                ? "파일 경로 또는 파일명에서 곡 정보를 추출했습니다."
                : "입력한 곡 제목과 가수명을 검색어로 사용합니다.");
    }

    private static (string Title, string Artist) ParseTitleArtist(string value)
    {
        var text = (value ?? "").Trim();
        text = Regex.Replace(text, @"^\s*\d{1,3}\s*[-._) ]+\s*", "");
        text = Regex.Replace(text, @"\.(mp3|wav|flac|m4a)$", "", RegexOptions.IgnoreCase).Trim();

        var separators = new[] { " - ", " – ", " — ", "－" };
        foreach (var separator in separators)
        {
            var parts = text.Split(new[] { separator }, 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
                return (parts[0].Trim(), parts[1].Trim());
        }
        return (text, "");
    }

    private static bool LooksLikeAiSong(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return Regex.IsMatch(value,
            @"(?i)(\bSuno\b|\bUdio\b|\bAI[ _-]?(song|music|generated)\b|AI\s*곡|인공지능\s*음악|AI로\s*생성)");
    }

    private static async Task<string> ReadIcyTitleAsync(string streamUrl)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(18));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, streamUrl);
            request.Headers.TryAddWithoutValidation("Icy-MetaData", "1");
            request.Headers.TryAddWithoutValidation("User-Agent", "EasyMusicScore/1.10");
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();

            if (!response.Headers.TryGetValues("icy-metaint", out var values) &&
                !response.Headers.TryGetValues("Icy-MetaInt", out values))
                return "";
            if (!int.TryParse(values.FirstOrDefault(), out var interval) || interval <= 0 || interval > 2_000_000)
                return "";

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var audio = new byte[interval];
            await ReadFullyAsync(stream, audio, timeout.Token);
            var lengthByte = new byte[1];
            await ReadFullyAsync(stream, lengthByte, timeout.Token);
            var metadataLength = lengthByte[0] * 16;
            if (metadataLength <= 0) return "";

            var metadata = new byte[metadataLength];
            await ReadFullyAsync(stream, metadata, timeout.Token);
            var declared = response.Content.Headers.ContentType?.CharSet?.Trim('"', '\'');
            var raw = DecodeMetadata(metadata, declared).Trim('\0', ' ', '\r', '\n', '\t');
            var match = Regex.Match(raw, @"(?i)StreamTitle\s*=\s*['""]([^'""]*)['""]");
            return match.Success ? match.Groups[1].Value.Trim() : "";
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

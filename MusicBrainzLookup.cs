using System;
using System.Collections.Generic;
using System.Linq;\nusing System.Text.RegularExpressions;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace EasyMusicScore;\n\ninternal sealed record ArtistCandidate(string Artist, string Album, string Year);

internal static class MusicBrainzLookup
{
    private static readonly HttpClient Client = CreateClient();
    private static DateTime _lastRequestUtc = DateTime.MinValue;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "EasyMusicScore/1.06 (https://github.com/ryoobach/Easy_Music_Score)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    public static async Task<(string Title, string Artist)?> FindAsync(
        IEnumerable<(string Title, string Artist)> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.Title) ||
                string.IsNullOrWhiteSpace(candidate.Artist))
                continue;

            try
            {
                var wait = TimeSpan.FromSeconds(1.1) - (DateTime.UtcNow - _lastRequestUtc);
                if (wait > TimeSpan.Zero) await Task.Delay(wait);

                var query = $"recording:\"{EscapeQuery(candidate.Title)}\" AND artist:\"{EscapeQuery(candidate.Artist)}\"";
                var url = "https://musicbrainz.org/ws/2/recording/?fmt=json&limit=5&query=" +
                          Uri.EscapeDataString(query);

                using var response = await Client.GetAsync(url);
                _lastRequestUtc = DateTime.UtcNow;
                if (!response.IsSuccessStatusCode) continue;

                await using var stream = await response.Content.ReadAsStreamAsync();
                using var document = await JsonDocument.ParseAsync(stream);
                if (!document.RootElement.TryGetProperty("recordings", out var recordings)) continue;

                foreach (var recording in recordings.EnumerateArray())
                {
                    var score = recording.TryGetProperty("score", out var scoreNode) &&
                                scoreNode.TryGetInt32(out var parsedScore) ? parsedScore : 0;
                    if (score < 85) continue;

                    var title = recording.TryGetProperty("title", out var titleNode)
                        ? titleNode.GetString()?.Trim() ?? string.Empty : string.Empty;
                    var artist = ReadArtist(recording);
                    if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(artist))
                        return (title, artist);
                }
            }
            catch
            {
                // 공개 자료 연결이 실패해도 MP3 자체의 정보는 계속 표시합니다.
            }
        }

        return null;
    }

    public static async Task<List<ArtistCandidate>> FindArtistsAsync(string title)
    {
        var results = new List<ArtistCandidate>();
        if (string.IsNullOrWhiteSpace(title)) return results;

        try
        {
            var wait = TimeSpan.FromSeconds(1.1) - (DateTime.UtcNow - _lastRequestUtc);
            if (wait > TimeSpan.Zero) await Task.Delay(wait);

            var query = $"recording:\"{EscapeQuery(title)}\"";
            var url = "https://musicbrainz.org/ws/2/recording/?fmt=json&limit=25&query=" +
                      Uri.EscapeDataString(query);

            using var response = await Client.GetAsync(url);
            _lastRequestUtc = DateTime.UtcNow;
            if (!response.IsSuccessStatusCode) return results;

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);
            if (!document.RootElement.TryGetProperty("recordings", out var recordings)) return results;

            foreach (var recording in recordings.EnumerateArray())
            {
                var score = recording.TryGetProperty("score", out var scoreNode) &&
                            scoreNode.TryGetInt32(out var parsedScore) ? parsedScore : 0;
                var foundTitle = recording.TryGetProperty("title", out var titleNode)
                    ? titleNode.GetString()?.Trim() ?? string.Empty : string.Empty;

                // 제목이 실제로 일치하는 결과만 사용해 동명이곡 오인을 줄입니다.
                if (score < 75 || !string.Equals(NormalizeTitle(foundTitle), NormalizeTitle(title),
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                var artist = ReadArtist(recording);
                if (string.IsNullOrWhiteSpace(artist) ||
                    results.Any(x => string.Equals(x.Artist, artist, StringComparison.OrdinalIgnoreCase)))
                    continue;

                string album = string.Empty;
                string year = string.Empty;
                if (recording.TryGetProperty("releases", out var releases) &&
                    releases.ValueKind == JsonValueKind.Array && releases.GetArrayLength() > 0)
                {
                    var release = releases[0];
                    if (release.TryGetProperty("title", out var albumNode))
                        album = albumNode.GetString() ?? string.Empty;
                    if (release.TryGetProperty("date", out var dateNode))
                    {
                        var date = dateNode.GetString() ?? string.Empty;
                        year = date.Length >= 4 ? date[..4] : date;
                    }
                }

                results.Add(new ArtistCandidate(artist, album, year));
            }
        }
        catch
        {
            // 무료 공개 자료에 연결하지 못하면 가수 후보를 임의로 만들지 않습니다.
        }

        return results;
    }

    private static string NormalizeTitle(string value) =>
        Regex.Replace(value ?? string.Empty, @"[\s\p{P}\p{S}]+, string.Empty)
             .ToLowerInvariant();

    private static string ReadArtist(JsonElement recording)
    {
        if (!recording.TryGetProperty("artist-credit", out var credits) ||
            credits.ValueKind != JsonValueKind.Array)
            return string.Empty;

        var names = new List<string>();
        foreach (var credit in credits.EnumerateArray())
        {
            if (credit.ValueKind != JsonValueKind.Object) continue;
            if (credit.TryGetProperty("name", out var creditName) &&
                creditName.ValueKind == JsonValueKind.String)
            {
                names.Add(creditName.GetString() ?? string.Empty);
                continue;
            }

            if (credit.TryGetProperty("artist", out var artist) &&
                artist.TryGetProperty("name", out var artistName))
                names.Add(artistName.GetString() ?? string.Empty);
        }

        return string.Join("", names.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static string EscapeQuery(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}

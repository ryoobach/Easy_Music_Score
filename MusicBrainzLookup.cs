using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace EasyMusicScore;

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

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LyricalWidget.Lyrics;

public record LyricWord(
    [property: JsonPropertyName("timestamp")] TimeSpan Timestamp,
    [property: JsonPropertyName("duration")] TimeSpan Duration,
    [property: JsonPropertyName("text")] string Text
)
{
    [JsonIgnore]
    public TimeSpan EndTime => Timestamp + Duration;
}

public class LyricLine
{
    [JsonPropertyName("timestamp")]
    public TimeSpan Timestamp { get; set; }

    [JsonPropertyName("duration")]
    public TimeSpan Duration { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("subText")]
    public string? SubText { get; set; }

    [JsonPropertyName("words")]
    public List<LyricWord>? Words { get; set; }

    [JsonIgnore]
    public bool HasWordSync => Words != null && Words.Count > 0;

    [JsonIgnore]
    public bool HasSubText => !string.IsNullOrWhiteSpace(SubText);

    [JsonIgnore]
    public TimeSpan EndTime => Duration > TimeSpan.Zero ? Timestamp + Duration : Timestamp + TimeSpan.FromSeconds(4);

    public LyricLine() { }

    public LyricLine(TimeSpan timestamp, string text, TimeSpan duration = default, List<LyricWord>? words = null, string? subText = null)
    {
        Timestamp = timestamp;
        Text = text;
        Duration = duration;
        Words = words;
        SubText = subText;
    }
}

public class LrcDocument
{
    [JsonPropertyName("hasSyncedLyrics")]
    public bool HasSyncedLyrics => Lines.Count > 0;

    [JsonPropertyName("isInstrumental")]
    public bool IsInstrumental { get; set; }

    [JsonPropertyName("isNotFound")]
    public bool IsNotFound { get; set; }

    [JsonPropertyName("provider")]
    public string Provider { get; set; } = string.Empty;

    [JsonPropertyName("userOffsetMs")]
    public int UserOffsetMs { get; set; } = 0;

    [JsonPropertyName("lines")]
    public List<LyricLine> Lines { get; set; } = new();

    [JsonPropertyName("plainLyrics")]
    public string PlainLyrics { get; set; } = string.Empty;

    [JsonIgnore]
    public bool HasWordSync
    {
        get
        {
            if (Lines == null || Lines.Count == 0) return false;
            foreach (var line in Lines)
            {
                if (line.HasWordSync) return true;
            }
            return false;
        }
    }

    public static LrcDocument Empty(bool isInstrumental = false, bool isNotFound = false, string provider = "") => new()
    {
        IsInstrumental = isInstrumental,
        IsNotFound = isNotFound,
        Provider = provider,
        Lines = new List<LyricLine>()
    };
}

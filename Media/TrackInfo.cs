using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;

namespace LyricalWidget.Media;

public class TrackInfo
{
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public BitmapImage? Thumbnail { get; set; }
    public TimeSpan Duration { get; set; } = TimeSpan.Zero;
    public bool IsPlaying { get; set; }
    public string SourceAppId { get; set; } = string.Empty;

    public bool IsBrowserSource => SmtcMediaService.IsBrowserApp(SourceAppId);

    /// <summary>Ordered (artist, title) guesses for browser media; empty for dedicated music apps.</summary>
    public IReadOnlyList<(string Artist, string Title)> LookupCandidates { get; set; } = Array.Empty<(string, string)>();

    public bool IsValid => !string.IsNullOrWhiteSpace(Title);

    public bool IsDjOrInterstitial
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Title)) return true;

            string t = Title.Trim();
            string a = Artist.Trim();
            double durSec = Duration.TotalSeconds;

            if (t.Equals("DJ", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("DJ X", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("Advertisement", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("Spotify", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Browser titles are free-form, so the guesses below would misfire on them
            if (IsBrowserSource) return false;

            // Artist is "Spotify" or empty while duration is 0 or under 18 seconds
            if ((string.IsNullOrWhiteSpace(a) || a.Equals("Spotify", StringComparison.OrdinalIgnoreCase)) &&
                durSec <= 18.0)
            {
                return true;
            }

            // Known DJ interstitial / commentary patterns
            if (t.StartsWith("DJ ", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("DJ Commentary", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("Spotify DJ", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("Audio Ad", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("Advertisement", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (a.Equals("DJ", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("DJ X", StringComparison.OrdinalIgnoreCase) ||
                a.Contains("Spotify DJ", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
    }

    public string CacheKey
    {
        get
        {
            int durSec = (int)Duration.TotalSeconds;
            return $"{Artist}_{Title}_{Album}_{durSec}";
        }
    }
}

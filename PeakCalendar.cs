using System.Globalization;

namespace LiangWenWidget;

// Mirrors the peak/valley calendar of github.com/sukholovskyyy/dsh-liang-price-badge.
// Beijing time (UTC+8). Peak = weekdays (Mon-Fri, non-holiday) 09:00-12:00 and 14:00-18:00.
public static class PeakCalendar
{
    private const int Levels = 30; // 31 frames: 00 plain -> 30 梁祖
    private static readonly (int Start, int End)[] PeakWindows =
    [
        (9 * 60, 12 * 60),
        (14 * 60, 18 * 60),
    ];

    private static readonly HashSet<string> Holidays = new(StringComparer.Ordinal)
    {
        "2026-01-01", "2026-01-02",
        "2026-02-16", "2026-02-17", "2026-02-18", "2026-02-19", "2026-02-20", "2026-02-23",
        "2026-04-06",
        "2026-05-01", "2026-05-04", "2026-05-05",
        "2026-06-19",
        "2026-09-25",
        "2026-10-01", "2026-10-02", "2026-10-05", "2026-10-06", "2026-10-07",
        "2027-01-01",
    };

    private static readonly TimeZoneInfo Tz =
        TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");

    public readonly record struct Info(bool IsPeak, DateTime NextBoundary, int FrameIndex, double Progress);

    public static Info Compute()
    {
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Tz);
        bool isPeak = IsPeakAt(now);

        var next = Boundary(now, forward: true);
        var prev = Boundary(now, forward: false);
        if (next <= prev) next = now.AddHours(24);

        double progress = (now - prev).TotalSeconds / Math.Max(1, (next - prev).TotalSeconds);
        progress = Math.Clamp(progress, 0, 1);

        int level = (int)Math.Round(progress * Levels);
        int frameIndex = isPeak ? level : Levels - level;
        frameIndex = Math.Clamp(frameIndex, 0, Levels);

        return new Info(isPeak, next, frameIndex, progress);
    }

    private static bool IsPeakDay(DateTime d)
    {
        if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return false;
        return !Holidays.Contains(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private static bool IsPeakAt(DateTime now)
    {
        if (!IsPeakDay(now.Date)) return false;
        int minutes = now.Hour * 60 + now.Minute;
        return PeakWindows.Any(w => minutes >= w.Start && minutes < w.End);
    }

    // nearest window edge strictly after (forward) or at-or-before (!forward) now
    private static DateTime Boundary(DateTime now, bool forward)
    {
        DateTime best = DateTime.MinValue;
        bool found = false;
        foreach (var (s, e) in EnumerateEdges(now.Date))
        {
            bool ok = forward ? s > now : s <= now;
            if (ok && (!found || (forward ? s < best : s > best))) { best = s; found = true; }
        }
        return found ? best : (forward ? now.AddHours(24) : now.AddHours(-8));
    }

    private static IEnumerable<(DateTime Start, DateTime End)> EnumerateEdges(DateTime anchorDate)
    {
        for (int offset = -8; offset <= 8; offset++)
        {
            var day = anchorDate.AddDays(offset);
            if (!IsPeakDay(day)) continue;
            foreach (var w in PeakWindows)
            {
                yield return (day.AddMinutes(w.Start), day.AddMinutes(w.End));
            }
        }
    }
}

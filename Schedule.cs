using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace VenueLedger;

/// <summary>
/// Timezone resolution and display. Dalamud-free: the harness exercises this on any host.
/// Ids are stored as IANA names when the platform can convert them, because those are portable
/// between machines; a Windows id still resolves, and an unreadable id falls back to system local
/// instead of throwing in the middle of a draw.
/// </summary>
public static class ZoneBook
{
    public static readonly string[] Common =
    {
        "UTC", "Europe/London", "Europe/Lisbon", "Europe/Paris", "Europe/Berlin", "Europe/Madrid",
        "Europe/Rome", "Europe/Stockholm", "Europe/Warsaw", "Europe/Athens", "Europe/Istanbul",
        "Europe/Moscow", "Asia/Dubai", "Asia/Karachi", "Asia/Kolkata", "Asia/Kathmandu", "Asia/Dhaka",
        "Asia/Bangkok", "Asia/Jakarta", "Asia/Singapore", "Asia/Manila", "Asia/Hong_Kong",
        "Asia/Shanghai", "Asia/Taipei", "Asia/Seoul", "Asia/Tokyo", "Australia/Perth",
        "Australia/Adelaide", "Australia/Brisbane", "Australia/Sydney", "Pacific/Auckland",
        "Pacific/Honolulu", "America/Anchorage", "America/Los_Angeles", "America/Denver",
        "America/Chicago", "America/New_York", "America/Halifax", "America/Sao_Paulo",
        "America/Argentina/Buenos_Aires", "Africa/Johannesburg", "Africa/Cairo", "Africa/Lagos",
    };

    /// <summary>Short labels for the zones people actually use in FFXIV, keyed by IANA id.</summary>
    private static readonly Dictionary<string, string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["UTC"] = "UTC",
        ["Europe/London"] = "GMT",
        ["Europe/Lisbon"] = "WET",
        ["Europe/Paris"] = "CET",
        ["Europe/Berlin"] = "CET",
        ["Europe/Madrid"] = "CET",
        ["Europe/Rome"] = "CET",
        ["Europe/Stockholm"] = "CET",
        ["Europe/Warsaw"] = "CET",
        ["Europe/Athens"] = "EET",
        ["Europe/Istanbul"] = "TRT",
        ["Europe/Moscow"] = "MSK",
        ["Asia/Dubai"] = "GST",
        ["Asia/Karachi"] = "PKT",
        ["Asia/Kolkata"] = "IST",
        ["Asia/Kathmandu"] = "NPT",
        ["Asia/Dhaka"] = "BST",
        ["Asia/Bangkok"] = "ICT",
        ["Asia/Jakarta"] = "WIB",
        ["Asia/Singapore"] = "SGT",
        ["Asia/Manila"] = "PHT",
        ["Asia/Hong_Kong"] = "HKT",
        ["Asia/Shanghai"] = "CST",
        ["Asia/Taipei"] = "CST",
        ["Asia/Seoul"] = "KST",
        ["Asia/Tokyo"] = "JST",
        ["Australia/Perth"] = "AWST",
        ["Australia/Adelaide"] = "ACST",
        ["Australia/Brisbane"] = "AEST",
        ["Australia/Sydney"] = "AEST",
        ["Pacific/Auckland"] = "NZST",
        ["Pacific/Honolulu"] = "HST",
        ["America/Anchorage"] = "AKT",
        ["America/Los_Angeles"] = "PT",
        ["America/Denver"] = "MT",
        ["America/Chicago"] = "CT",
        ["America/New_York"] = "ET",
        ["America/Halifax"] = "AT",
        ["America/Sao_Paulo"] = "BRT",
        ["Africa/Johannesburg"] = "SAST",
        ["Africa/Cairo"] = "EET",
        ["Africa/Lagos"] = "WAT",
    };

    private static readonly Dictionary<string, TimeZoneInfo?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// .NET hands back the older IANA spellings through ICU ("Asia/Calcutta" for India). Those read as
    /// obsolete to anyone typing them, so display and persistence use the current name.
    /// </summary>
    private static readonly Dictionary<string, string> ModernIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Asia/Calcutta"] = "Asia/Kolkata",
        ["Asia/Katmandu"] = "Asia/Kathmandu",
        ["Asia/Saigon"] = "Asia/Ho_Chi_Minh",
        ["Asia/Rangoon"] = "Asia/Yangon",
        ["Asia/Chongqing"] = "Asia/Shanghai",
        ["Asia/Ashkhabad"] = "Asia/Ashgabat",
        ["Europe/Kiev"] = "Europe/Kyiv",
        ["America/Buenos_Aires"] = "America/Argentina/Buenos_Aires",
        ["America/Godthab"] = "America/Nuuk",
        ["Pacific/Ponape"] = "Pacific/Pohnpei",
        ["Pacific/Truk"] = "Pacific/Chuuk",
    };

    public static TimeZoneInfo System => TimeZoneInfo.Local;

    /// <summary>Empty/null means the system timezone. An unknown id also means the system timezone.</summary>
    public static TimeZoneInfo Resolve(string? id) => ResolveOrNull(id) ?? TimeZoneInfo.Local;

    public static TimeZoneInfo? ResolveOrNull(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Local;

        var key = id.Trim();
        if (Cache.TryGetValue(key, out var cached)) return cached;

        TimeZoneInfo? zone = null;
        if (TimeZoneInfo.TryFindSystemTimeZoneById(key, out var direct)) zone = direct;

        if (zone is null)
        {
            try
            {
                if (TimeZoneInfo.TryConvertIanaIdToWindowsId(key, out var windowsId) &&
                    TimeZoneInfo.TryFindSystemTimeZoneById(windowsId, out var viaIana))
                {
                    zone = viaIana;
                }
            }
            catch (Exception)
            {
                // ICU-less host: fall through to the Windows-id attempt.
            }
        }

        if (zone is null)
        {
            try
            {
                if (TimeZoneInfo.TryConvertWindowsIdToIanaId(key, out var ianaId) &&
                    TimeZoneInfo.TryFindSystemTimeZoneById(ianaId, out var viaWindows))
                {
                    zone = viaWindows;
                }
            }
            catch (Exception)
            {
            }
        }

        Cache[key] = zone;
        return zone;
    }

    /// <summary>The portable IANA name for a zone, whichever id the platform handed us.</summary>
    public static string IanaId(TimeZoneInfo zone)
    {
        var candidate = zone.Id;

        if (!candidate.Contains('/'))
        {
            try
            {
                if (TimeZoneInfo.TryConvertWindowsIdToIanaId(candidate, out var ianaId) &&
                    !string.IsNullOrWhiteSpace(ianaId))
                {
                    candidate = ianaId;
                }
            }
            catch (Exception)
            {
            }
        }

        return ModernIds.TryGetValue(candidate, out var modern) ? modern : candidate;
    }

    /// <summary>The id to persist for a zone: IANA when convertible, otherwise whatever the platform uses.</summary>
    public static string PersistId(TimeZoneInfo zone) =>
        zone.Id == TimeZoneInfo.Local.Id ? string.Empty : IanaId(zone);

    public static string Label(TimeZoneInfo zone)
    {
        var iana = IanaId(zone);
        if (Abbreviations.TryGetValue(iana, out var abbr)) return abbr;
        if (Abbreviations.TryGetValue(zone.Id, out var windowsAbbr)) return windowsAbbr;

        var slashed = iana.LastIndexOf('/');
        return slashed >= 0 && slashed + 1 < iana.Length ? iana[(slashed + 1)..] : iana;
    }

    public static string OffsetText(TimeZoneInfo zone, DateTimeOffset at)
    {
        var offset = zone.GetUtcOffset(at);
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var abs = offset.Duration();
        return sign + abs.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>"JST · Asia/Tokyo UTC+09:00" - the label alone hides DST, so the offset is always shown.</summary>
    public static string Describe(TimeZoneInfo zone, DateTimeOffset at) =>
        Label(zone) + " · " + IanaId(zone) + " UTC" + OffsetText(zone, at);

    public static bool SameZone(TimeZoneInfo a, TimeZoneInfo b) =>
        string.Equals(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);

    /// <summary>The system clock's own identity, for the header.</summary>
    public static string SystemDescription(DateTimeOffset at) =>
        Describe(TimeZoneInfo.Local, at) + (TimeZoneInfo.Local.IsDaylightSavingTime(at) ? " (DST)" : string.Empty);
}

/// <summary>Lenient wall-clock input: what you would actually type at a door.</summary>
public static class ClockText
{
    private static readonly Regex RxColon = new(@"^(\d{1,2}):(\d{2})(?::\d{2})?\s*(am|pm)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RxMeridiem = new(@"^(\d{1,2})\s*(am|pm)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RxDigits = new(@"^\d{1,4}$", RegexOptions.Compiled);

    public const string Hint = "20:00, 8pm, 8:30pm or 2000";

    public static bool TryParse(string? text, out int minutes, out string error)
    {
        minutes = 0;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "no time given";
            return false;
        }

        var token = text.Trim().ToLowerInvariant().Replace('.', ':');
        if (token.EndsWith(" am", StringComparison.Ordinal) || token.EndsWith(" pm", StringComparison.Ordinal))
            token = token[..^3] + token[^2..];

        var m = RxColon.Match(token);
        if (m.Success)
        {
            var hour = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var minute = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            if (!ApplyMeridiem(ref hour, m.Groups[3].Value) || hour > 23 || minute > 59)
            {
                error = Bad(text);
                return false;
            }

            minutes = hour * 60 + minute;
            return true;
        }

        m = RxMeridiem.Match(token);
        if (m.Success)
        {
            var hour = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            if (!ApplyMeridiem(ref hour, m.Groups[2].Value))
            {
                error = Bad(text);
                return false;
            }

            minutes = hour * 60;
            return true;
        }

        if (!RxDigits.IsMatch(token))
        {
            error = Bad(text);
            return false;
        }

        if (token.Length <= 2)
        {
            var bare = int.Parse(token, CultureInfo.InvariantCulture);
            if (bare > 23)
            {
                error = Bad(text);
                return false;
            }

            minutes = bare * 60;
            return true;
        }

        var packed = int.Parse(token, CultureInfo.InvariantCulture);
        var packedHour = packed / 100;
        var packedMinute = packed % 100;
        if (packedHour > 23 || packedMinute > 59)
        {
            error = Bad(text);
            return false;
        }

        minutes = packedHour * 60 + packedMinute;
        return true;
    }

    private static string Bad(string? text) => "\"" + text + "\" is not a time - try " + Hint + ".";

    /// <summary>Empty meridiem means a 24-hour value; otherwise the hour must read as 1-12.</summary>
    private static bool ApplyMeridiem(ref int hour, string meridiem)
    {
        if (meridiem.Length == 0) return hour <= 23;
        if (hour is < 1 or > 12) return false;
        hour = meridiem.ToLowerInvariant() == "am" ? hour == 12 ? 0 : hour : hour == 12 ? 12 : hour + 12;
        return true;
    }

    /// <summary>Minutes since midnight -&gt; "20:00" or "8:00 PM". 1440 reads as "00:00" of the next day.</summary>
    public static string Format(int minutes, bool use24Hour)
    {
        minutes = ((minutes % 1440) + 1440) % 1440;
        var hour = minutes / 60;
        var minute = minutes % 60;

        if (use24Hour) return hour.ToString("00", CultureInfo.InvariantCulture) + ":" + minute.ToString("00", CultureInfo.InvariantCulture);

        var suffix = hour < 12 ? "AM" : "PM";
        var display = hour % 12;
        if (display == 0) display = 12;
        return display.ToString(CultureInfo.InvariantCulture) + ":" + minute.ToString("00", CultureInfo.InvariantCulture) + " " + suffix;
    }

    /// <summary>"4h 12m", "3d 2h", "12m". Never "00:12:44" - this is read at a glance, not audited.</summary>
    public static string Humanize(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = span.Negate();
        var days = (int)span.TotalDays;
        var hours = span.Hours;
        var minutes = span.Minutes;

        if (days > 0) return days.ToString(CultureInfo.InvariantCulture) + "d " + hours.ToString(CultureInfo.InvariantCulture) + "h";
        if (hours > 0) return hours.ToString(CultureInfo.InvariantCulture) + "h " + minutes.ToString(CultureInfo.InvariantCulture) + "m";
        if (minutes > 0) return minutes.ToString(CultureInfo.InvariantCulture) + "m";
        return span.Seconds.ToString(CultureInfo.InvariantCulture) + "s";
    }

    /// <summary>"6h" / "5h 30m" for a session length, with a zero-minute hour left bare.</summary>
    public static string Length(TimeSpan span)
    {
        var total = (int)Math.Round(span.TotalMinutes);
        var hours = total / 60;
        var minutes = total % 60;
        if (hours > 0 && minutes == 0) return hours.ToString(CultureInfo.InvariantCulture) + "h";
        if (hours > 0) return hours.ToString(CultureInfo.InvariantCulture) + "h " + minutes.ToString(CultureInfo.InvariantCulture) + "m";
        return minutes.ToString(CultureInfo.InvariantCulture) + "m";
    }
}

/// <summary>One open-to-close session as real instants.</summary>
public sealed class Session
{
    public DateTimeOffset Opens { get; init; }
    public DateTimeOffset Closes { get; init; }

    /// <summary>The wall clock landed in a DST spring-forward gap and was nudged forward an hour.</summary>
    public bool OpenShifted { get; init; }

    public bool CloseShifted { get; init; }

    public TimeSpan Length => Closes - Opens;
}

public enum VenueState
{
    Unknown,
    Open,
    Closed,
}

/// <summary>The answer to "is it on right now, and when is it next on".</summary>
public sealed class VenueStatus
{
    public VenueState State { get; init; } = VenueState.Unknown;
    public string Error { get; init; } = string.Empty;

    /// <summary>The session that is live now, or the next one to open.</summary>
    public Session? Session { get; init; }

    public TimeSpan Remaining { get; init; }

    public bool IsOpen => State == VenueState.Open;
    public bool Known => State != VenueState.Unknown;
}

public static class Scheduler
{
    /// <summary>Reads a venue's two wall clocks and two zones. Fails with a message, never throws.</summary>
    public static bool TryReadHours(Venue venue, out int openMinutes, out int closeMinutes,
        out TimeZoneInfo openZone, out TimeZoneInfo closeZone, out string error)
    {
        openMinutes = 0;
        closeMinutes = 0;
        openZone = ZoneBook.System;
        closeZone = ZoneBook.System;

        if (!ClockText.TryParse(venue.OpenTime, out openMinutes, out var openError))
        {
            error = "Opening time: " + openError;
            return false;
        }

        if (!ClockText.TryParse(venue.CloseTime, out closeMinutes, out var closeError))
        {
            error = "Closing time: " + closeError;
            return false;
        }

        var openId = string.IsNullOrWhiteSpace(venue.OpenZone) ? string.Empty : venue.OpenZone.Trim();
        var closeId = string.IsNullOrWhiteSpace(venue.CloseZone) ? string.Empty : venue.CloseZone.Trim();

        var resolvedOpen = ZoneBook.ResolveOrNull(openId);
        if (resolvedOpen is null)
        {
            error = "Opening timezone \"" + venue.OpenZone + "\" is not a zone this system knows.";
            return false;
        }

        var resolvedClose = ZoneBook.ResolveOrNull(closeId);
        if (resolvedClose is null)
        {
            error = "Closing timezone \"" + venue.CloseZone + "\" is not a zone this system knows.";
            return false;
        }

        openZone = resolvedOpen;
        closeZone = resolvedClose;
        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Every session within the window, one per opening day. A closing wall clock that resolves to
    /// before the opening instant is read as the next day - that is how "20:00 - 02:00" works.
    /// </summary>
    public static List<Session> Sessions(Venue venue, DateTimeOffset now, int daysBack = 1, int daysForward = 7)
    {
        var sessions = new List<Session>();
        if (!TryReadHours(venue, out var openMinutes, out var closeMinutes, out var openZone, out var closeZone, out _))
            return sessions;

        var anchor = TimeZoneInfo.ConvertTime(now, openZone).Date.AddDays(-daysBack);

        for (var i = 0; i <= daysBack + daysForward; i++)
        {
            var date = anchor.AddDays(i);

            var open = ToInstant(date.AddMinutes(openMinutes), openZone, out var openShifted);
            var closeDate = TimeZoneInfo.ConvertTime(open, closeZone).Date;
            var close = ToInstant(closeDate.AddMinutes(closeMinutes), closeZone, out var closeShifted);

            var guard = 0;
            while (close <= open && guard++ < 3)
            {
                closeDate = closeDate.AddDays(1);
                close = ToInstant(closeDate.AddMinutes(closeMinutes), closeZone, out closeShifted);
            }

            if (close <= open)
            {
                // Both clocks land on the same minute in different zones: a one-hour door, not a crash.
                close = open.AddHours(1);
                closeShifted = true;
            }

            sessions.Add(new Session
            {
                Opens = open,
                Closes = close,
                OpenShifted = openShifted,
                CloseShifted = closeShifted,
            });
        }

        return sessions.OrderBy(s => s.Opens).ToList();
    }

    public static VenueStatus Status(Venue venue, DateTimeOffset now)
    {
        if (!TryReadHours(venue, out _, out _, out _, out _, out var hoursError))
            return new VenueStatus { Error = hoursError };

        var sessions = Sessions(venue, now);

        foreach (var session in sessions)
        {
            if (session.Opens <= now && now < session.Closes)
            {
                return new VenueStatus
                {
                    State = VenueState.Open,
                    Session = session,
                    Remaining = session.Closes - now,
                };
            }
        }

        var next = sessions.FirstOrDefault(s => s.Opens > now);
        if (next is null)
            return new VenueStatus { Error = "no opening time within the next week" };

        return new VenueStatus
        {
            State = VenueState.Closed,
            Session = next,
            Remaining = next.Opens - now,
        };
    }

    /// <summary>
    /// A wall clock read as being in <paramref name="zone"/>. A time that does not exist there
    /// (the hour DST skips) moves forward instead of throwing.
    /// </summary>
    public static DateTimeOffset ToInstant(DateTime wallClock, TimeZoneInfo zone, out bool shifted)
    {
        var naive = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
        shifted = false;

        for (var i = 0; i < 3 && zone.IsInvalidTime(naive); i++)
        {
            naive = naive.AddMinutes(30);
            shifted = true;
        }

        var offset = zone.GetUtcOffset(naive);
        var instant = new DateTimeOffset(naive, offset);

        // GetUtcOffset is already unambiguous, but converting back proves it - a host with a broken
        // tz database must not silently produce a wrong instant.
        return TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.Utc).ToOffset(offset);
    }
}

/// <summary>Everything the UI and the exporter need about one venue, worked out once per frame.</summary>
public sealed class VenueRow
{
    public Venue Venue { get; init; } = new();
    public VenueStatus Status { get; init; } = new();

    public bool Known => Status.Known;
    public bool IsOpen => Status.IsOpen;
    public string Error => Status.Error;

    public string OwnOpenText { get; init; } = "-";
    public string OwnCloseText { get; init; } = "-";

    /// <summary>"JST" when both clocks share a zone, "JST -&gt; CET" when they do not.</summary>
    public string OwnZoneText { get; init; } = string.Empty;

    public string BoardOpenText { get; init; } = "-";
    public string BoardCloseText { get; init; } = "-";

    /// <summary>"+1d" when the session ends on another day in the board timezone.</summary>
    public string BoardDayText { get; init; } = string.Empty;

    public string LengthText { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public string Travel { get; init; } = string.Empty;

    /// <summary>Session pieces clipped to the board day, for the day strip.</summary>
    public IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> Segments { get; init; } =
        Array.Empty<(DateTimeOffset, DateTimeOffset)>();
}

/// <summary>The whole board: one shared clock, every venue projected onto it.</summary>
public sealed class BoardView
{
    public TimeZoneInfo BoardZone { get; init; } = TimeZoneInfo.Local;
    public DateTimeOffset Now { get; init; }
    public DateTimeOffset DayStart { get; init; }
    public DateTimeOffset DayEnd { get; init; }
    public bool Use24Hour { get; init; } = true;
    public List<VenueRow> Rows { get; init; } = new();

    public int OpenNow => Rows.Count(r => r.IsOpen);
    public int Unknown => Rows.Count(r => !r.Known);

    public string BoardZoneText => ZoneBook.Describe(BoardZone, Now);
}

public static class Board
{
    /// <summary>
    /// Builds the comparison view. Every venue's hours are projected into
    /// <paramref name="boardZoneId"/> (empty = system local) for the day that contains <paramref name="now"/>.
    /// </summary>
    public static BoardView Build(IReadOnlyList<Venue> venues, string? boardZoneId, DateTimeOffset now, bool use24Hour)
    {
        var boardZone = ZoneBook.Resolve(boardZoneId);
        var localNow = TimeZoneInfo.ConvertTime(now, boardZone);
        var dayStart = new DateTimeOffset(localNow.Date, boardZone.GetUtcOffset(localNow));
        var dayEnd = dayStart.AddDays(1);

        var rows = new List<VenueRow>(venues.Count);
        foreach (var venue in venues)
        {
            rows.Add(BuildRow(venue, boardZone, now, dayStart, dayEnd, use24Hour));
        }

        return new BoardView
        {
            BoardZone = boardZone,
            Now = now,
            DayStart = dayStart,
            DayEnd = dayEnd,
            Use24Hour = use24Hour,
            Rows = rows,
        };
    }

    private static VenueRow BuildRow(Venue venue, TimeZoneInfo boardZone, DateTimeOffset now,
        DateTimeOffset dayStart, DateTimeOffset dayEnd, bool use24Hour)
    {
        var status = Scheduler.Status(venue, now);

        if (!status.Known)
        {
            return new VenueRow
            {
                Venue = venue,
                Status = status,
                StatusText = "hours unknown",
                Travel = Travel.Command(venue) ?? string.Empty,
            };
        }

        Scheduler.TryReadHours(venue, out var openMinutes, out var closeMinutes, out var openZone, out var closeZone, out _);
        var session = status.Session!;

        var boardOpen = TimeZoneInfo.ConvertTime(session.Opens, boardZone);
        var boardClose = TimeZoneInfo.ConvertTime(session.Closes, boardZone);
        var dayDelta = (boardClose.Date - boardOpen.Date).Days;

        var statusText = status.IsOpen
            ? "OPEN - closes in " + ClockText.Humanize(status.Remaining)
            : "closed - opens " + WhenText(session.Opens, boardZone, now, use24Hour, status.Remaining);

        var segments = new List<(DateTimeOffset, DateTimeOffset)>();
        foreach (var candidate in Scheduler.Sessions(venue, now))
        {
            if (candidate.Closes <= dayStart) continue;
            if (candidate.Opens >= dayEnd) break;

            var start = candidate.Opens < dayStart ? dayStart : candidate.Opens;
            var end = candidate.Closes > dayEnd ? dayEnd : candidate.Closes;
            if (end > start) segments.Add((start, end));
        }

        var ownZoneText = ZoneBook.SameZone(openZone, closeZone)
            ? ZoneBook.Label(openZone)
            : ZoneBook.Label(openZone) + " -> " + ZoneBook.Label(closeZone);

        return new VenueRow
        {
            Venue = venue,
            Status = status,
            OwnOpenText = ClockText.Format(openMinutes, use24Hour),
            OwnCloseText = ClockText.Format(closeMinutes, use24Hour),
            OwnZoneText = ownZoneText,
            BoardOpenText = ClockText.Format((int)boardOpen.TimeOfDay.TotalMinutes, use24Hour),
            BoardCloseText = ClockText.Format((int)boardClose.TimeOfDay.TotalMinutes, use24Hour),
            BoardDayText = dayDelta > 0 ? "+" + dayDelta.ToString(CultureInfo.InvariantCulture) + "d" : string.Empty,
            LengthText = ClockText.Length(session.Length),
            StatusText = statusText,
            Travel = Travel.Command(venue) ?? string.Empty,
            Segments = segments,
        };
    }

    /// <summary>"in 3h 12m" today, "Thu 20:00 (in 2d 4h)" when it is a later day.</summary>
    private static string WhenText(DateTimeOffset at, TimeZoneInfo boardZone, DateTimeOffset now, bool use24Hour, TimeSpan until)
    {
        var local = TimeZoneInfo.ConvertTime(at, boardZone);
        var today = TimeZoneInfo.ConvertTime(now, boardZone).Date;
        var clock = ClockText.Format((int)local.TimeOfDay.TotalMinutes, use24Hour);
        var human = "in " + ClockText.Humanize(until);

        if (local.Date == today) return "at " + clock + " (" + human + ")";

        var day = local.Date == today.AddDays(1)
            ? "tomorrow"
            : local.ToString("ddd dd MMM", CultureInfo.InvariantCulture);

        return day + " at " + clock + " (" + human + ")";
    }
}

public sealed class ExportOptions
{
    public bool Use24Hour { get; set; } = true;
    public bool IncludeTravel { get; set; } = true;
    public bool IncludeNotes { get; set; } = true;
}

/// <summary>Turns the board into the text that gets pasted into Discord, a note or chat.</summary>
public static class Exporter
{
    public const string Title = "VENUE LEDGER";

    public static string Build(BoardView board, ExportOptions options)
    {
        var sb = new StringBuilder();
        var stamped = TimeZoneInfo.ConvertTime(board.Now, board.BoardZone);
        var venueCount = board.Rows.Count;

        sb.Append(Title).Append(" - ")
            .Append(venueCount.ToString(CultureInfo.InvariantCulture))
            .Append(venueCount == 1 ? " venue" : " venues")
            .AppendLine();

        sb.Append("Board clock: ").Append(board.BoardZoneText)
            .Append(" - exported ").Append(stamped.ToString("ddd dd MMM yyyy HH:mm", CultureInfo.InvariantCulture))
            .AppendLine();

        if (board.OpenNow > 0)
        {
            sb.Append("Open right now: ").Append(board.OpenNow.ToString(CultureInfo.InvariantCulture))
                .AppendLine(board.OpenNow == 1 ? " venue" : " venues");
        }

        sb.AppendLine();

        var index = 1;
        foreach (var row in board.Rows)
        {
            var venue = row.Venue;
            sb.Append(index.ToString(CultureInfo.InvariantCulture)).Append(". ").Append(venue.DisplayName);

            if (row.Known)
            {
                sb.Append("   [").Append(row.StatusText.ToUpperInvariant()).Append(']');
            }
            else if (row.Error.Length > 0)
            {
                sb.Append("   [").Append(row.Error).Append(']');
            }

            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(venue.Location))
                sb.Append("   Location  ").AppendLine(venue.Location.Trim());

            if (options.IncludeTravel && row.Travel.Length > 0)
                sb.Append("   Travel    ").AppendLine(row.Travel);

            if (row.Known)
            {
                sb.Append("   Hours     ")
                    .Append(row.OwnOpenText).Append('-').Append(row.OwnCloseText).Append(' ').Append(row.OwnZoneText)
                    .Append("  ->  ")
                    .Append(row.BoardOpenText).Append('-').Append(row.BoardCloseText)
                    .Append(row.BoardDayText.Length > 0 ? " " + row.BoardDayText : string.Empty)
                    .Append(' ').Append(ZoneBook.Label(board.BoardZone))
                    .Append("  (").Append(row.LengthText).Append(')')
                    .AppendLine();
            }
            else
            {
                sb.Append("   Hours     ")
                    .Append(venue.OpenTime).Append('-').Append(venue.CloseTime)
                    .Append(" (not converted: ").Append(row.Error).Append(')')
                    .AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(venue.Website))
                sb.Append("   Website   ").AppendLine(venue.Website.Trim());

            if (!string.IsNullOrWhiteSpace(venue.Discord))
                sb.Append("   Discord   ").AppendLine(venue.Discord.Trim());

            if (options.IncludeNotes && !string.IsNullOrWhiteSpace(venue.Notes))
                sb.Append("   Notes     ").AppendLine(venue.Notes.Trim().Replace("\r\n", " / ").Replace('\n', ' '));

            sb.AppendLine();
            index++;
        }

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    /// <summary>File name for an export: sortable, no spaces, no colon (which Windows rejects).</summary>
    public static string FileName(DateTimeOffset now) =>
        "venues-" + TimeZoneInfo.ConvertTime(now, ZoneBook.System).ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture) + ".txt";
}

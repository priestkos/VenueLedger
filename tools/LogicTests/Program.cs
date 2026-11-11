using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VenueLedger;

namespace VenueLedger.LogicTests;

/// <summary>
/// Offline proof for the plugin's logic: no game, no Dalamud, no dev distrib. Run with
///   dotnet run --project tools/LogicTests -c Release
/// Exits non-zero if any check fails.
/// </summary>
internal static class Program
{
    private static int checks;
    private static int failures;

    private static int Main(string[] args)
    {
        if (args.Contains("--show"))
        {
            Demo();
            return 0;
        }

        Clocks();
        Formats();
        TravelCommands();
        Districts_();
        ZoneResolution();
        Sessions();
        DaylightSaving();
        CrossZoneClose();
        Statuses();
        BoardConversion();
        BoardDstDays();
        DaylightLabels();
        LinkSchemes();
        ExportText();
        ExportPaths();
        ConfigRepair();
        InvariantSweep();

        Console.WriteLine();
        Console.WriteLine(failures == 0
            ? $"PASS - {checks} checks"
            : $"FAIL - {failures} of {checks} checks failed");

        return failures == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------ helpers

    private static void Ok(bool condition, string what)
    {
        checks++;
        if (condition) return;

        failures++;
        Console.WriteLine("  FAIL  " + what);
    }

    private static void Eq<T>(T actual, T expected, string what)
    {
        checks++;
        if (EqualityComparer<T>.Default.Equals(actual, expected)) return;

        failures++;
        Console.WriteLine($"  FAIL  {what}: expected <{expected}>, got <{actual}>");
    }

    private static void Section(string name) => Console.WriteLine("[" + name + "]");

    /// <summary>Prints a real export from three demo venues - the fastest way to eyeball the output.</summary>
    private static void Demo()
    {
        var venues = new List<Venue>
        {
            new()
            {
                Name = "Jade Parlour",
                Location = "Ward 12, Plot 30, Lavender Beds, Mateus",
                District = Districts.LavenderBeds,
                World = "Mateus",
                Ward = 12,
                Plot = 30,
                Website = "https://jadeparlour.example",
                Discord = "https://discord.gg/jadeparlour",
                OpenTime = "20:00",
                CloseTime = "02:00",
                OpenZone = "Asia/Tokyo",
                CloseZone = "Asia/Tokyo",
                Notes = "Live music from 21:00 JST. No cover charge.",
            },
            new()
            {
                Name = "Crimson Hour",
                Location = "Ward 5, Plot 12, The Goblet, Siren",
                District = Districts.Goblet,
                World = "Siren",
                Ward = 5,
                Plot = 12,
                Website = "crimsonhour.example",
                Discord = "https://discord.gg/crimsonhour",
                OpenTime = "21:00",
                CloseTime = "01:00",
                OpenZone = "Asia/Kolkata",
                CloseZone = "Asia/Kolkata",
                Notes = "Dress code: black and gold.",
            },
            new()
            {
                Name = "Foxfire Lounge",
                Location = "Ward 22, Plot 8, Empyreum, Balmung",
                District = Districts.Empyreum,
                World = "Balmung",
                Ward = 22,
                Plot = 8,
                Website = "https://foxfire.example",
                Discord = "https://discord.gg/foxfire",
                OpenTime = "19:00",
                CloseTime = "23:30",
                OpenZone = "America/New_York",
                CloseZone = "America/New_York",
                Notes = "18+ venue. Bathhouse rules apply.",
            },
        };

        var board = Board.Build(venues, "", Now, true);
        Console.WriteLine(Exporter.Build(board, new ExportOptions()));
        Console.WriteLine("--- board table ---");
        foreach (var row in board.Rows)
        {
            Console.WriteLine($"{row.Venue.DisplayName,-16} {row.OwnOpenText}-{row.OwnCloseText} {row.OwnZoneText,-14} -> " +
                              $"{row.BoardOpenText}-{row.BoardCloseText}{row.BoardDayText,-4} {row.LengthText,-8} {row.StatusText}");
        }
    }

    private static readonly DateTimeOffset Now =
        new(2026, 10, 27, 11, 12, 0, TimeSpan.Zero); // 20:12 JST, 16:42 IST

    // ------------------------------------------------------------------ clocks

    private static void Clocks()
    {
        Section("clock input");

        foreach (var (text, minutes) in new (string, int)[]
                 {
                     ("20:00", 1200), ("8pm", 1200), ("8:30pm", 1230), ("2000", 1200), ("930", 570),
                     ("20", 1200), ("8 PM", 1200), ("00:00", 0), ("12am", 0), ("12pm", 720),
                     ("23:59", 1439), ("7:05am", 425), ("08.15", 495),
                 })
        {
            var ok = ClockText.TryParse(text, out var got, out var error);
            Ok(ok, $"\"{text}\" should parse ({error})");
            if (ok) Eq(got, minutes, $"\"{text}\" minutes");
        }

        foreach (var bad in new[] { "25:00", "8x", "", "  ", "1260", "12:75", "24:00", "1330pm" })
        {
            Ok(!ClockText.TryParse(bad, out _, out var error), $"\"{bad}\" should be rejected");
            Ok(error.Length > 0, $"\"{bad}\" should explain itself");
        }

        // \d matches every Unicode digit, but int.Parse only reads ASCII: an IME's full-width or another
        // script's digits must be refused with a message, never thrown out of a Draw call.
        foreach (var foreign in new[] { "\uFF12\uFF10:\uFF10\uFF10", "\uFF12\uFF10\uFF10\uFF10", "\u0662\u0660\u0660\u0660", "\uFF18pm" })
        {
            bool parsed;
            try
            {
                parsed = ClockText.TryParse(foreign, out _, out _);
            }
            catch (Exception ex)
            {
                Ok(false, $"\"{foreign}\" threw {ex.GetType().Name}");
                continue;
            }

            Ok(!parsed, $"\"{foreign}\" (non-ASCII digits) is refused");
        }

        // Every minute of the day round-trips through both formats.
        var roundtrips = 0;
        for (var minute = 0; minute < 1440; minute++)
        {
            if (ClockText.TryParse(ClockText.Format(minute, true), out var back, out _) && back == minute) roundtrips++;
        }

        Eq(roundtrips, 1440, "24-hour format round-trips every minute");

        var twelveHour = 0;
        for (var minute = 0; minute < 1440; minute++)
        {
            if (ClockText.TryParse(ClockText.Format(minute, false), out var back, out _) && back == minute) twelveHour++;
        }

        Eq(twelveHour, 1440, "12-hour format round-trips every minute");
    }

    private static void Formats()
    {
        Section("formatting");

        Eq(ClockText.Format(1200, true), "20:00", "20:00 in 24h");
        Eq(ClockText.Format(1200, false), "8:00 PM", "20:00 in 12h");
        Eq(ClockText.Format(0, false), "12:00 AM", "midnight in 12h");
        Eq(ClockText.Format(720, false), "12:00 PM", "noon in 12h");
        Eq(ClockText.Format(1440, true), "00:00", "1440 wraps to midnight");
        Eq(ClockText.Length(TimeSpan.FromHours(6)), "6h", "6h length");
        Eq(ClockText.Length(TimeSpan.FromMinutes(330)), "5h 30m", "5h30m length");
        Eq(ClockText.Length(TimeSpan.FromMinutes(45)), "45m", "45m length");
        Eq(ClockText.Humanize(TimeSpan.FromMinutes(252)), "4h 12m", "humanize hours");
        Eq(ClockText.Humanize(TimeSpan.FromDays(2) + TimeSpan.FromHours(3)), "2d 3h", "humanize days");
        Eq(ClockText.Humanize(TimeSpan.FromMinutes(12)), "12m", "humanize minutes");
    }

    // ------------------------------------------------------------------ travel

    private static void TravelCommands()
    {
        Section("lifestream commands");

        var full = new Venue
        {
            Name = "Jade Parlour",
            District = Districts.LavenderBeds,
            World = "Mateus",
            Ward = 12,
            Plot = 30,
        };
        Eq(Travel.Command(full), "/li mateus lavender 12 30", "world + district + plot");

        var homeWorld = new Venue { District = Districts.Goblet, Ward = 1, Plot = 30, World = "" };
        Eq(Travel.Command(homeWorld), "/li goblet 1 30", "current-world form matches Lifestream's own example");

        Eq(Travel.Command(new Venue { District = Districts.Mist, Ward = 3, Plot = 7, World = "  Siren  " }),
            "/li siren mist 3 7", "world is lowercased and trimmed");

        Ok(Travel.Command(new Venue { District = Districts.Shirogane, Ward = 0, Plot = 9 }) is null,
            "no ward means no command");
        Ok(Travel.Command(new Venue { District = Districts.Empyreum, Ward = 5, Plot = 0 }) is null,
            "no plot means no command");
        Ok(Travel.Command(new Venue { District = "", Ward = 5, Plot = 9 }) is null,
            "no district means no command");

        var locationOnly = new Venue { Location = "The Carline Canopy, New Gridania" };
        Ok(Travel.Command(locationOnly) is null, "a free-text location alone cannot produce a command");
        Eq(Travel.Preview(locationOnly), "The Carline Canopy, New Gridania", "preview falls back to the location");
        Ok(Travel.Preview(new Venue()).StartsWith("Add a district", StringComparison.Ordinal),
            "empty venue explains what the button needs");

        Eq(Travel.NormalizeWorld("Mateus"), "mateus", "world normalization");
        Eq(Travel.NormalizeWorld("  Siren "), "siren", "world trim");
    }

    private static void Districts_()
    {
        Section("districts");

        Eq(Districts.Match("lavender"), Districts.LavenderBeds, "partial district match");
        Eq(Districts.Match("the goblet"), Districts.Goblet, "district match with article");
        Eq(Districts.Match("SHIROGANE"), Districts.Shirogane, "district match case-insensitive");
        Ok(Districts.Match("limsa") is null, "unknown district stays unset");

        Eq(Districts.Keyword(Districts.LavenderBeds), "lavender", "lavender keyword");
        Eq(Districts.Keyword(Districts.Goblet), "goblet", "goblet keyword");
        Eq(Districts.Keyword(Districts.Mist), "mist", "mist keyword");
        Eq(Districts.Keyword(Districts.Shirogane), "shirogane", "shirogane keyword");
        Eq(Districts.Keyword(Districts.Empyreum), "empyreum", "empyreum keyword");
    }

    // ------------------------------------------------------------------ zones

    private static void ZoneResolution()
    {
        Section("timezones");

        Ok(ZoneBook.ResolveOrNull("Asia/Tokyo") is not null, "IANA id resolves");
        Ok(ZoneBook.ResolveOrNull("Tokyo Standard Time") is not null, "Windows id resolves");
        Ok(ZoneBook.ResolveOrNull("Mars/Olympus") is null, "unknown id returns null");
        Eq(ZoneBook.Resolve("").Id, TimeZoneInfo.Local.Id, "empty id means system local");
        Eq(ZoneBook.Resolve("  ").Id, TimeZoneInfo.Local.Id, "whitespace id means system local");
        Eq(ZoneBook.Resolve("Mars/Olympus").Id, TimeZoneInfo.Local.Id, "unknown id falls back to local");

        var tokyo = ZoneBook.Resolve("Asia/Tokyo");
        Eq(ZoneBook.Label(tokyo), "JST", "Tokyo abbreviation");
        Eq(ZoneBook.IanaId(tokyo), "Asia/Tokyo", "Tokyo IANA id");
        Eq(ZoneBook.OffsetText(tokyo, Now), "+09:00", "Tokyo offset text");

        var kolkata = ZoneBook.Resolve("Asia/Kolkata");
        Eq(ZoneBook.Label(kolkata), "IST", "Kolkata abbreviation");
        Eq(ZoneBook.OffsetText(kolkata, Now), "+05:30", "half-hour offset");

        var newYork = ZoneBook.Resolve("America/New_York");
        Eq(ZoneBook.OffsetText(newYork, Now), "-04:00", "New York is on EDT in late October");
        Eq(ZoneBook.OffsetText(newYork, new DateTimeOffset(2027, 1, 15, 12, 0, 0, TimeSpan.Zero)), "-05:00",
            "New York is on EST in January");

        Eq(ZoneBook.SameZone(tokyo, ZoneBook.Resolve("Asia/Tokyo")), true, "same zone compares by id");
        Eq(ZoneBook.SameZone(tokyo, kolkata), false, "different zones do not compare");

        Ok(ZoneBook.SystemDescription(Now).Contains("UTC+", StringComparison.Ordinal),
            "system description always carries an offset");

        // Every zone in the common list must resolve on this machine.
        var unresolved = ZoneBook.Common.Where(id => ZoneBook.ResolveOrNull(id) is null).ToList();
        Eq(unresolved.Count, 0, "every common zone resolves (" + string.Join(", ", unresolved) + ")");
    }

    // ------------------------------------------------------------------ sessions

    private static Venue JstVenue(string open = "20:00", string close = "02:00") => new()
    {
        Name = "Jade Parlour",
        OpenTime = open,
        CloseTime = close,
        OpenZone = "Asia/Tokyo",
        CloseZone = "Asia/Tokyo",
    };

    private static void Sessions()
    {
        Section("sessions");

        var venue = JstVenue();
        Ok(Scheduler.TryReadHours(venue, out var open, out var close, out var openZone, out var closeZone, out var error),
            "hours read (" + error + ")");
        Eq(open, 1200, "20:00 opening minutes");
        Eq(close, 120, "02:00 closing minutes");
        Eq(ZoneBook.Label(openZone), "JST", "opening zone label");
        Eq(ZoneBook.SameZone(openZone, closeZone), true, "one zone for both clocks");

        var sessions = Scheduler.Sessions(venue, Now);
        Ok(sessions.Count >= 8, "a week of sessions is generated");
        Ok(sessions.All(s => s.Closes > s.Opens), "every session closes after it opens");
        Ok(sessions.All(s => Math.Abs(s.Length.TotalHours - 6) < 0.001), "20:00-02:00 is a 6 hour session");

        // 20:00 JST on 27 Oct 2026 is 11:00 UTC; closing is 02:00 JST on the 28th, i.e. 17:00 UTC.
        var tonight = sessions.First(s => s.Opens == new DateTimeOffset(2026, 10, 27, 11, 0, 0, TimeSpan.Zero));
        Eq(tonight.Closes, new DateTimeOffset(2026, 10, 27, 17, 0, 0, TimeSpan.Zero), "closing instant crosses midnight");

        // Same clock, no crossing.
        var day = Scheduler.Sessions(JstVenue("09:00", "17:00"), Now);
        Ok(day.All(s => Math.Abs(s.Length.TotalHours - 8) < 0.001), "09:00-17:00 is an 8 hour session");

        // Open == close is a full day, not a zero-length session.
        var allDay = Scheduler.Sessions(JstVenue("12:00", "12:00"), Now);
        Ok(allDay.All(s => Math.Abs(s.Length.TotalHours - 24) < 0.001), "12:00-12:00 is a 24 hour session");

        // Bad input keeps failing loudly but without throwing.
        var broken = new Venue { OpenTime = "25:00", CloseTime = "02:00" };
        Ok(!Scheduler.TryReadHours(broken, out _, out _, out _, out _, out var brokenError), "25:00 is refused");
        Ok(brokenError.Contains("Opening time", StringComparison.Ordinal), "the error names the field");
        Eq(Scheduler.Status(broken, Now).State, VenueState.Unknown, "broken hours read as unknown");
        Eq(Scheduler.Sessions(broken, Now).Count, 0, "broken hours yield no sessions");

        var badZone = new Venue { OpenTime = "20:00", CloseTime = "02:00", OpenZone = "Mars/Olympus" };
        Ok(!Scheduler.TryReadHours(badZone, out _, out _, out _, out _, out var zoneError), "unknown zone is refused");
        Ok(zoneError.Contains("Mars/Olympus", StringComparison.Ordinal), "the error names the zone");
    }

    private static void DaylightSaving()
    {
        Section("daylight saving");

        // New York springs forward at 02:00 on 14 March 2027: 02:30 does not exist.
        var springForward = new Venue
        {
            OpenTime = "02:30",
            CloseTime = "06:00",
            OpenZone = "America/New_York",
            CloseZone = "America/New_York",
        };

        var sessions = Scheduler.Sessions(springForward, new DateTimeOffset(2027, 3, 13, 12, 0, 0, TimeSpan.Zero));
        var shifted = sessions.FirstOrDefault(s => s.Opens.UtcDateTime.Date == new DateTime(2027, 3, 14));
        Ok(shifted is not null, "the DST day still produces a session");
        if (shifted is not null)
        {
            Ok(shifted.OpenShifted, "the skipped wall clock is flagged as shifted");
            var local = TimeZoneInfo.ConvertTime(shifted.Opens, ZoneBook.Resolve("America/New_York"));
            Eq(local.Hour, 3, "02:30 is nudged past the spring-forward gap");
            Ok(shifted.Length > TimeSpan.Zero, "the session still has a length");
        }

        // Autumn fall-back: 01:30 happens twice, and the session must still open exactly once a day.
        var fallBack = new Venue
        {
            OpenTime = "01:30",
            CloseTime = "05:00",
            OpenZone = "America/New_York",
            CloseZone = "America/New_York",
        };

        var autumn = Scheduler.Sessions(fallBack, new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));
        var distinctOpenDays = autumn.Select(s => s.Opens.UtcDateTime.Date).Distinct().Count();
        Eq(distinctOpenDays, autumn.Count, "one session per opening day across the autumn change");

        // Lord Howe keeps a 30 minute DST shift - a good hostile case for the nudge loop.
        var lordHowe = new Venue
        {
            OpenTime = "02:00",
            CloseTime = "04:00",
            OpenZone = "Australia/Lord_Howe",
            CloseZone = "Australia/Lord_Howe",
        };

        var lh = Scheduler.Sessions(lordHowe, new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
        Ok(lh.All(s => s.Closes > s.Opens), "Lord Howe sessions stay positive across its 30 minute shift");
    }

    private static void CrossZoneClose()
    {
        Section("two zones per venue");

        // Opens 20:00 JST, closes 05:00 IST - the closing clock is in a different zone on purpose.
        var venue = new Venue
        {
            OpenTime = "20:00",
            CloseTime = "05:00",
            OpenZone = "Asia/Tokyo",
            CloseZone = "Asia/Kolkata",
        };

        var session = Scheduler.Sessions(venue, Now)
            .First(s => s.Opens == new DateTimeOffset(2026, 10, 27, 11, 0, 0, TimeSpan.Zero));

        // 20:00 JST = 11:00 UTC; 05:00 IST the next morning = 23:30 UTC -> 12h30m.
        Eq(session.Closes, new DateTimeOffset(2026, 10, 27, 23, 30, 0, TimeSpan.Zero), "close instant in the close zone");
        Eq(Math.Round(session.Length.TotalHours, 2), 12.5, "cross-zone session length");

        var board = Board.Build(new List<Venue> { venue }, "", Now, true);
        Eq(board.Rows[0].OwnZoneText, "JST -> IST", "the row names both zones");
        Ok(board.Rows[0].Known, "cross-zone hours convert cleanly");
    }

    // ------------------------------------------------------------------ status

    private static void Statuses()
    {
        Section("open / closed");

        var venue = JstVenue(); // 20:00-02:00 JST

        // 20:12 JST -> open, closes in 5h 48m.
        var open = Scheduler.Status(venue, Now);
        Eq(open.State, VenueState.Open, "open at 20:12 JST");
        Eq(ClockText.Humanize(open.Remaining), "5h 48m", "closes in 5h 48m");

        // 12:00 JST -> closed, next opening tonight.
        var midday = new DateTimeOffset(2026, 10, 27, 3, 0, 0, TimeSpan.Zero); // 12:00 JST
        var closed = Scheduler.Status(venue, midday);
        Eq(closed.State, VenueState.Closed, "closed at noon JST");
        Eq(TimeZoneInfo.ConvertTime(closed.Session!.Opens, ZoneBook.Resolve("Asia/Tokyo")).Hour, 20, "next opening is 20:00 JST");

        // 02:00 JST is closing time: the door is shut at the boundary, not still open.
        var boundary = new DateTimeOffset(2026, 10, 26, 17, 0, 0, TimeSpan.Zero); // 02:00 JST 27 Oct
        Eq(Scheduler.Status(venue, boundary).State, VenueState.Closed, "closing time is already closed");

        // 19:59:59 JST is the last closed second.
        var oneSecondBefore = new DateTimeOffset(2026, 10, 27, 10, 59, 59, TimeSpan.Zero);
        Eq(Scheduler.Status(venue, oneSecondBefore).State, VenueState.Closed, "one second before opening is closed");

        // 20:00:00 JST is open.
        Eq(Scheduler.Status(venue, new DateTimeOffset(2026, 10, 27, 11, 0, 0, TimeSpan.Zero)).State,
            VenueState.Open, "opening instant is open");

        var never = new Venue { OpenTime = "20:00", CloseTime = "02:00", OpenZone = "Mars/Olympus" };
        Eq(Scheduler.Status(never, Now).State, VenueState.Unknown, "unknown zone is not guessed");
    }

    // ------------------------------------------------------------------ board

    private static void BoardConversion()
    {
        Section("board conversion");

        var jst = JstVenue();                       // 20:00-02:00 JST
        var ist = new Venue
        {
            Name = "Kolkata Lounge",
            OpenTime = "21:00",
            CloseTime = "01:00",
            OpenZone = "Asia/Kolkata",
            CloseZone = "Asia/Kolkata",
        };

        var board = Board.Build(new List<Venue> { jst, ist }, "Asia/Kolkata", Now, true);
        Eq(ZoneBook.Label(board.BoardZone), "IST", "board zone is the requested one");
        Eq(board.Rows.Count, 2, "both venues are on the board");
        Eq(board.Rows[0].BoardOpenText, "16:30", "20:00 JST is 16:30 IST");
        Eq(board.Rows[0].BoardCloseText, "22:30", "02:00 JST is 22:30 IST");
        Eq(board.Rows[0].BoardDayText, string.Empty, "same board day");
        Eq(board.Rows[0].LengthText, "6h", "length survives conversion");

        // 21:00 IST -> 00:30 JST the next morning.
        var tokyoBoard = Board.Build(new List<Venue> { ist }, "Asia/Tokyo", Now, true);
        Eq(tokyoBoard.Rows[0].BoardOpenText, "00:30", "21:00 IST is 00:30 JST");
        Eq(tokyoBoard.Rows[0].BoardCloseText, "04:30", "01:00 IST is 04:30 JST");
        Eq(tokyoBoard.Rows[0].BoardDayText, string.Empty, "that session still ends on the same JST day");

        // A session that crosses midnight in the board zone is marked +1d.
        var jstBoard = Board.Build(new List<Venue> { jst }, "Asia/Tokyo", Now, true);
        Eq(jstBoard.Rows[0].BoardOpenText, "20:00", "20:00 JST stays 20:00 on the JST board");
        Eq(jstBoard.Rows[0].BoardCloseText, "02:00", "02:00 JST stays 02:00 on the JST board");
        Eq(jstBoard.Rows[0].BoardDayText, "+1d", "a midnight-crossing session is marked +1d");

        // Open-now count and ordering: JST venue opens 16:30 IST, IST venue opens 21:00 IST.
        var evening = new DateTimeOffset(2026, 10, 27, 14, 0, 0, TimeSpan.Zero); // 19:30 IST
        var eveningBoard = Board.Build(new List<Venue> { jst, ist }, "Asia/Kolkata", evening, true);
        Eq(eveningBoard.OpenNow, 1, "only the JST venue is open at 19:30 IST");
        Ok(eveningBoard.Rows[0].IsOpen, "the JST venue is the open one");

        // Nothing open: both unknown.
        var unknownBoard = Board.Build(new List<Venue> { new Venue { OpenTime = "nope" } }, "", Now, true);
        Eq(unknownBoard.Unknown, 1, "a broken row is counted as unknown");
        Eq(unknownBoard.OpenNow, 0, "a broken row is never open");
        Eq(unknownBoard.Rows[0].StatusText, "hours unknown", "broken rows say so");

        // Segments must sit inside the board day.
        var segments = eveningBoard.Rows.SelectMany(r => r.Segments).ToList();
        Ok(segments.Count > 0, "the day strip has something to draw");
        Ok(segments.All(s => s.Start >= eveningBoard.DayStart && s.End <= eveningBoard.DayEnd),
            "segments are clipped to the board day");
        Ok(segments.All(s => s.End > s.Start), "segments are never empty");

        // 12 hour board reads 12-hour clock text.
        var twelve = Board.Build(new List<Venue> { jst }, "Asia/Kolkata", Now, false);
        Eq(twelve.Rows[0].BoardOpenText, "4:30 PM", "12-hour board clock");
    }

    /// <summary>The board day runs midnight to midnight in the board zone, so DST change days are 25 or 23 hours.</summary>
    private static void BoardDstDays()
    {
        Section("board day across DST");

        var lateNight = new Venue
        {
            Name = "Night Owl",
            OpenTime = "20:00",
            CloseTime = "02:00",
            OpenZone = "America/New_York",
            CloseZone = "America/New_York",
        };

        var fallBack = Board.Build(new List<Venue> { lateNight }, "America/New_York",
            new DateTimeOffset(2026, 11, 1, 15, 0, 0, TimeSpan.Zero), true);
        Eq(fallBack.DayStart, new DateTimeOffset(2026, 11, 1, 4, 0, 0, TimeSpan.Zero), "NY 2026-11-01 starts at 04:00Z (EDT midnight)");
        Eq(fallBack.DayEnd, new DateTimeOffset(2026, 11, 2, 5, 0, 0, TimeSpan.Zero), "NY 2026-11-01 ends at 05:00Z (EST midnight)");
        Eq((fallBack.DayEnd - fallBack.DayStart).TotalHours, 25d, "fall-back day is 25 hours");
        Ok(fallBack.Rows[0].Segments.Any(s => s.End == fallBack.DayEnd),
            "the 20:00 session runs to the strip's real midnight, not an hour short");

        var springForward = Board.Build(new List<Venue> { lateNight }, "America/New_York",
            new DateTimeOffset(2026, 3, 8, 15, 0, 0, TimeSpan.Zero), true);
        Eq(springForward.DayStart, new DateTimeOffset(2026, 3, 8, 5, 0, 0, TimeSpan.Zero), "NY 2026-03-08 starts at 05:00Z (EST midnight)");
        Eq(springForward.DayEnd, new DateTimeOffset(2026, 3, 9, 4, 0, 0, TimeSpan.Zero), "NY 2026-03-08 ends at 04:00Z (EDT midnight)");
        Eq((springForward.DayEnd - springForward.DayStart).TotalHours, 23d, "spring-forward day is 23 hours");

        var normal = Board.Build(new List<Venue> { lateNight }, "America/New_York",
            new DateTimeOffset(2026, 10, 27, 15, 0, 0, TimeSpan.Zero), true);
        Eq(normal.DayStart, new DateTimeOffset(2026, 10, 27, 4, 0, 0, TimeSpan.Zero), "a normal NY day still starts at local midnight");
        Eq((normal.DayEnd - normal.DayStart).TotalHours, 24d, "a normal day is 24 hours");

        var ny = ZoneBook.Resolve("America/New_York");
        Eq(ZoneBook.AtLocal(new DateTime(2026, 11, 1, 1, 30, 0), ny), new DateTimeOffset(2026, 11, 1, 5, 30, 0, TimeSpan.Zero),
            "a repeated 01:30 is its first (EDT) occurrence");
        Eq(ZoneBook.AtLocal(new DateTime(2026, 3, 8, 2, 30, 0), ny), new DateTimeOffset(2026, 3, 8, 7, 30, 0, TimeSpan.Zero),
            "a skipped 02:30 lands just after the gap");
    }

    private static void DaylightLabels()
    {
        Section("daylight labels");

        var july = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
        var january = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var london = ZoneBook.Resolve("Europe/London");
        var sydney = ZoneBook.Resolve("Australia/Sydney");
        var newYork = ZoneBook.Resolve("America/New_York");

        Eq(ZoneBook.Label(london, july), "BST", "London in July is BST");
        Eq(ZoneBook.Label(london, january), "GMT", "London in January is GMT");
        Eq(ZoneBook.Label(sydney, january), "AEDT", "Sydney in January is AEDT");
        Eq(ZoneBook.Label(sydney, july), "AEST", "Sydney in July is AEST");
        Eq(ZoneBook.Label(newYork, july), "ET", "generic ET needs no summer form");
        Ok(ZoneBook.Describe(london, july).StartsWith("BST", StringComparison.Ordinal), "Describe uses the instant's label");
    }

    private static void LinkSchemes()
    {
        Section("link schemes");

        Eq(Links.Normalize("venue.example"), "https://venue.example/", "a bare host gains https://");
        Eq(Links.Normalize(" http://venue.example/menu "), "http://venue.example/menu", "http passes");
        Eq(Links.Normalize("discord://discord.com/invite/abc"), "discord://discord.com/invite/abc", "discord:// passes");
        Eq(Links.Normalize("file:///C:/Windows/notepad.exe"), null, "file:// is refused");
        Eq(Links.Normalize("httpx:payload"), null, "an 'http'-prefixed non-URL is refused");
        Eq(Links.Normalize("javascript:alert(1)"), null, "javascript: is refused");
        Eq(Links.Normalize("   "), null, "blank is refused");
    }

    private static void ExportPaths()
    {
        Section("export paths");

        Eq(Exporter.FileName(new DateTimeOffset(2026, 10, 27, 9, 5, 7, TimeSpan.Zero)).Length,
            "venues-yyyyMMdd-HHmmss.txt".Length, "file name carries seconds");

        var dir = System.IO.Directory.CreateTempSubdirectory("venueledger-").FullName;
        try
        {
            var first = Exporter.UniquePath(dir, "venues-x.txt");
            Eq(System.IO.Path.GetFileName(first), "venues-x.txt", "a free name is used as-is");
            System.IO.File.WriteAllText(first, "1");
            var second = Exporter.UniquePath(dir, "venues-x.txt");
            Eq(System.IO.Path.GetFileName(second), "venues-x-2.txt", "a taken name gets -2");
            System.IO.File.WriteAllText(second, "2");
            Eq(System.IO.Path.GetFileName(Exporter.UniquePath(dir, "venues-x.txt")), "venues-x-3.txt", "then -3");
            Eq(System.IO.File.ReadAllText(first), "1", "the earlier export is untouched");
        }
        finally
        {
            System.IO.Directory.Delete(dir, true);
        }
    }

    // ------------------------------------------------------------------ config

    private static void ConfigRepair()
    {
        Section("config repair");

        var twin = new Venue { Id = "same", Name = "A" };
        var config = new Configuration
        {
            Venues = new List<Venue>
            {
                twin,
                null!,
                new() { Id = "same", Name = "B", District = "lavender", Ward = 99, Plot = -3, Notes = null!, OpenZone = null! },
                new() { Id = "  ", District = "limsa" },
            },
            SelectedId = "gone",
            BoardZone = null!,
        };
        config.Normalize();

        Eq(config.Venues.Count, 3, "null venues are dropped");
        Eq(config.Venues.Select(v => v.Id).Distinct().Count(), 3, "duplicate and blank ids are replaced");
        Eq(config.Venues[0].Id, "same", "the first holder of an id keeps it");
        Eq(config.Venues[1].District, Districts.LavenderBeds, "a hand-typed district folds onto the canonical name");
        Eq(config.Venues[2].District, string.Empty, "an unknown district is cleared");
        Eq(config.Venues[1].Ward, 30, "ward clamps to 30");
        Eq(config.Venues[1].Plot, 0, "a negative plot clamps to unset");
        Eq(config.Venues[1].Notes, string.Empty, "null text becomes empty");
        Eq(config.Venues[1].OpenZone, string.Empty, "null zone becomes system");
        Eq(config.SelectedId, "same", "a dangling selection moves to the first venue");
        Eq(config.BoardZone, string.Empty, "null board zone becomes system");

        var empty = new Configuration { Venues = null! };
        empty.Normalize();
        Eq(empty.Venues.Count, 0, "a missing venue list is rebuilt");
        Eq(empty.SelectedId, string.Empty, "nothing selected in an empty ledger");

        // Deleting moves the selection to the row that slid into place, or the new last row.
        var ledger = new Configuration { NewVenueZone = "Asia/Tokyo" };
        var a = ledger.AddVenue();
        var b = ledger.AddVenue();
        var c = ledger.AddVenue();
        Eq(ledger.SelectedId, c.Id, "a new venue is selected");
        Eq(a.OpenZone + "|" + a.CloseZone, "Asia/Tokyo|Asia/Tokyo", "a new venue takes the default zone for both clocks");
        ledger.Remove(b);
        Eq(ledger.SelectedId, c.Id, "deleting a middle row selects the next one");
        ledger.Remove(c);
        Eq(ledger.SelectedId, a.Id, "deleting the last row selects the new last row");
        ledger.Remove(new Venue());
        Eq(ledger.Venues.Count, 1, "removing a venue not in the ledger is a no-op");
        ledger.Remove(a);
        Eq(ledger.SelectedId, string.Empty, "deleting the only venue clears the selection");

        var copy = twin.Copy();
        Ok(copy.Id != twin.Id && copy.Name == "A (copy)" && copy.OpenTime == twin.OpenTime, "duplicate gets a fresh id and keeps the rest");
    }

    // ------------------------------------------------------------------ export

    private static void ExportText()
    {
        Section("export");

        var venue = JstVenue();
        venue.Location = "Ward 12, Plot 30, Lavender Beds, Mateus";
        venue.District = Districts.LavenderBeds;
        venue.World = "Mateus";
        venue.Ward = 12;
        venue.Plot = 30;
        venue.Website = "https://jade.example";
        venue.Discord = "https://discord.gg/jade";
        venue.Notes = "Live music from 21:00.\nNo cover charge.";
        venue.Name = "Jade Parlour";

        var board = Board.Build(new List<Venue> { venue }, "Asia/Kolkata", Now, true);
        var text = Exporter.Build(board, new ExportOptions());

        Ok(text.StartsWith("VENUE LEDGER - 1 venue", StringComparison.Ordinal), "export starts with the title and count");
        Ok(text.Contains("Board clock: IST | Asia/Kolkata UTC+05:30", StringComparison.Ordinal) ||
           text.Contains("Board clock: IST · Asia/Kolkata UTC+05:30", StringComparison.Ordinal),
            "export names the board clock");
        Ok(text.Contains("1. Jade Parlour", StringComparison.Ordinal), "the venue is numbered");
        Ok(text.Contains("OPEN - CLOSES IN 5H 48M", StringComparison.OrdinalIgnoreCase),
            "live status is in the header line");
        Ok(text.Contains("Location  Ward 12, Plot 30, Lavender Beds, Mateus", StringComparison.Ordinal), "location line");
        Ok(text.Contains("Travel    /li mateus lavender 12 30", StringComparison.Ordinal), "travel line");
        Ok(text.Contains("Hours     20:00-02:00 JST  ->  16:30-22:30 IST", StringComparison.Ordinal), "hours line");
        Ok(text.Contains("(6h)", StringComparison.Ordinal), "length in the hours line");
        Ok(text.Contains("Website   https://jade.example", StringComparison.Ordinal), "website line");
        Ok(text.Contains("Discord   https://discord.gg/jade", StringComparison.Ordinal), "discord line");
        Ok(text.Contains("Notes     Live music from 21:00. No cover charge.", StringComparison.Ordinal),
            "notes are folded onto one line");

        // The export must stay one field per line: a note's own newline may not start a new line.
        var warningLines = text.Split('\n').Count(line => line.Contains("No cover charge", StringComparison.Ordinal));
        Eq(warningLines, 1, "a multi-line note never breaks the export layout");

        var withoutNotes = Exporter.Build(board, new ExportOptions { IncludeNotes = false, IncludeTravel = false });
        Ok(!withoutNotes.Contains("Travel", StringComparison.Ordinal), "travel can be left out");
        Ok(!withoutNotes.Contains("Notes", StringComparison.Ordinal), "notes can be left out");
        Ok(withoutNotes.Contains("Hours", StringComparison.Ordinal), "hours are never optional");

        var twelve = Exporter.Build(Board.Build(new List<Venue> { venue }, "Asia/Kolkata", Now, false),
            new ExportOptions());
        Ok(twelve.Contains("8:00 PM-2:00 AM JST", StringComparison.Ordinal), "12-hour export");

        // The board zone's label is read at the session it labels, not at export time: a London venue
        // opening after the October change must read GMT even when exported on the BST side of it.
        var earlyBird = new Venue
        {
            Name = "Early Bird",
            OpenTime = "03:00",
            CloseTime = "05:00",
            OpenZone = "Europe/London",
            CloseZone = "Europe/London",
        };
        var beforeChange = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero); // 13:00 BST, change is 25 Oct 01:00Z
        var acrossDst = Exporter.Build(Board.Build(new List<Venue> { earlyBird }, "Europe/London", beforeChange, true),
            new ExportOptions());
        Ok(acrossDst.Contains("03:00-05:00 GMT  ->  03:00-05:00 GMT", StringComparison.Ordinal),
            "board label is taken at the session's own instant (GMT after the change)");

        // An empty ledger still produces a valid, honest document.
        var empty = Exporter.Build(Board.Build(new List<Venue>(), "", Now, true), new ExportOptions());
        Ok(empty.Contains("0 venues", StringComparison.Ordinal), "empty export says zero venues");
        Ok(!empty.Contains("Open right now", StringComparison.Ordinal), "empty export claims nothing is open");

        var fileName = Exporter.FileName(Now);
        Ok(fileName.StartsWith("venues-", StringComparison.Ordinal) && fileName.EndsWith(".txt", StringComparison.Ordinal),
            "export file name shape: " + fileName);
        Ok(fileName.IndexOfAny(new[] { ':', '/', '\\', ' ' }) < 0, "export file name has no path-hostile characters");
    }

    // ------------------------------------------------------------------ sweep

    /// <summary>
    /// Every combination of the awkward clocks and zones the UI can produce, checked against the
    /// invariants the UI relies on: a session always has a length, always closes after it opens, and
    /// always lands within a day and a bit of the opening clock.
    /// </summary>
    private static void InvariantSweep()
    {
        Section("invariant sweep");

        var zones = new[] { "Asia/Tokyo", "Asia/Kolkata", "America/New_York", "Europe/London", "Pacific/Auckland", "UTC" };
        var times = new[] { "00:00", "01:30", "02:30", "09:00", "12:00", "20:00", "23:59" };

        var cases = 0;
        var bad = 0;
        var maxLength = TimeSpan.Zero;
        var minLength = TimeSpan.MaxValue;

        foreach (var openZone in zones)
        foreach (var closeZone in zones)
        foreach (var open in times)
        foreach (var close in times)
        {
            var venue = new Venue
            {
                OpenTime = open,
                CloseTime = close,
                OpenZone = openZone,
                CloseZone = closeZone,
            };

            var sessions = Scheduler.Sessions(venue, Now, 0, 3);
            if (sessions.Count == 0)
            {
                bad++;
                continue;
            }

            foreach (var session in sessions)
            {
                cases++;

                if (session.Closes <= session.Opens) bad++;
                if (session.Length < TimeSpan.Zero) bad++;

                if (session.Length > maxLength) maxLength = session.Length;
                if (session.Length < minLength) minLength = session.Length;

                // The instant must be the same instant in every zone, by definition.
                var back = TimeZoneInfo.ConvertTime(session.Opens, ZoneBook.System);
                if (Math.Abs((back - session.Opens).TotalSeconds) > 1) bad++;
            }

            var status = Scheduler.Status(venue, Now);
            if (!status.Known) bad++;
        }

        Eq(bad, 0, "every zone/time combination produces a sane session");
        Ok(cases > 3000, "the sweep actually covered ground (" + cases + " sessions)");
        Ok(maxLength <= TimeSpan.FromHours(26), "the longest session stays under 26h (was " + maxLength + ")");
        Ok(minLength >= TimeSpan.FromMinutes(1), "the shortest session is at least a minute (was " + minLength + ")");

        // now-in-zone is stable: converting twice must not drift.
        var drift = 0;
        var now = DateTimeOffset.UtcNow;
        foreach (var id in zones)
        {
            var zone = ZoneBook.Resolve(id);
            var once = TimeZoneInfo.ConvertTime(now, zone);
            var backAgain = TimeZoneInfo.ConvertTime(once, ZoneBook.System);
            if ((backAgain - now).Duration() > TimeSpan.FromSeconds(1)) drift++;
        }

        Eq(drift, 0, "zone conversion is idempotent");

        // The ledger list used to recompute each venue's status and "your time" text itself; it now reads the
        // board row. Old formula kept here and compared over the whole sweep, both clocks, three board zones.
        var all = new List<Venue>();
        foreach (var openZone in zones)
        foreach (var closeZone in zones)
        foreach (var open in times)
        foreach (var close in times)
            all.Add(new Venue { OpenTime = open, CloseTime = close, OpenZone = openZone, CloseZone = closeZone });
        all.Add(new Venue { OpenTime = "nope" });

        var compared = 0;
        var mismatched = 0;
        foreach (var boardId in new[] { "", "Asia/Kolkata", "America/New_York" })
        foreach (var use24 in new[] { true, false })
        {
            var board = Board.Build(all, boardId, Now, use24);
            var boardZone = ZoneBook.Resolve(boardId);
            foreach (var row in board.Rows)
            {
                var status = Scheduler.Status(row.Venue, Now);
                var oldDetail = string.Empty;
                if (status.Known && status.Session is not null)
                {
                    var o = TimeZoneInfo.ConvertTime(status.Session.Opens, boardZone);
                    var c = TimeZoneInfo.ConvertTime(status.Session.Closes, boardZone);
                    oldDetail = ClockText.Format((int)o.TimeOfDay.TotalMinutes, use24) + "-" +
                                ClockText.Format((int)c.TimeOfDay.TotalMinutes, use24) + " your time";
                }

                var newDetail = row.Known ? row.BoardOpenText + "-" + row.BoardCloseText + " your time" : string.Empty;
                compared++;
                if (oldDetail != newDetail || status.State != row.Status.State || status.Remaining != row.Status.Remaining) mismatched++;
            }
        }

        Eq(mismatched, 0, "ledger list text from the board row matches the old per-venue recompute (" + compared + " compared)");
        Console.WriteLine("  ledger-list old/new comparisons: " + compared);
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace VenueLedger;

/// <summary>
/// One venue in the ledger. Deliberately free of every Dalamud type so this file compiles into the
/// offline harness (tools/LogicTests) and runs with no game, no Dalamud and no dev distrib.
/// </summary>
public sealed class Venue
{
    /// <summary>Stable across edits so the selected row survives a rename, reorder or reload.</summary>
    public string Id { get; set; } = NewId();

    public string Name { get; set; } = string.Empty;

    /// <summary>Free text shown to the reader, e.g. "Ward 12, Plot 30, Lavender Beds, Mateus".</summary>
    public string Location { get; set; } = string.Empty;

    // ---- optional structured address: this is what makes the /li command possible ----
    public string World { get; set; } = string.Empty;

    /// <summary>District display name, one of <see cref="Districts.Names"/>. Empty means unset.</summary>
    public string District { get; set; } = string.Empty;

    /// <summary>0 means unset.</summary>
    public int Ward { get; set; }

    /// <summary>0 means unset.</summary>
    public int Plot { get; set; }

    public string Website { get; set; } = string.Empty;
    public string Discord { get; set; } = string.Empty;

    /// <summary>Wall-clock text as typed ("20:00", "8pm", "2000"). Parsed on every read, never baked into a row.</summary>
    public string OpenTime { get; set; } = "20:00";

    /// <summary>IANA id when known ("Asia/Tokyo"), Windows id otherwise. Empty means the system timezone.</summary>
    public string OpenZone { get; set; } = string.Empty;

    public string CloseTime { get; set; } = "02:00";
    public string CloseZone { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 10);

    /// <summary>Never blank, so an empty row is still selectable in the list.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Untitled venue" : Name.Trim();

    public bool HasTravel => Travel.HasAddress(this);

    /// <summary>A copy gets a fresh id but keeps everything else, for "duplicate".</summary>
    public Venue Copy()
    {
        var copy = (Venue)MemberwiseClone();
        copy.Id = NewId();
        copy.Name = DisplayName + " (copy)";
        return copy;
    }
}

/// <summary>The five residential districts, and the keyword Lifestream expects for each.</summary>
public static class Districts
{
    public const string LavenderBeds = "Lavender Beds";
    public const string Goblet = "The Goblet";
    public const string Mist = "Mist";
    public const string Shirogane = "Shirogane";
    public const string Empyreum = "Empyreum";

    public static readonly IReadOnlyList<string> Names = new[] { LavenderBeds, Goblet, Mist, Shirogane, Empyreum };

    /// <summary>
    /// Lifestream's own help documents "/li lavender 1 30", "/li goblet 1 30", "/li mist 1 30", so the
    /// keyword is the district name without its article. Null when the district is unset/unknown.
    /// </summary>
    public static string? Keyword(string? district) => district switch
    {
        LavenderBeds => "lavender",
        Goblet => "goblet",
        Mist => "mist",
        Shirogane => "shirogane",
        Empyreum => "empyreum",
        _ => null,
    };

    /// <summary>Maps a partially typed or lowercase name onto the canonical display name.</summary>
    public static string? Match(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed)) return null;
        var needle = typed.Trim();
        foreach (var name in Names)
        {
            if (string.Equals(name, needle, StringComparison.OrdinalIgnoreCase)) return name;
        }

        foreach (var name in Names)
        {
            if (name.StartsWith(needle, StringComparison.OrdinalIgnoreCase)) return name;
            if (name.Contains(needle, StringComparison.OrdinalIgnoreCase)) return name;
        }

        return null;
    }
}

/// <summary>Builds the Lifestream chat command for a venue's address.</summary>
public static class Travel
{
    public static bool HasAddress(Venue venue) =>
        venue.Ward > 0 && venue.Plot > 0 && Districts.Keyword(venue.District) is not null;

    /// <summary>
    /// Lifestream syntax, taken from the plugin's own /li help text:
    ///   /li &lt;district&gt; &lt;ward&gt; &lt;plot&gt;              - current world
    ///   /li &lt;world&gt; &lt;district&gt; &lt;ward&gt; &lt;plot&gt;      - another world
    /// Null when the address is incomplete: a wrong command is worse than none.
    /// </summary>
    public static string? Command(Venue venue)
    {
        if (!HasAddress(venue)) return null;

        var keyword = Districts.Keyword(venue.District)!;
        var parts = new List<string> { "/li" };

        var world = NormalizeWorld(venue.World);
        if (world.Length > 0) parts.Add(world);

        parts.Add(keyword);
        parts.Add(venue.Ward.ToString(CultureInfo.InvariantCulture));
        parts.Add(venue.Plot.ToString(CultureInfo.InvariantCulture));
        return string.Join(' ', parts);
    }

    /// <summary>"Mateus" -&gt; "mateus". Worlds have no spaces, so only case and strays need clearing.</summary>
    public static string NormalizeWorld(string? world)
    {
        if (string.IsNullOrWhiteSpace(world)) return string.Empty;

        var sb = new StringBuilder(world.Length);
        foreach (var c in world.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>The human line under the field: what the button will actually put on the clipboard.</summary>
    public static string Preview(Venue venue)
    {
        var command = Command(venue);
        if (command is not null) return command;

        if (string.IsNullOrWhiteSpace(venue.Location))
            return "Add a district, ward and plot (or just a location) to get a travel command.";

        return venue.Location.Trim();
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Configuration;

namespace VenueLedger;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public List<Venue> Venues { get; set; } = new();

    /// <summary>Empty means: project every venue onto the system timezone - the same clock as the header.</summary>
    public string BoardZone { get; set; } = string.Empty;

    public bool Use24Hour { get; set; } = true;

    public bool IncludeTravelInExport { get; set; } = true;

    public bool IncludeNotesInExport { get; set; } = true;

    /// <summary>Remembered selection so reopening the window lands where the user left off.</summary>
    public string SelectedId { get; set; } = string.Empty;

    /// <summary>Default timezone for a brand-new venue: the system's own, read live.</summary>
    public string NewVenueZone { get; set; } = string.Empty;

    /// <summary>Repairs anything a hand-edited config file could break. Runs once at load.</summary>
    public void Normalize()
    {
        Venues ??= new List<Venue>();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var venue in Venues)
        {
            if (venue is null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(venue.Id) || !seen.Add(venue.Id))
            {
                venue.Id = Venue.NewId();
                seen.Add(venue.Id);
            }

            venue.Name ??= string.Empty;
            venue.Location ??= string.Empty;
            venue.World ??= string.Empty;
            venue.Website ??= string.Empty;
            venue.Discord ??= string.Empty;
            venue.Notes ??= string.Empty;
            venue.OpenTime ??= string.Empty;
            venue.CloseTime ??= string.Empty;
            venue.OpenZone ??= string.Empty;
            venue.CloseZone ??= string.Empty;

            // A district typed by hand ("lavender") is folded onto the canonical name so /li can build.
            var district = Districts.Match(venue.District);
            venue.District = district ?? string.Empty;

            venue.Ward = Math.Max(0, Math.Min(30, venue.Ward));
            venue.Plot = Math.Max(0, Math.Min(60, venue.Plot));
        }

        Venues = Venues.Where(v => v is not null).ToList();

        if (SelectedId is null || Venues.All(v => v.Id != SelectedId))
        {
            SelectedId = Venues.FirstOrDefault()?.Id ?? string.Empty;
        }

        BoardZone ??= string.Empty;
        NewVenueZone ??= string.Empty;
    }

    public Venue? Selected => Venues.FirstOrDefault(v => v.Id == SelectedId);

    public Venue AddVenue()
    {
        var venue = new Venue
        {
            OpenZone = NewVenueZone,
            CloseZone = NewVenueZone,
        };

        Venues.Add(venue);
        SelectedId = venue.Id;
        return venue;
    }

    public void Remove(Venue venue)
    {
        var index = Venues.IndexOf(venue);
        if (index < 0) return;

        Venues.RemoveAt(index);

        var next = Venues.Count == 0 ? null : Venues[Math.Min(index, Venues.Count - 1)];
        SelectedId = next?.Id ?? string.Empty;
    }

    public void Move(int from, int to)
    {
        if (from < 0 || from >= Venues.Count) return;
        if (to < 0 || to >= Venues.Count) return;
        if (from == to) return;

        var venue = Venues[from];
        Venues.RemoveAt(from);
        Venues.Insert(to, venue);
    }
}

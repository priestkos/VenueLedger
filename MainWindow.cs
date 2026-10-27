using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;

namespace VenueLedger;

/// <summary>
/// One window, three faces: the ledger (list + editor), the board (every venue projected onto one clock)
/// and the export (the exact text that will be pasted elsewhere).
/// </summary>
public sealed class MainWindow : Window, IDisposable
{
    private const int TabLedger = 0;
    private const int TabBoard = 1;
    private const int TabExport = 2;

    private const string WindowId = "###VenueLedgerMain";

    private const int NameMax = 64;
    private const int UrlMax = 256;
    private const int NoteMax = 1024;
    private const int TimeMax = 8;

    private readonly Configuration config;
    private readonly Action save;
    private readonly Action<string> copy;
    private readonly Func<string, string?> export;

    private static List<TimeZoneInfo>? allZones;

    private int tab = TabLedger;
    private string zoneFilter = string.Empty;
    private string filter = string.Empty;
    private bool dirty;
    private DateTime dirtySince;
    private bool confirmDelete;
    private string flash = string.Empty;
    private DateTime flashUntil;
    private string lastExportPath = string.Empty;

    public MainWindow(Configuration config, Action save, Action<string> copy, Func<string, string?> export)
        : base("Venue Ledger" + WindowId)
    {
        this.config = config;
        this.save = save;
        this.copy = copy;
        this.export = export;

        Flags = ImGuiWindowFlags.NoCollapse;
        Size = new Vector2(940, 660) * ImGuiHelpers.GlobalScale;
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(720, 480) * ImGuiHelpers.GlobalScale,
            MaximumSize = new Vector2(4096, 4096),
        };
    }

    public void Dispose()
    {
    }

    public override void OnClose() => Save();

    public override void Draw()
    {
        using var palette = KosMos.Push();
        using var style = KosMos.PushStyle();

        var now = DateTimeOffset.UtcNow;

        // Typing saves on blur, but a window closed mid-word must not lose the edit: flush a
        // short debounce as well, so the config on disk is never more than a second behind.
        if (dirty && (DateTime.UtcNow - dirtySince).TotalMilliseconds > 900)
        {
            Save();
        }

        var board = Board.Build(config.Venues, config.BoardZone, now, config.Use24Hour);

        DrawHeader(now, board);
        DrawTabs();

        switch (tab)
        {
            case TabBoard:
                DrawBoard(board);
                break;
            case TabExport:
                DrawExport(board);
                break;
            default:
                DrawLedger(board);
                break;
        }

        DrawFooter();
    }

    // ------------------------------------------------------------------ header

    private void DrawHeader(DateTimeOffset now, BoardView board)
    {
        var contentWidth = ImGui.GetContentRegionAvail().X;
        var local = TimeZoneInfo.ConvertTime(now, ZoneBook.System);
        var clock = local.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        KosMos.BigText("VENUE LEDGER", KosMos.InkSoft, 1.5f);

        var clockWidth = ImGui.CalcTextSize(clock).X * 2.15f;
        if (contentWidth > clockWidth + 80f)
        {
            ImGui.SameLine(contentWidth - clockWidth);
            KosMos.BigText(clock, KosMos.Ink, 2.15f);
        }
        else
        {
            ImGui.Spacing();
            KosMos.BigText(clock, KosMos.Ink, 2.15f);
        }

        var systemClock = "system clock: " + ZoneBook.SystemDescription(now);
        var compare = config.Venues.Count == 0
            ? "no venues yet"
            : config.Venues.Count.ToString(CultureInfo.InvariantCulture) +
              (config.Venues.Count == 1 ? " venue" : " venues") +
              " | compare in " + board.BoardZoneText;

        KosMos.Hint(compare + "  |  " + systemClock);

        KosMos.DottedRule(0.8f);
    }

    private void DrawTabs()
    {
        var contentWidth = ImGui.GetContentRegionAvail().X;

        ImGui.Spacing();
        TabButton("Ledger", TabLedger);
        ImGui.SameLine();
        TabButton("Board", TabBoard);
        ImGui.SameLine();
        TabButton("Export", TabExport);

        ImGui.SameLine(contentWidth - 56f * ImGuiHelpers.GlobalScale);

        var use24 = config.Use24Hour;
        if (ImGui.Checkbox("24h", ref use24))
        {
            config.Use24Hour = use24;
            Save();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Show every time on this board as 24-hour or 12-hour.");
        }

        ImGui.Spacing();
    }

    private void TabButton(string label, int index)
    {
        var active = tab == index;
        var text = active ? "[ " + label.ToUpperInvariant() + " ]" : label.ToUpperInvariant();

        using var button = ImRaii.PushColor(ImGuiCol.Button, KosMos.Fade(KosMos.Ground, 0f));
        using var hover = ImRaii.PushColor(ImGuiCol.ButtonHovered, KosMos.Raised);
        using var activeColor = ImRaii.PushColor(ImGuiCol.ButtonActive, KosMos.Raised);
        using var ink = ImRaii.PushColor(ImGuiCol.Text, active ? KosMos.InkSoft : KosMos.Dim);
        using var border = ImRaii.PushStyle(ImGuiStyleVar.FrameBorderSize, 0f);

        if (ImGui.Button(text))
        {
            tab = index;
            confirmDelete = false;
        }
    }

    // ------------------------------------------------------------------ ledger

    private void DrawLedger(BoardView board)
    {
        var avail = ImGui.GetContentRegionAvail();
        var listWidth = 264f * ImGuiHelpers.GlobalScale;
        var height = MathF.Max(160f, avail.Y - 44f * ImGuiHelpers.GlobalScale);

        using (var list = ImRaii.Child("##venuelist", new Vector2(listWidth, height), true))
        {
            if (list.Success) DrawVenueList();
        }

        ImGui.SameLine();

        using (var editor = ImRaii.Child("##venueeditor", new Vector2(0, height), true))
        {
            if (editor.Success) DrawEditor(board);
        }
    }

    private void DrawVenueList()
    {
        KosMos.Section("Ledger");

        if (config.Venues.Count == 0)
        {
            KosMos.Hint("No venues yet.");
            KosMos.Hint("Press New venue to start one.");
            return;
        }

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##filter", "filter", ref filter, 40);

        var now = DateTimeOffset.UtcNow;
        var shown = 0;

        foreach (var venue in config.Venues.ToList())
        {
            if (filter.Length > 0 &&
                venue.DisplayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                venue.Location.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            shown++;

            var status = Scheduler.Status(venue, now);
            var selected = config.SelectedId == venue.Id;

            var nameColor = !status.Known ? KosMos.Dim : status.IsOpen ? KosMos.Live : KosMos.Ink;
            using (ImRaii.PushColor(ImGuiCol.Text, nameColor))
            {
                if (ImGui.Selectable(venue.DisplayName + "##" + venue.Id, selected, ImGuiSelectableFlags.None,
                        new Vector2(0, 0)))
                {
                    config.SelectedId = venue.Id;
                    confirmDelete = false;
                    Save();
                }
            }

            var line = status.Known
                ? (status.IsOpen ? "open - closes in " : "closed - opens in ") + ClockText.Humanize(status.Remaining)
                : status.Error;

            var boardZone = ZoneBook.Resolve(config.BoardZone);
            var detail = string.Empty;
            if (status.Known && status.Session is not null)
            {
                var open = TimeZoneInfo.ConvertTime(status.Session.Opens, boardZone);
                var close = TimeZoneInfo.ConvertTime(status.Session.Closes, boardZone);
                detail = ClockText.Format((int)open.TimeOfDay.TotalMinutes, config.Use24Hour) + "-" +
                         ClockText.Format((int)close.TimeOfDay.TotalMinutes, config.Use24Hour) + " your time";
            }

            ImGui.Indent(10f * ImGuiHelpers.GlobalScale);
            KosMos.Hint(line);
            if (detail.Length > 0) KosMos.Hint(detail);
            ImGui.Unindent(10f * ImGuiHelpers.GlobalScale);
            ImGui.Spacing();
        }

        if (shown == 0 && filter.Length > 0)
        {
            KosMos.Hint("Nothing matches \"" + filter + "\".");
        }
    }

    private void DrawEditor(BoardView board)
    {
        var venue = config.Selected;
        if (venue is null)
        {
            KosMos.Section("Venue");
            KosMos.Hint("Select a venue on the left, or create one with New venue.");
            return;
        }

        var row = board.Rows.FirstOrDefault(r => r.Venue.Id == venue.Id);

        KosMos.Section("Venue");

        if (!ImGui.BeginTable("##venuefields", 2,
                ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.PadOuterX))
        {
            return;
        }

        ImGui.TableSetupColumn("##label", ImGuiTableColumnFlags.WidthFixed, 82f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("##field", ImGuiTableColumnFlags.WidthStretch);

        // ---- name
        FieldRow("Name", () =>
        {
            ImGui.SetNextItemWidth(-1);
            var name = venue.Name;
            if (ImGui.InputText("##name", ref name, NameMax))
            {
                venue.Name = name;
                MarkDirty();
            }

            CommitEdit();
        });

        // ---- location + travel command
        FieldRow("Location", () =>
        {
            ImGui.SetNextItemWidth(-1);
            var location = venue.Location;
            if (ImGui.InputTextWithHint("##location", "Ward 12, Plot 30, Lavender Beds, Mateus", ref location, UrlMax))
            {
                venue.Location = location;
                MarkDirty();
            }

            CommitEdit();
            DrawTravelLine(venue);
        });

        // ---- structured address
        FieldRow("Address", () =>
        {
            var scale = ImGuiHelpers.GlobalScale;

            ImGui.SetNextItemWidth(110f * scale);
            var world = venue.World;
            if (ImGui.InputTextWithHint("##world", "world", ref world, 32))
            {
                venue.World = world;
                MarkDirty();
            }

            CommitEdit();
            ImGui.SameLine();

            ImGui.SetNextItemWidth(140f * scale);
            var district = string.IsNullOrWhiteSpace(venue.District) ? "district" : venue.District;
            if (ImGui.BeginCombo("##district", district))
            {
                if (ImGui.Selectable("(none)##nodistrict", string.IsNullOrWhiteSpace(venue.District)))
                {
                    venue.District = string.Empty;
                    Save();
                }

                foreach (var option in Districts.Names)
                {
                    if (ImGui.Selectable(option + "##" + option, venue.District == option))
                    {
                        venue.District = option;
                        Save();
                    }
                }

                ImGui.EndCombo();
            }

            ImGui.SameLine();
            ImGui.SetNextItemWidth(64f * scale);
            var ward = venue.Ward;
            if (ImGui.InputInt("##ward", ref ward, 1, 5))
            {
                venue.Ward = Math.Clamp(ward, 0, 30);
                MarkDirty();
            }

            CommitEdit();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Ward 1-30. 0 means unknown.");

            ImGui.SameLine();
            ImGui.SetNextItemWidth(64f * scale);
            var plot = venue.Plot;
            if (ImGui.InputInt("##plot", ref plot, 1, 5))
            {
                venue.Plot = Math.Clamp(plot, 0, 60);
                MarkDirty();
            }

            CommitEdit();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Plot 1-60. 0 means unknown.");

            KosMos.Hint("District, ward and plot are optional - they are only used to build the travel command.");
        });

        // ---- website
        FieldRow("Website", () =>
        {
            ImGui.SetNextItemWidth(-90f * ImGuiHelpers.GlobalScale);
            var website = venue.Website;
            if (ImGui.InputTextWithHint("##website", "https://", ref website, UrlMax))
            {
                venue.Website = website;
                MarkDirty();
            }

            CommitEdit();
            ImGui.SameLine();
            if (ImGui.Button("Open##website")) OpenLink(venue.Website);

            ImGui.SameLine();
            if (ImGui.Button("Copy##website")) Copy(venue.Website, "Website copied.");
        });

        // ---- discord
        FieldRow("Discord", () =>
        {
            ImGui.SetNextItemWidth(-90f * ImGuiHelpers.GlobalScale);
            var discord = venue.Discord;
            if (ImGui.InputTextWithHint("##discord", "https://discord.gg/", ref discord, UrlMax))
            {
                venue.Discord = discord;
                MarkDirty();
            }

            CommitEdit();
            ImGui.SameLine();
            if (ImGui.Button("Open##discord")) OpenLink(venue.Discord);

            ImGui.SameLine();
            if (ImGui.Button("Copy##discord")) Copy(venue.Discord, "Discord invite copied.");
        });

        // ---- hours
        FieldRow("Opens", () => DrawHourRow(venue, true, row));

        FieldRow("Closes", () => DrawHourRow(venue, false, row));

        FieldRow("Schedule", () =>
        {
            if (row is null) return;

            if (!row.Known)
            {
                ImGui.TextColored(KosMos.Crimson, row.Error);
                return;
            }

            var boardZone = ZoneBook.Resolve(config.BoardZone);
            var text = row.OwnOpenText + "-" + row.OwnCloseText + " " + row.OwnZoneText +
                       "   ->   " + row.BoardOpenText + "-" + row.BoardCloseText +
                       (row.BoardDayText.Length > 0 ? row.BoardDayText : string.Empty) +
                       " " + ZoneBook.Label(boardZone) +
                       "   (" + row.LengthText + ")";

            ImGui.TextColored(KosMos.Gold, text);
            ImGui.TextColored(row.IsOpen ? KosMos.Live : KosMos.Dim, row.StatusText);
        });

        // ---- notes
        FieldRow("Notes", () =>
        {
            var notes = venue.Notes;
            if (ImGui.InputTextMultiline("##notes", ref notes, NoteMax, new Vector2(-1, 84f * ImGuiHelpers.GlobalScale)))
            {
                venue.Notes = notes;
                MarkDirty();
            }

            CommitEdit();
        });

        ImGui.EndTable();
    }

    private void DrawHourRow(Venue venue, bool open, VenueRow? row)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var text = open ? venue.OpenTime : venue.CloseTime;

        ImGui.SetNextItemWidth(78f * scale);
        if (ImGui.InputTextWithHint(open ? "##opentime" : "##closetime", "20:00", ref text, TimeMax))
        {
            if (open) venue.OpenTime = text;
            else venue.CloseTime = text;
            MarkDirty();
        }

        if (ImGui.IsItemDeactivatedAfterEdit()) Save();

        if (!ClockText.TryParse(text, out _, out var error) && ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(error);
        }

        ImGui.SameLine();
        DrawZoneCombo(open ? "##openzone" : "##closezone", venue, open);

        if (open && row is { Known: true })
        {
            ImGui.SameLine();
            ImGui.TextColored(KosMos.Dim, "(" + row.LengthText + " session)");
        }
    }

    private void DrawZoneCombo(string id, Venue venue, bool open)
    {
        var currentId = open ? venue.OpenZone : venue.CloseZone;
        var zone = ZoneBook.Resolve(currentId);
        var at = DateTimeOffset.UtcNow;
        var preview = (string.IsNullOrWhiteSpace(currentId) ? "System - " : string.Empty) + ZoneBook.Describe(zone, at);

        ImGui.SetNextItemWidth(272f * ImGuiHelpers.GlobalScale);
        ImGui.PushID(id);

        if (ImGui.BeginCombo("##zonecombo", preview))
        {
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##zonefilter", "filter zones", ref zoneFilter, 32);

            if (ImGui.Selectable("System - " + ZoneBook.SystemDescription(at) + "##system", string.IsNullOrWhiteSpace(currentId)))
            {
                if (open) venue.OpenZone = string.Empty;
                else venue.CloseZone = string.Empty;
                Save();
            }

            foreach (var common in ZoneBook.Common)
            {
                var candidate = ZoneBook.ResolveOrNull(common);
                if (candidate is null) continue;
                if (!ZoneMatches(zoneFilter, common, candidate)) continue;

                if (ImGui.Selectable(ZoneBook.Describe(candidate, at) + "##c_" + common,
                        string.Equals(ZoneBook.IanaId(zone), common, StringComparison.OrdinalIgnoreCase)))
                {
                    SetZone(venue, open, ZoneBook.IanaId(candidate));
                }
            }

            ImGui.Separator();

            foreach (var candidate in Zones())
            {
                var iana = ZoneBook.IanaId(candidate);
                if (!ZoneMatches(zoneFilter, iana, candidate)) continue;

                if (ImGui.Selectable(ZoneBook.Describe(candidate, at) + "##z_" + iana,
                        string.Equals(ZoneBook.IanaId(zone), iana, StringComparison.OrdinalIgnoreCase)))
                {
                    SetZone(venue, open, iana);
                }
            }

            ImGui.EndCombo();
        }
        else if (zoneFilter.Length > 0)
        {
            zoneFilter = string.Empty;
        }

        ImGui.PopID();
    }

    private static bool ZoneMatches(string filter, string iana, TimeZoneInfo zone)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        filter = filter.Trim();

        return iana.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               zone.Id.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               ZoneBook.Label(zone).Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<TimeZoneInfo> Zones()
    {
        if (allZones is not null) return allZones;

        var now = DateTimeOffset.UtcNow;
        allZones = TimeZoneInfo.GetSystemTimeZones()
            .OrderBy(z => z.GetUtcOffset(now))
            .ThenBy(z => ZoneBook.IanaId(z), StringComparer.OrdinalIgnoreCase)
            .ToList();

        return allZones;
    }

    private void SetZone(Venue venue, bool open, string id)
    {
        if (open) venue.OpenZone = id;
        else venue.CloseZone = id;

        zoneFilter = string.Empty;
        Save();
    }

    private void DrawTravelLine(Venue venue)
    {
        var command = Travel.Command(venue);
        var preview = Travel.Preview(venue);

        if (command is not null)
        {
            ImGui.TextColored(KosMos.Gold, preview);
        }
        else
        {
            KosMos.Hint(preview);
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(preview.Length == 0))
        {
            if (ImGui.Button("Copy travel##travel"))
            {
                Copy(preview, command is null ? "Location copied." : "Lifestream command copied.");
            }
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(command is null
                ? "Copies the location text. Add district, ward and plot for a /li command."
                : "Paste into chat: Lifestream teleports there.");
        }
    }

    private void FieldRow(string label, Action body)
    {
        ImGui.TableNextRow();

        ImGui.TableSetColumnIndex(0);
        ImGui.TextColored(KosMos.Dim, label);

        ImGui.TableSetColumnIndex(1);
        body();
    }

    // ------------------------------------------------------------------ board

    private void DrawBoard(BoardView board)
    {
        var avail = ImGui.GetContentRegionAvail();
        var height = MathF.Max(160f, avail.Y - 44f * ImGuiHelpers.GlobalScale);

        using var child = ImRaii.Child("##board", new Vector2(0, height), true);
        if (!child.Success) return;

        KosMos.Section("Board");
        KosMos.Hint("Every venue's hours projected onto " + board.BoardZoneText + ". The red line is now.");

        if (board.Rows.Count == 0)
        {
            KosMos.Hint("Nothing to show yet - add a venue in the Ledger tab.");
            return;
        }

        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp |
                                      ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.NoPadOuterX;

        if (!ImGui.BeginTable("##boardtable", 6, flags)) return;

        var scale = ImGuiHelpers.GlobalScale;
        ImGui.TableSetupColumn("Venue", ImGuiTableColumnFlags.WidthStretch, 1.5f);
        ImGui.TableSetupColumn("Hours", ImGuiTableColumnFlags.WidthFixed, 132f * scale);
        ImGui.TableSetupColumn("Your time", ImGuiTableColumnFlags.WidthFixed, 120f * scale);
        ImGui.TableSetupColumn("Length", ImGuiTableColumnFlags.WidthFixed, 56f * scale);
        ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthStretch, 1.2f);
        ImGui.TableSetupColumn("Today", ImGuiTableColumnFlags.WidthFixed, 170f * scale);
        ImGui.TableHeadersRow();

        foreach (var row in board.Rows.OrderByDescending(r => r.IsOpen).ThenBy(r => r.Status.Session?.Opens ?? DateTimeOffset.MaxValue))
        {
            ImGui.TableNextRow();
            ImGui.PushID(row.Venue.Id);

            ImGui.TableSetColumnIndex(0);
            ImGui.TextColored(row.IsOpen ? KosMos.Live : KosMos.Ink, row.Venue.DisplayName);

            ImGui.TableSetColumnIndex(1);
            if (row.Known)
            {
                ImGui.TextUnformatted(row.OwnOpenText + "-" + row.OwnCloseText + " " + row.OwnZoneText);
            }
            else
            {
                ImGui.TextColored(KosMos.Dim, "-");
            }

            ImGui.TableSetColumnIndex(2);
            if (row.Known)
            {
                ImGui.TextColored(KosMos.Gold, row.BoardOpenText + "-" + row.BoardCloseText +
                                               (row.BoardDayText.Length > 0 ? " " + row.BoardDayText : string.Empty));
            }
            else
            {
                ImGui.TextColored(KosMos.Dim, row.Error);
            }

            ImGui.TableSetColumnIndex(3);
            ImGui.TextColored(KosMos.Dim, row.Known ? row.LengthText : "-");

            ImGui.TableSetColumnIndex(4);
            ImGui.TextColored(row.IsOpen ? KosMos.Live : KosMos.Dim, row.StatusText);

            ImGui.TableSetColumnIndex(5);
            DrawDayStrip(row, board, 160f * scale, 16f * scale);

            ImGui.PopID();
        }

        ImGui.EndTable();
    }

    private static void DrawDayStrip(VenueRow row, BoardView board, float width, float height)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(width, height));

        var topLeft = pos;
        var bottomRight = pos + new Vector2(width, height);

        draw.AddRectFilled(topLeft, bottomRight, KosMos.U32(KosMos.Fade(KosMos.Raised, 0.75f)));
        draw.AddRect(topLeft, bottomRight, KosMos.U32(KosMos.Line));

        var span = (board.DayEnd - board.DayStart).TotalSeconds;
        if (span <= 0) return;

        for (var hour = 6; hour < 24; hour += 6)
        {
            var x = pos.X + width * hour / 24f;
            draw.AddLine(new Vector2(x, topLeft.Y + 1f), new Vector2(x, bottomRight.Y - 1f),
                KosMos.U32(KosMos.Fade(KosMos.Line, 0.9f)));
        }

        foreach (var (start, end) in row.Segments)
        {
            var x0 = (float)((start - board.DayStart).TotalSeconds / span) * width;
            var x1 = (float)((end - board.DayStart).TotalSeconds / span) * width;
            var from = new Vector2(topLeft.X + Math.Clamp(x0, 0f, width - 1f), topLeft.Y + 2f);
            var to = new Vector2(topLeft.X + Math.Clamp(x1, 1f, width), bottomRight.Y - 2f);
            if (to.X <= from.X) continue;

            var color = row.IsOpen ? KosMos.Gold : KosMos.Ember;
            draw.AddRectFilled(from, to, KosMos.U32(KosMos.Fade(color, 0.8f)));
        }

        var nowX = (float)((board.Now - board.DayStart).TotalSeconds / span) * width;
        if (nowX is >= 0f && nowX <= width)
        {
            draw.AddLine(new Vector2(topLeft.X + nowX, topLeft.Y), new Vector2(topLeft.X + nowX, bottomRight.Y),
                KosMos.U32(KosMos.Crimson), 1.5f);
        }
    }

    // ------------------------------------------------------------------ export

    private void DrawExport(BoardView board)
    {
        var options = new ExportOptions
        {
            Use24Hour = config.Use24Hour,
            IncludeTravel = config.IncludeTravelInExport,
            IncludeNotes = config.IncludeNotesInExport,
        };

        var text = Exporter.Build(board, options);

        KosMos.Section("Export");

        var includeTravel = config.IncludeTravelInExport;
        if (ImGui.Checkbox("Include travel commands", ref includeTravel))
        {
            config.IncludeTravelInExport = includeTravel;
            Save();
        }

        ImGui.SameLine();
        var includeNotes = config.IncludeNotesInExport;
        if (ImGui.Checkbox("Include notes", ref includeNotes))
        {
            config.IncludeNotesInExport = includeNotes;
            Save();
        }

        ImGui.SameLine();
        KosMos.Hint("24-hour is set on the board.");

        ImGui.Spacing();

        var avail = ImGui.GetContentRegionAvail();
        var height = MathF.Max(120f, avail.Y - 96f * ImGuiHelpers.GlobalScale);

        using (var preview = ImRaii.Child("##exportpreview", new Vector2(0, height), true))
        {
            if (preview.Success)
            {
                using (ImRaii.PushColor(ImGuiCol.Text, KosMos.Ink))
                {
                    ImGui.PushTextWrapPos(0f);
                    ImGui.TextUnformatted(text);
                    ImGui.PopTextWrapPos();
                }
            }
        }

        ImGui.Spacing();

        if (ImGui.Button("Copy list", new Vector2(120f * ImGuiHelpers.GlobalScale, 0)))
        {
            Copy(text, "Full venue list copied.");
        }

        ImGui.SameLine();
        if (ImGui.Button("Save .txt", new Vector2(120f * ImGuiHelpers.GlobalScale, 0)))
        {
            var path = export(text);
            if (path is null)
            {
                Flash("Writing the file failed - see /xllog.");
            }
            else
            {
                lastExportPath = path;
                Flash("Saved to " + path);
            }
        }

        if (lastExportPath.Length > 0)
        {
            ImGui.SameLine();
            KosMos.Hint(lastExportPath);
        }
    }

    // ------------------------------------------------------------------ footer

    private void DrawFooter()
    {
        ImGui.Spacing();
        KosMos.DottedRule(0.5f);

        var scale = ImGuiHelpers.GlobalScale;

        if (ImGui.Button("New venue", new Vector2(120f * scale, 0)))
        {
            config.AddVenue();
            Save();
            Flash("Venue added.");
        }

        var venue = config.Selected;

        ImGui.SameLine();
        using (ImRaii.Disabled(venue is null))
        {
            if (ImGui.Button("Duplicate", new Vector2(100f * scale, 0)) && venue is not null)
            {
                var copyOf = venue.Copy();
                config.Venues.Insert(config.Venues.IndexOf(venue) + 1, copyOf);
                config.SelectedId = copyOf.Id;
                Save();
                Flash("Venue duplicated.");
            }

            ImGui.SameLine();

            if (!confirmDelete)
            {
                if (ImGui.Button("Delete", new Vector2(80f * scale, 0)) && venue is not null)
                {
                    confirmDelete = true;
                }
            }
            else if (ImGui.Button("Confirm delete", new Vector2(120f * scale, 0)) && venue is not null)
            {
                config.Remove(venue);
                confirmDelete = false;
                Save();
                Flash("Venue deleted.");
            }

            if (confirmDelete)
            {
                ImGui.SameLine();
                if (ImGui.Button("Cancel", new Vector2(70f * scale, 0)))
                {
                    confirmDelete = false;
                }
            }
        }

        if (dirty)
        {
            ImGui.SameLine();
            KosMos.Hint("unsaved");
        }

        if (flash.Length > 0 && DateTime.UtcNow < flashUntil)
        {
            ImGui.SameLine();
            ImGui.TextColored(KosMos.Gold, flash);
        }
    }

    // ------------------------------------------------------------------ helpers

    private void MarkDirty()
    {
        dirty = true;
        dirtySince = DateTime.UtcNow;
    }

    private void CommitEdit()
    {
        if (!ImGui.IsItemDeactivatedAfterEdit()) return;
        Save();
    }

    private void Copy(string text, string message)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            Flash("Nothing to copy.");
            return;
        }

        copy(text.Trim());
        Flash(message);
    }

    private void OpenLink(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            Flash("Nothing to open.");
            return;
        }

        var target = url.Trim();
        if (!target.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
            !target.StartsWith("discord://", StringComparison.OrdinalIgnoreCase))
        {
            target = "https://" + target;
        }

        try
        {
            Util.OpenLink(target);
        }
        catch (Exception)
        {
            Flash("Could not open that link.");
        }
    }

    private void Flash(string message)
    {
        flash = message;
        flashUntil = DateTime.UtcNow.AddSeconds(4);
    }

    private void Save()
    {
        dirty = false;
        save();
    }
}

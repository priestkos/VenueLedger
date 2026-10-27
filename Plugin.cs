using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace VenueLedger;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "Venue Ledger";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;

    private const string CommandName = "/venue";

    private readonly Configuration configuration;
    private readonly WindowSystem windowSystem = new("VenueLedger");
    private readonly MainWindow mainWindow;

    /// <summary>
    /// Clipboard writes are queued and flushed inside the Draw handler, so they always land in a frame -
    /// the same reason /venue copy works with the window shut.
    /// </summary>
    private string? pendingCopy;

    public Plugin()
    {
        configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        configuration.Normalize();

        mainWindow = new MainWindow(configuration, SaveConfig, QueueCopy, ExportToFile);
        windowSystem.AddWindow(mainWindow);

        PluginInterface.UiBuilder.Draw += OnDraw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMain;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleMain;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the venue ledger. /venue copy copies the list, /venue export writes it to a file, " +
                          "/venue list echoes today's hours into chat.",
        });

        Log.Info("[VenueLedger] loaded with " + configuration.Venues.Count.ToString(CultureInfo.InvariantCulture) +
                 " venues, board clock " + ZoneBook.Describe(ZoneBook.Resolve(configuration.BoardZone), DateTimeOffset.UtcNow) + ".");
    }

    public void Dispose()
    {
        SaveConfig();

        CommandManager.RemoveHandler(CommandName);

        PluginInterface.UiBuilder.OpenConfigUi -= ToggleMain;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMain;
        PluginInterface.UiBuilder.Draw -= OnDraw;

        windowSystem.RemoveAllWindows();
        mainWindow.Dispose();
    }

    private void OnDraw()
    {
        windowSystem.Draw();

        var text = pendingCopy;
        if (text is null) return;

        pendingCopy = null;
        try
        {
            ImGui.SetClipboardText(text);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[VenueLedger] could not write to the clipboard.");
        }
    }

    private void OnCommand(string command, string args)
    {
        args = args.Trim();
        var lowered = args.ToLowerInvariant();

        if (args.Length == 0)
        {
            ToggleMain();
            return;
        }

        var options = new ExportOptions
        {
            Use24Hour = configuration.Use24Hour,
            IncludeTravel = configuration.IncludeTravelInExport,
            IncludeNotes = configuration.IncludeNotesInExport,
        };

        var board = Board.Build(configuration.Venues, configuration.BoardZone, DateTimeOffset.UtcNow, configuration.Use24Hour);

        switch (lowered)
        {
            case "copy":
            {
                if (board.Rows.Count == 0)
                {
                    ChatGui.Print("[Venue Ledger] the ledger is empty.", "VenueLedger");
                    return;
                }

                QueueCopy(Exporter.Build(board, options));
                ChatGui.Print("[Venue Ledger] copied " + board.Rows.Count.ToString(CultureInfo.InvariantCulture) +
                              " venues to the clipboard.", "VenueLedger");
                return;
            }

            case "export":
            {
                if (board.Rows.Count == 0)
                {
                    ChatGui.Print("[Venue Ledger] the ledger is empty.", "VenueLedger");
                    return;
                }

                var path = ExportToFile(Exporter.Build(board, options));
                ChatGui.Print(path is null
                    ? "[Venue Ledger] the export failed - see /xllog."
                    : "[Venue Ledger] saved " + path, "VenueLedger");
                return;
            }

            case "list":
            {
                if (board.Rows.Count == 0)
                {
                    ChatGui.Print("[Venue Ledger] the ledger is empty.", "VenueLedger");
                    return;
                }

                var label = ZoneBook.Label(board.BoardZone);
                foreach (var row in board.Rows)
                {
                    var hours = row.Known
                        ? row.BoardOpenText + "-" + row.BoardCloseText + " " + label + " (" + row.StatusText + ")"
                        : row.Error;

                    ChatGui.Print(row.Venue.DisplayName + " - " + hours, "VenueLedger");
                }

                return;
            }

            case "help":
            case "?":
                ChatGui.Print("/venue opens the ledger. /venue copy copies the list. /venue export writes a .txt. " +
                              "/venue list prints today's hours to chat.", "VenueLedger");
                return;

            default:
                ChatGui.Print("[Venue Ledger] unknown argument \"" + args + "\". Try /venue help.", "VenueLedger");
                return;
        }
    }

    /// <summary>Writes the exported text beside the plugin's own config, so it survives plugin updates.</summary>
    private string? ExportToFile(string text)
    {
        try
        {
            var directory = PluginInterface.GetPluginConfigDirectory();
            Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, Exporter.FileName(DateTimeOffset.UtcNow));
            File.WriteAllText(path, text, new UTF8Encoding(false));

            Log.Info("[VenueLedger] exported to " + path);
            return path;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[VenueLedger] could not write the export file.");
            return null;
        }
    }

    private void SaveConfig()
    {
        try
        {
            PluginInterface.SavePluginConfig(configuration);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[VenueLedger] could not save the configuration.");
        }
    }

    private void QueueCopy(string text) => pendingCopy = text;

    private void ToggleMain() => mainWindow.Toggle();
}

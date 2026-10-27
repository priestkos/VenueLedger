using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace VenueLedger;

/// <summary>
/// The K'os Mos palette: smoke, wine, ember and cream - #0a0507 #4a161b #d05a26 #f7d6a8, taken from the
/// canon-portrait theme sheet. Pushed around our own widgets only, so the user's global Dalamud style is
/// left exactly as it was.
/// </summary>
internal static class KosMos
{
    public static readonly Vector4 Ground = Hex(0x0a0507);
    public static readonly Vector4 Well = Hex(0x080406);
    public static readonly Vector4 Surface = Hex(0x120a0c);
    public static readonly Vector4 Card = Hex(0x150b0d);
    public static readonly Vector4 Raised = Hex(0x1e1012);
    public static readonly Vector4 Line = Hex(0x3d1a1f);
    public static readonly Vector4 Ink = Hex(0xf6e2c6);
    public static readonly Vector4 InkSoft = Hex(0xf7d6a8);
    public static readonly Vector4 Dim = Hex(0x96736d);
    public static readonly Vector4 Ember = Hex(0xd05a26);
    public static readonly Vector4 Gold = Hex(0xd9a94e);
    public static readonly Vector4 Crimson = Hex(0xb8323f);
    public static readonly Vector4 DeepInk = Hex(0x1a0a06);

    /// <summary>Open is gold, not red: a venue being open is good news, and crimson is for errors.</summary>
    public static readonly Vector4 Live = Hex(0xd9a94e);

    public static Vector4 Hex(uint rgb) => new(
        ((rgb >> 16) & 0xFF) / 255f,
        ((rgb >> 8) & 0xFF) / 255f,
        (rgb & 0xFF) / 255f,
        1f);

    public static Vector4 Fade(Vector4 color, float alpha) => new(color.X, color.Y, color.Z, alpha);

    public static uint U32(Vector4 color) => ImGui.GetColorU32(color);

    /// <summary>Blend two palette entries - used for hover states so labels keep their contrast.</summary>
    public static Vector4 Mix(Vector4 a, Vector4 b, float t) => new(
        a.X + (b.X - a.X) * t,
        a.Y + (b.Y - a.Y) * t,
        a.Z + (b.Z - a.Z) * t,
        a.W + (b.W - a.W) * t);

    /// <summary>
    /// Scoped palette for the window. Buttons and headers are mixed toward the window background rather
    /// than painted the bright accent, so the ink stays readable on them.
    /// </summary>
    public static ImRaii.ColorDisposable Push()
    {
        var buttonHover = Mix(Ember, Ground, 0.58f);
        var buttonActive = Mix(Ember, Ground, 0.42f);
        var tabHover = Mix(Ember, Ground, 0.66f);
        var tabActive = Mix(Ember, Ground, 0.5f);

        return new ImRaii.ColorDisposable()
            .Push(ImGuiCol.WindowBg, Ground)
            .Push(ImGuiCol.ChildBg, Surface)
            .Push(ImGuiCol.PopupBg, Card)
            .Push(ImGuiCol.Border, Line)
            .Push(ImGuiCol.BorderShadow, Fade(Ground, 0f))
            .Push(ImGuiCol.Text, Ink)
            .Push(ImGuiCol.TextDisabled, Dim)
            .Push(ImGuiCol.TextSelectedBg, Fade(Ember, 0.35f))
            .Push(ImGuiCol.FrameBg, Raised)
            .Push(ImGuiCol.FrameBgHovered, Mix(Raised, Ember, 0.18f))
            .Push(ImGuiCol.FrameBgActive, Mix(Raised, Ember, 0.28f))
            .Push(ImGuiCol.Button, Card)
            .Push(ImGuiCol.ButtonHovered, buttonHover)
            .Push(ImGuiCol.ButtonActive, buttonActive)
            .Push(ImGuiCol.Header, Mix(Ink, Ground, 0.72f))
            .Push(ImGuiCol.HeaderHovered, Mix(Ink, Ground, 0.5f))
            .Push(ImGuiCol.HeaderActive, Mix(Ink, Ground, 0.35f))
            .Push(ImGuiCol.Tab, Surface)
            .Push(ImGuiCol.TabHovered, tabHover)
            .Push(ImGuiCol.TabActive, tabActive)
            .Push(ImGuiCol.TabUnfocused, Surface)
            .Push(ImGuiCol.TabUnfocusedActive, Mix(Ember, Ground, 0.62f))
            .Push(ImGuiCol.TitleBg, Well)
            .Push(ImGuiCol.TitleBgActive, Well)
            .Push(ImGuiCol.MenuBarBg, Well)
            .Push(ImGuiCol.ScrollbarBg, Well)
            .Push(ImGuiCol.ScrollbarGrab, Mix(Line, Ember, 0.35f))
            .Push(ImGuiCol.ScrollbarGrabHovered, Mix(Line, Ember, 0.55f))
            .Push(ImGuiCol.ScrollbarGrabActive, Ember)
            .Push(ImGuiCol.CheckMark, Gold)
            .Push(ImGuiCol.SliderGrab, Gold)
            .Push(ImGuiCol.SliderGrabActive, Ember)
            .Push(ImGuiCol.Separator, Line)
            .Push(ImGuiCol.SeparatorHovered, Ember)
            .Push(ImGuiCol.SeparatorActive, Gold)
            .Push(ImGuiCol.ResizeGrip, Fade(Ember, 0.4f))
            .Push(ImGuiCol.ResizeGripHovered, Fade(Ember, 0.7f))
            .Push(ImGuiCol.ResizeGripActive, Gold)
            .Push(ImGuiCol.PlotLines, Gold)
            .Push(ImGuiCol.TableHeaderBg, Raised)
            .Push(ImGuiCol.TableBorderStrong, Line)
            .Push(ImGuiCol.TableBorderLight, Fade(Line, 0.5f))
            .Push(ImGuiCol.TableRowBg, Fade(Ground, 0f))
            .Push(ImGuiCol.TableRowBgAlt, Fade(Raised, 0.45f))
            .Push(ImGuiCol.NavHighlight, Fade(Gold, 0.5f))
            .Push(ImGuiCol.DragDropTarget, Gold)
            .Push(ImGuiCol.ModalWindowDimBg, Fade(Ground, 0.7f));
    }

    /// <summary>Sharp window, softly rounded frames - an editorial sheet, not a mobile app.</summary>
    public static ImRaii.StyleDisposable PushStyle()
    {
        var scale = ImGui.GetIO().FontGlobalScale;
        return new ImRaii.StyleDisposable()
            .Push(ImGuiStyleVar.WindowRounding, 2f)
            .Push(ImGuiStyleVar.ChildRounding, 2f)
            .Push(ImGuiStyleVar.FrameRounding, 2f)
            .Push(ImGuiStyleVar.PopupRounding, 2f)
            .Push(ImGuiStyleVar.TabRounding, 0f)
            .Push(ImGuiStyleVar.ScrollbarRounding, 2f)
            .Push(ImGuiStyleVar.GrabRounding, 2f)
            .Push(ImGuiStyleVar.WindowBorderSize, 1f)
            .Push(ImGuiStyleVar.ChildBorderSize, 1f)
            .Push(ImGuiStyleVar.FrameBorderSize, 1f)
            .Push(ImGuiStyleVar.WindowPadding, new Vector2(14f, 12f) * scale)
            .Push(ImGuiStyleVar.FramePadding, new Vector2(7f, 4f) * scale)
            .Push(ImGuiStyleVar.ItemSpacing, new Vector2(7f, 6f) * scale)
            .Push(ImGuiStyleVar.CellPadding, new Vector2(6f, 4f) * scale);
    }

    /// <summary>A dotted rule: the Kos Mos sheet separates things with dots, never with a solid bar.</summary>
    public static void DottedRule(float alpha = 0.55f, float dash = 2f, float gap = 4f)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = Math.Max(8f, ImGui.GetContentRegionAvail().X);
        var y = pos.Y + 3f;
        var color = U32(Fade(Ember, alpha));

        for (var x = 0f; x < width; x += dash + gap)
        {
            var end = Math.Min(x + dash, width);
            draw.AddLine(new Vector2(pos.X + x, y), new Vector2(pos.X + end, y), color, 1f);
        }

        ImGui.Dummy(new Vector2(width, 5f));
    }

    /// <summary>Section label: mono-ish caps in ink gold, with a dotted rule under it.</summary>
    public static void Section(string label)
    {
        ImGui.Spacing();
        ImGui.TextColored(Gold, label.ToUpperInvariant());
        DottedRule(0.4f);
    }

    /// <summary>A soft hint line - always dimmer than the ink, never brighter than a value.</summary>
    public static void Hint(string text)
    {
        ImGui.TextColored(Dim, text);
    }

    /// <summary>Large text drawn straight onto the window list, so the clock reads at a glance.</summary>
    public static void BigText(string text, Vector4 color, float sizeMultiplier)
    {
        var draw = ImGui.GetWindowDrawList();
        var font = ImGui.GetFont();
        var size = ImGui.GetFontSize() * sizeMultiplier;
        var pos = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(ImGui.CalcTextSize(text).X * sizeMultiplier, size * 1.2f));
        draw.AddText(font, size, pos, U32(color), text);
    }
}

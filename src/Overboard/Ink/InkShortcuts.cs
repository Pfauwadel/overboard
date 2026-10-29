using Avalonia.Input;

namespace Overboard.Ink;

/// <summary>Keyboard shortcuts shared by every drawing window: tools, colors, size, undo and clear.</summary>
public static class InkShortcuts
{
    /// <summary>Ctrl, or Cmd on macOS.</summary>
    public static bool IsCommand(KeyModifiers modifiers) =>
        modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta);

    /// <summary>Applies the shortcut for <paramref name="e"/>, if any. Returns whether there was one.</summary>
    public static bool Handle(KeyEventArgs e, ToolSettings settings, InkCanvas canvas)
    {
        var command = IsCommand(e.KeyModifiers);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        // Physical keys for digits: they need Shift on AZERTY keyboards.
        var digit = e.PhysicalKey switch
        {
            PhysicalKey.Digit1 or PhysicalKey.NumPad1 => 1,
            PhysicalKey.Digit2 or PhysicalKey.NumPad2 => 2,
            PhysicalKey.Digit3 or PhysicalKey.NumPad3 => 3,
            PhysicalKey.Digit4 or PhysicalKey.NumPad4 => 4,
            PhysicalKey.Digit5 or PhysicalKey.NumPad5 => 5,
            PhysicalKey.Digit6 or PhysicalKey.NumPad6 => 6,
            _ => 0,
        };

        Tool? tool = command ? null : e.Key switch
        {
            Key.P => Tool.Pen,
            Key.S => Tool.Highlighter,
            Key.G => Tool.Eraser,
            Key.T => Tool.Text,
            Key.L => Tool.Line,
            Key.F => Tool.Arrow,
            Key.R => Tool.Rectangle,
            Key.E => Tool.Ellipse,
            Key.K => Tool.Laser,
            Key.O => Tool.Spotlight,
            _ => null,
        };

        if (tool is { } selected) settings.Tool = selected;
        else if (digit > 0 && !command) settings.Color = ToolSettings.Palette[digit - 1];
        else if (command && e.Key == Key.Z && !shift) canvas.Undo();
        else if (command && (e.Key == Key.Y || (e.Key == Key.Z && shift))) canvas.Redo();
        else if (e.Key is Key.Delete or Key.Back) canvas.Clear();
        else if (e.Key is Key.Add or Key.OemPlus) settings.Grow(1);
        else if (e.Key is Key.Subtract or Key.OemMinus) settings.Grow(-1);
        else return false;
        return true;
    }
}

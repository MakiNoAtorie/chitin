using Avalonia.Input;

namespace Chitin;

// One user command. The key handler, the menus, the F1 list, Find and the points panel all read
// this table, so they cannot drift apart. Run takes the index of the key that matched (0 from a
// menu or Find); an indexed command (1-9) needs that key, so it is in neither the menus nor Find.
public sealed record Command(
    string Name,
    string Group,
    string Gesture,
    KeyGesture[] Keys,
    Func<int, Task> Run,
    bool InMenu = false,
    bool Separator = false,
    bool Indexed = false);

public readonly record struct HelpRow(string Keys, string Text, double Indent = 0);

public static class Commands
{
    // Per word, case-insensitive, over name and group: "grid lar" finds "Larger grid". An indexed
    // command needs its digit key, so Find leaves it out.
    public static List<Command> Find(IEnumerable<Command> commands, string query)
    {
        string[] words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return commands.Where(command => !command.Indexed && words.All(word =>
            $"{command.Name} {command.Group}".Contains(word, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    public static string Label(KeyGesture gesture)
    {
        string modifiers = "";
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            modifiers += "Ctrl+";
        }

        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            modifiers += "Shift+";
        }

        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            modifiers += "Alt+";
        }

        string key = gesture.Key switch
        {
            Key.OemPlus or Key.Add => "+",
            Key.OemMinus or Key.Subtract => "-",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.Back => "Backspace",
            >= Key.D0 and <= Key.D9 => ((int)(gesture.Key - Key.D0)).ToString(),
            _ => gesture.Key.ToString()
        };
        return modifiers + key;
    }

    // The overlay lists the pointer gestures and the tool keys, then every command with a key.
    public static List<HelpRow> HelpRows(IEnumerable<Command> commands)
    {
        var rows = new List<HelpRow>
        {
            new("Click", "Place the next point (points tool)"),
            new("Left drag", "Drag a vanishing point"),
            new("Space + drag", "Pan (middle drag too)"),
            new("Wheel", "Pan (Shift: sideways)"),
            new("Ctrl + wheel", "Zoom about the cursor"),
            new("Right drag", "Create a trace line (2 max; T tool: left drag)"),
            new("+ Ctrl", "Snap to 5 degree increments", 32),
            new("+ Shift", "Snap to 15 degree increments", 32),
            new("Alt + drag", "Move the reference image"),
            new("Alt + wheel", "Scale the reference image"),
            new(" ", " "),
            new("In the box tool", " "),
            new("1 / 2", "During the first drag: follow point 1 or 2", 32),
            new("In the subdivision tool", " "),
            new("2-9  + / -", "Cells of the last subdivision (with Shift: rows only)", 32),
            new("In a tool", " "),
            new("Backspace", "Cancel, or delete its last shape", 32)
        };

        string? group = null;
        foreach (Command command in commands.Where(command => command.Keys.Length > 0))
        {
            if (command.Group != group)
            {
                rows.Add(new HelpRow(" ", " "));
                group = command.Group;
            }

            rows.Add(new HelpRow(command.Gesture, command.Name));
        }

        return rows;
    }
}

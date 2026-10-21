using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

namespace Chitin;

// Detailed mode: the inspector as ruled tables. Every edit runs a command from the table, so it
// pushes the same undo step as its shortcut.
public partial class MainWindow
{
    private static readonly string[] GridStyles = { "both", "vertical", "horizontal" };
    private int _inspectorRevision = -1;
    private IBrush _ink = Brushes.Black;
    private IBrush _paper = Brushes.White;
    private IBrush _hair = Brushes.Gray;

    internal void SetDetailed(bool detailed)
    {
        Detailed = detailed;
        Tray.Width = detailed ? 184 : 40;
        Inspector.Width = detailed ? 304 : 0;
        Inspector.BorderThickness = new Thickness(detailed ? 2 : 0, 0, 0, 0);
        ModeName.Text = detailed ? "Detailed" : "Compact";
        _inspectorRevision = -1;
        UpdateChrome();
    }

    private void UpdateInspector()
    {
        World world = Viewport.World;
        if (!Detailed || _inspectorRevision == world.Revision)
        {
            return;
        }

        _inspectorRevision = world.Revision;
        Palette palette = world.ActivePalette;
        _ink = new SolidColorBrush(palette.Ink);
        _paper = new SolidColorBrush(palette.Paper);
        _hair = new SolidColorBrush(Chrome.Hair(palette));
        CultureInfo inv = CultureInfo.InvariantCulture;
        InspectorPanel.Children.Clear();

        Section("A", "VANISHING POINTS", "CTRL 1-9");
        for (int i = 0; i < world.VanishingPoints.Length; i++)
        {
            IBrush colour = world.Styles.VPLines[i].Brush!;
            if (world.VanishingPoints[i] is { } point)
            {
                var chip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                chip.Children.Add(new Border { Width = 10, Height = 10, Background = colour });
                chip.Children.Add(new TextBlock { Text = (i + 1).ToString(inv) });
                Row("46,*,52", world.SelectionIdx == i,
                    Cell(chip, "Select vanishing point", i),
                    Cell(string.Format(inv, "{0:0}, {1:0}", point.Pos.X + world.PicturePlaneWidthHalf,
                        point.Pos.Y + world.PicturePlaneHeightHalf)),
                    Cell(point.Density == 0 ? "auto" : (point.Density / 4).ToString(inv)));
            }
            else
            {
                var chip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                chip.Children.Add(new Border { Width = 10, Height = 10, BorderBrush = colour, BorderThickness = new Thickness(1) });
                chip.Children.Add(new TextBlock { Text = (i + 1).ToString(inv) });
                Row("46,*", false, Cell(chip), Hatched());
            }
        }

        Section("B", "GUIDES", "");
        Row("140,*", false, Cell("Grid · G"), Cell(Box(world.ShowGrid), "Toggle grid"));
        Row("140,*", false, Cell("Style · Ctrl+G"), Cell(GridStyles[world.GridStyle % 3], "Cycle grid style"));
        Row("140,*", false, Cell("Size · + / -"),
            Stepper("Smaller grid", world.GridSz.ToString(inv), "Larger grid"));
        Row("140,*", false, Cell("Cone · V"), Cell(Box(world.ShowCone), "Cone of vision"));
        Row("140,*", false, Cell("Level 1-2 · H"), Cell("level", "Level points 1 and 2"));

        Section("C", "CAMERA", "");
        var presets = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*") };
        for (int n = 1; n <= 3; n++)
        {
            Control preset = Cell($"{n}-pt", $"{n}-point preset");
            preset.Margin = new Thickness(n > 1 ? 1 : 0, 0, 0, 0);
            Grid.SetColumn(preset, n - 1);
            presets.Children.Add(preset);
        }

        Row("140,*", false, Cell("Preset"), presets);
        ConeOfVision built = default;
        bool cone = world.ShowCone && ConeOfVision.TryBuild(world.VanishingPoints[0]?.Pos,
            world.VanishingPoints[1]?.Pos, world.PicturePlaneWidth, out built);
        Row("140,*", false, Cell("FOV"), cone ? Cell(string.Format(inv, "{0:0}°", built.FovDegrees)) : Hatched());
        Row("140,*", false, Cell("Station distance"),
            cone ? Cell(string.Format(inv, "{0:0}", built.Distance)) : Hatched());
        foreach (RigState rig in world.Rigs)
        {
            string name = rig.Name;
            Row("140,*", false, Cell("Rig"), Action(new TextBlock { Text = name }, $"rig:{name}", () =>
            {
                Viewport.RecallRig(name);
                return Task.CompletedTask;
            }));
        }

        Row("140,*", false, Cell("Rigs"), Cell("save rig...", "Save rig..."));

        Section("D", "REFERENCE", "I");
        bool image = world.BackgroundImg != null;
        Row("140,*", false, Cell("Show · I"), image ? Cell(Box(!world.Reference.Hidden), "Show / hide reference") : Hatched());
        Row("140,*", false, Cell("Opacity · [ ]"), image
            ? Stepper("Lower reference opacity", string.Format(inv, "{0:0}%", world.Reference.Opacity * 100),
                "Raise reference opacity")
            : Hatched());
        Row("140,*", false, Cell("Image"), Cell("load...", "Load reference image..."));

        Section("E", "PALETTE", "CTRL SHIFT P");
        foreach (Palette option in Palette.BuiltIns)
        {
            var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            label.Children.Add(new TextBlock { Text = option.Name, Width = 70 });
            var swatches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
            foreach (Color colour in option.VpColors)
            {
                swatches.Children.Add(new Border { Width = 7, Height = 10, Background = new SolidColorBrush(colour) });
            }

            label.Children.Add(swatches);
            Row("*", option.Name == world.PaletteName, Cell(label, $"Palette {option.Name}"));
        }

        Section("F", "REVISIONS", "CTRL Z");
        (int Number, string Name)[] revisions = Viewport.History.Revisions.Take(5).ToArray();
        foreach ((int number, string name) in revisions)
        {
            Row("46,*", false, Cell(number.ToString("00", inv)), Cell(name));
        }

        if (revisions.Length == 0)
        {
            Row("46,*", false, Cell("-"), Hatched());
        }
    }

    // A detail bubble, the section title in spaced capitals, its key on the right.
    private void Section(string letter, string title, string key)
    {
        var header = new DockPanel { Margin = new Thickness(8, 8, 8, 6) };
        var bubble = new Bubble { Letter = letter, Stroke = _ink, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(bubble, Dock.Left);
        header.Children.Add(bubble);
        var keys = new TextBlock { Classes = { "caps" }, Text = key, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(keys, Dock.Right);
        header.Children.Add(keys);
        header.Children.Add(new TextBlock { Classes = { "caps" }, Text = title, VerticalAlignment = VerticalAlignment.Center });
        InspectorPanel.Children.Add(new Border
        {
            Child = header,
            BorderBrush = _ink,
            BorderThickness = new Thickness(0, 0, 0, 1)
        });
    }

    // One ruled row: hairline under every cell and between cells; a selected row is inverted.
    private void Row(string columns, bool on, params Control[] cells)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(columns), Height = 25 };
        for (int i = 0; i < cells.Length; i++)
        {
            Control cell = cells[i];
            if (on)
            {
                cell.Classes.Add("on");
                if (cell is Border border)
                {
                    border.Background = _ink;
                    TextElement.SetForeground(border, _paper);
                }
            }

            var rule = new Border
            {
                Child = cell,
                BorderBrush = _hair,
                BorderThickness = new Thickness(i > 0 ? 1 : 0, 0, 0, 1)
            };
            Grid.SetColumn(rule, i);
            grid.Children.Add(rule);
        }

        InspectorPanel.Children.Add(grid);
    }

    // Text or content in a cell; with a command name, a flat button that runs it.
    private Control Cell(object content, string? command = null, int index = -1)
    {
        Control body = content as Control ??
            new TextBlock { Text = (string)content, VerticalAlignment = VerticalAlignment.Center };
        if (command == null)
        {
            return new Border { Child = body, Padding = new Thickness(8, 0) };
        }

        return Action(body, command, () => Viewport.RunCommandAsync(
            Viewport.Commands.Single(item => item.Name == command), index));
    }

    private Button Action(Control body, string tag, Func<Task> run)
    {
        var button = new Button
        {
            Classes = { "cell" },
            Content = body,
            Padding = new Thickness(8, 0),
            Tag = tag,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        AutomationProperties.SetName(button, tag);
        button.Click += (_, _) => RunAsync(run);
        return button;
    }

    private Control Hatched()
    {
        return new Hatch { Stroke = _hair };
    }

    // A small square, filled when on, then the word.
    private Control Box(bool on)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        panel.Children.Add(new Border
        {
            Width = 10,
            Height = 10,
            BorderThickness = new Thickness(1),
            BorderBrush = _ink,
            Background = on ? _ink : null
        });
        panel.Children.Add(new TextBlock { Text = on ? "on" : "off" });
        return panel;
    }

    // Lower and raise commands either side of a value: "[-] 32 [+]".
    private Control Stepper(string lower, string value, string raise)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("28,*,28") };
        Control down = Cell("-", lower);
        Control up = Cell("+", raise);
        var text = new TextBlock
        {
            Text = value,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(text, 1);
        Grid.SetColumn(up, 2);
        grid.Children.Add(down);
        grid.Children.Add(text);
        grid.Children.Add(up);
        return grid;
    }
}

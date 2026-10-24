using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Chitin;

// The chrome around the viewport: tool tray, rulers, points panel, foot and Find.
public partial class MainWindow
{
    private sealed record TrayTool(string Letter, string Name, string Gesture, string Options, string? Command,
        ToolKind? Kind);

    private static readonly TrayTool[] TrayTools =
    {
        new("P", "Points", "Esc", "Click to place the next point · drag a point to move it · 1-9 drop one at the cursor",
            "Leave tool / unselect", null),
        new("T", "Trace", "T", "Drag a trace line (right-drag works in every tool) · Ctrl snaps to 5°, Shift to 15°",
            "Trace tool", ToolKind.Trace),
        new("B", "Box", "B", "Drag width, then depth, then height", "Box tool", ToolKind.Box),
        new("D", "Subdivide", "D", "Click a box face or four corners, then 2-9 cells", "Subdivision tool",
            ToolKind.Subdivision),
        new("E", "Ellipse", "E", "Click a box face or four corners, then drag the radius", "Ellipse tool",
            ToolKind.Ellipse)
    };

    private const string PointColumns = "40,*,*,4,22,36,22,26";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly List<(TrayTool Tool, Control Cell)> _trayCells = new();
    private readonly List<PointRow> _pointRows = new();
    private TextBlock _pointsEmpty = null!;
    private string? _themePalette;
    private bool _chromePending;
    private int _pointsRevision = -1;
    private List<Command> _findRows = new();
    private int _findIndex;

    private sealed record PointRow(Control Row, Button Number, Border Chip, TextBox X, TextBox Y, TextBlock Density);

    internal static string ToolHint(ToolKind? tool)
    {
        return tool switch
        {
            ToolKind.Trace => "Drag to pull a trace line (two at most) · Ctrl snaps to 5°, Shift to 15° · Esc leaves",
            ToolKind.Box => "Drag width, then depth, then height · 1 / 2 force an axis · Esc leaves",
            ToolKind.Subdivision => "Click a box face or four corners · 2-9 set its cells (Shift: rows) · Esc leaves",
            ToolKind.Ellipse => "Click a box face or four corners, then drag from the centre · Esc leaves",
            _ => "Click to place a point · drag a point to move it · right-drag pulls a trace line · " +
                 "Space+drag pans, Ctrl+Space+drag zooms"
        };
    }

    private void BuildSheet()
    {
        TopRuler.Viewport = Viewport;
        LeftRuler.Viewport = Viewport;
        BuildTray();
        BuildPoints();

        FindBox.TextChanged += (_, _) =>
        {
            if (FindOverlay.IsVisible)
            {
                _findIndex = 0;
                ShowFind();
            }
        };
        FindBox.LostFocus += (_, _) =>
        {
            if (!FindOverlay.IsPointerOver)
            {
                CloseFind(focusViewport: false);
            }
        };
        // Tunnel: the text box would otherwise take the arrows and Enter itself.
        FindBox.AddHandler(KeyDownEvent, FindBox_KeyDown, RoutingStrategies.Tunnel);

        ZoomButton.Click += (_, _) => RunAsync(() => Viewport.RunCommandAsync("Centre view"));
        ZoomInButton.Click += (_, _) => RunAsync(() => Viewport.RunCommandAsync("Zoom in"));
        ZoomOutButton.Click += (_, _) => RunAsync(() => Viewport.RunCommandAsync("Zoom out"));
        Viewport.ViewChanged += ScheduleChrome;
        Viewport.World.StatusChanged += ScheduleChrome;
        Viewport.CurrentFileChanged += ScheduleChrome;
        Viewport.World.Changed += ScheduleChrome;
        UpdateChrome();
    }

    private void ScheduleChrome()
    {
        if (_chromePending)
        {
            return;
        }

        _chromePending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _chromePending = false;
            UpdateChrome();
        });
    }

    internal void UpdateChrome()
    {
        World world = Viewport.World;
        if (_themePalette != world.PaletteName)
        {
            ApplyTheme(world.ActivePalette);
            _pointsRevision = -1;
        }

        TopRuler.InvalidateVisual();
        LeftRuler.InvalidateVisual();
        UpdateTray();
        UpdateFoot();
        UpdatePoints();
    }

    private void ApplyTheme(Palette palette)
    {
        _themePalette = palette.Name;
        Resources["Ink"] = new SolidColorBrush(palette.Ink);
        Resources["Paper"] = new SolidColorBrush(palette.Paper);
        Resources["Hair"] = new SolidColorBrush(Chrome.Hair(palette));
        Resources["Prompt"] = new SolidColorBrush(palette.Prompt);
        foreach ((string key, Color colour) in Chrome.FluentColours(palette))
        {
            Resources[key] = new SolidColorBrush(colour);
        }

        RequestedThemeVariant = Chrome.Luminance(palette.Paper) < 0.5 ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    private void BuildTray()
    {
        foreach (TrayTool tool in TrayTools)
        {
            var letter = new TextBlock
            {
                Text = tool.Letter,
                FontSize = 19,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Control cell;
            if (tool.Command is { } name)
            {
                var button = new Button { Classes = { "cell" }, Content = letter, Height = 39 };
                button.Click += (_, _) => RunAsync(() => Viewport.RunCommandAsync(name));
                cell = button;
            }
            else
            {
                cell = new Border { Child = letter, Height = 39, Background = Brushes.Transparent };
            }

            ToolTip.SetTip(cell, $"{tool.Name} ({tool.Gesture})\n{tool.Options}");
            AutomationProperties.SetName(cell, $"{tool.Name} tool");
            var rule = new Border { Child = cell, BorderThickness = new Thickness(0, 0, 0, 1) };
            rule.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Hair"));
            TrayPanel.Children.Add(rule);
            _trayCells.Add((tool, cell));
        }
    }

    private void UpdateTray()
    {
        foreach ((TrayTool tool, Control cell) in _trayCells)
        {
            cell.Classes.Set("on", tool.Command != null && Viewport.Tool == tool.Kind);
        }
    }

    // The status message (prompt or error), else the trace-crossing prompt.
    internal string? FootMessage()
    {
        World world = Viewport.World;
        if (world.StatusMessage is { } message)
        {
            return message;
        }

        if (world.TraceLines.Count > 1 &&
            Geom.GetLineIntersection(world.TraceLines[0].Start, world.TraceLines[0].End,
                world.TraceLines[1].Start, world.TraceLines[1].End, world.CrazyFar, out _))
        {
            return "Trace lines cross: Ctrl+Shift+1-9 drops a point there";
        }

        return null;
    }

    private static void Set(TextBlock block, string text)
    {
        if (block.Text != text)
        {
            block.Text = text;
        }
    }

    private void UpdateFoot()
    {
        World world = Viewport.World;
        string? message = FootMessage();
        Set(Message, message ?? "");
        Message.IsVisible = message != null;
        MessageMark.IsVisible = message != null;
        Message.FontWeight = world.StatusIsError ? FontWeight.Bold : FontWeight.Normal;
        Set(Hint, ToolHint(Viewport.Tool));

        var parts = new List<string>();
        if (world.SelectionIdx >= 0 && world.VanishingPoints[world.SelectionIdx] is { } point)
        {
            string density = point.Density == 0 ? "" : $"  density {point.Density / 4}";
            parts.Add(string.Format(Inv, "VP {0}  {1:0}, {2:0}{3}", world.SelectionIdx + 1,
                point.Pos.X + world.PicturePlaneWidthHalf, point.Pos.Y + world.PicturePlaneHeightHalf, density));
        }

        if (world.ShowCone)
        {
            parts.Add(ConeOfVision.TryBuild(world.VanishingPoints[0]?.Pos, world.VanishingPoints[1]?.Pos,
                world.PicturePlaneWidth, out ConeOfVision cone)
                ? string.Format(Inv, "cone {0:0}° · d {1:0}", cone.FovDegrees, cone.Distance)
                : "cone needs points 1 and 2 either side of the centre");
        }

        Set(Readout, string.Join("   ·   ", parts));
        Set(ZoomText, string.Format(Inv, "{0:0}%", world.Zoom * 100));
    }

    private void BuildPoints()
    {
        var header = new DockPanel { Margin = new Thickness(10, 8, 10, 6) };
        var key = new TextBlock { Classes = { "caps" }, Text = "CTRL K" };
        DockPanel.SetDock(key, Dock.Right);
        header.Children.Add(key);
        header.Children.Add(new TextBlock { Classes = { "caps" }, Text = "POINTS", FontWeight = FontWeight.Bold });
        PointsList.Children.Add(header);

        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions(PointColumns), Margin = new Thickness(6, 0) };
        string[] labels = { "#", "X", "Y", "", "DENSITY" };
        for (int i = 0; i < labels.Length; i++)
        {
            var label = new TextBlock
            {
                Classes = { "caps" },
                Text = labels[i],
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(label, i);
            Grid.SetColumnSpan(label, i == 4 ? 3 : 1);
            columns.Children.Add(label);
        }

        PointsList.Children.Add(columns);
        for (int i = 0; i < 9; i++)
        {
            _pointRows.Add(PointRowFor(i));
            PointsList.Children.Add(_pointRows[i].Row);
        }

        _pointsEmpty = new TextBlock
        {
            Text = "No points yet. Click the plane, press 1-9 over it, or use Camera › 2-point preset.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(10, 8),
            Opacity = 0.8
        };
        PointsList.Children.Add(_pointsEmpty);
    }

    private PointRow PointRowFor(int index)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(PointColumns),
            Height = 26,
            Margin = new Thickness(6, 0)
        };
        // The paper outline keeps the chip visible on the inverted (selected) row.
        var chip = new Border { Width = 10, Height = 10, BorderThickness = new Thickness(1) };
        chip.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Paper"));
        var number = new Button
        {
            Classes = { "cell" },
            Padding = new Thickness(6, 0),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { chip, new TextBlock { Text = (index + 1).ToString(Inv) } }
            }
        };
        number.Click += (_, _) => RunAsync(() => Viewport.RunCommandAsync(
            Viewport.Commands.Single(command => command.Name == "Select vanishing point"), index));
        AutomationProperties.SetName(number, $"Select point {index + 1}");
        grid.Children.Add(number);

        TextBox Coord(int column, string axis)
        {
            var box = new TextBox { Classes = { "coord" }, Margin = new Thickness(3, 0) };
            AutomationProperties.SetName(box, $"Point {index + 1} {axis}");
            box.KeyDown += (_, e) =>
            {
                if (e.Key is Key.Enter or Key.Escape)
                {
                    // Escape drops the edit: the next update writes the point's value back.
                    if (e.Key == Key.Escape)
                    {
                        box.Text = null;
                    }

                    Viewport.Focus();
                    e.Handled = true;
                }
            };
            box.LostFocus += (_, _) => ApplyCoords(index);
            Grid.SetColumn(box, column);
            grid.Children.Add(box);
            return box;
        }

        TextBox x = Coord(1, "X");
        TextBox y = Coord(2, "Y");
        var density = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(density, 5);
        grid.Children.Add(density);

        void Step(string text, int column, string name, Func<Task> run)
        {
            var button = new Button
            {
                Classes = { "cell" },
                Content = new TextBlock { Text = text, HorizontalAlignment = HorizontalAlignment.Center }
            };
            ToolTip.SetTip(button, name);
            AutomationProperties.SetName(button, $"{name} {index + 1}");
            button.Click += (_, _) => RunAsync(run);
            Grid.SetColumn(button, column);
            grid.Children.Add(button);
        }

        Step("-", 4, "Lower density", () => DensityAt(index, "Lower density"));
        Step("+", 6, "Raise density", () => DensityAt(index, "Raise density"));
        Step("×", 7, "Delete point", () => Viewport.RunCommandAsync(
            Viewport.Commands.Single(command => command.Name == "Delete vanishing point"), index));
        return new PointRow(grid, number, chip, x, y, density);
    }

    // The density commands act on the selected point: select the row's point first.
    private Task DensityAt(int index, string command)
    {
        Viewport.World.SelectionIdx = index;
        return Viewport.RunCommandAsync(command);
    }

    // Typed coordinates are document units (plane top-left = 0). Only an edited value moves the point.
    private void ApplyCoords(int index)
    {
        World world = Viewport.World;
        PointRow row = _pointRows[index];
        if (world.VanishingPoints[index] is { } point &&
            double.TryParse(row.X.Text, NumberStyles.Float, Inv, out double x) && double.IsFinite(x) &&
            double.TryParse(row.Y.Text, NumberStyles.Float, Inv, out double y) && double.IsFinite(y) &&
            (row.X.Text != Coord(point.Pos.X, world.PicturePlaneWidthHalf) ||
             row.Y.Text != Coord(point.Pos.Y, world.PicturePlaneHeightHalf)))
        {
            Viewport.MovePoint(index, new Vec2(x - world.PicturePlaneWidthHalf, y - world.PicturePlaneHeightHalf));
        }

        _pointsRevision = -1;
        ScheduleChrome();
    }

    private static string Coord(double world, int half)
    {
        return (world + half).ToString("0", Inv);
    }

    private void UpdatePoints()
    {
        World world = Viewport.World;
        if (!PointsPanel.IsVisible || _pointsRevision == world.Revision)
        {
            return;
        }

        _pointsRevision = world.Revision;
        bool any = false;
        for (int i = 0; i < _pointRows.Count; i++)
        {
            PointRow row = _pointRows[i];
            VanishingPoint? point = world.VanishingPoints[i];
            row.Row.IsVisible = point != null;
            if (point == null)
            {
                continue;
            }

            any = true;
            row.Number.Classes.Set("on", world.SelectionIdx == i);
            row.Chip.Background = world.Styles.VPLines[i].Brush;
            // A field being typed in keeps its text.
            if (!row.X.IsFocused)
            {
                row.X.Text = Coord(point.Pos.X, world.PicturePlaneWidthHalf);
            }

            if (!row.Y.IsFocused)
            {
                row.Y.Text = Coord(point.Pos.Y, world.PicturePlaneHeightHalf);
            }

            Set(row.Density, point.Density == 0 ? "auto" : (point.Density / 4).ToString(Inv));
        }

        _pointsEmpty.IsVisible = !any;
    }

    // Ctrl+K. View state: not saved, not undoable.
    internal void TogglePoints()
    {
        PointsPanel.IsVisible = !PointsPanel.IsVisible;
        _pointsRevision = -1;
        UpdateChrome();
    }

    private void OpenFind()
    {
        FindBox.Text = "";
        _findIndex = 0;
        FindOverlay.IsVisible = true;
        ShowFind();
        // After the menu and the command runner give focus back to the viewport.
        Dispatcher.UIThread.Post(() => FindBox.Focus(), DispatcherPriority.Background);
    }

    private void ShowFind()
    {
        _findRows = Commands.Find(Viewport.Commands, FindBox.Text ?? "");
        _findIndex = Math.Clamp(_findIndex, 0, Math.Max(0, _findRows.Count - 1));
        FindList.Children.Clear();
        if (_findRows.Count == 0)
        {
            FindList.Children.Add(new TextBlock
            {
                Text = "No command matches",
                Margin = new Thickness(8, 4),
                Opacity = 0.8
            });
        }

        for (int i = 0; i < _findRows.Count; i++)
        {
            Command command = _findRows[i];
            int index = i;
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,90,Auto") };
            grid.Children.Add(new TextBlock { Text = command.Name, Margin = new Thickness(8, 4) });
            var group = new TextBlock
            {
                Classes = { "caps" },
                Text = command.Group.ToUpperInvariant(),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(group, 1);
            grid.Children.Add(group);
            var gesture = new TextBlock { Text = command.Gesture, Margin = new Thickness(8, 4) };
            Grid.SetColumn(gesture, 2);
            grid.Children.Add(gesture);
            var row = new Button
            {
                Classes = { "cell" },
                Content = grid,
                Focusable = false,
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            row.Classes.Set("on", i == _findIndex);
            row.Click += (_, _) => RunFind(index);
            FindList.Children.Add(row);
        }

        if (_findIndex < FindList.Children.Count)
        {
            FindList.Children[_findIndex].BringIntoView();
        }
    }

    private void FindBox_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                _findIndex = Math.Min(_findRows.Count - 1, _findIndex + 1);
                ShowFind();
                break;
            case Key.Up:
                _findIndex = Math.Max(0, _findIndex - 1);
                ShowFind();
                break;
            case Key.Enter:
                RunFind(_findIndex);
                break;
            case Key.Escape:
                CloseFind();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void RunFind(int index)
    {
        Command? command = index >= 0 && index < _findRows.Count ? _findRows[index] : null;
        CloseFind();
        if (command != null)
        {
            RunAsync(() => Viewport.RunCommandAsync(command));
        }
    }

    // Losing focus closes the list too, but a press on a row lands first (rows take no focus).
    private void CloseFind(bool focusViewport = true)
    {
        if (!FindOverlay.IsVisible)
        {
            return;
        }

        FindOverlay.IsVisible = false;
        FindBox.Text = "";
        _findIndex = 0;
        if (focusViewport)
        {
            Viewport.Focus();
        }
    }

    // F1: the pointer gestures, the tool keys and every shortcut, one row each.
    private async Task ShowKeysAsync()
    {
        var rows = new StackPanel { Margin = new Thickness(16, 12) };
        foreach (HelpRow row in Commands.HelpRows(Viewport.Commands))
        {
            if (row.Keys == " ")
            {
                rows.Children.Add(new Border { Height = 8 });
            }
            else if (row.Text == " ")
            {
                rows.Children.Add(new TextBlock
                {
                    Text = row.Keys,
                    FontWeight = FontWeight.Bold,
                    Margin = new Thickness(0, 4, 0, 2)
                });
            }
            else
            {
                var line = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("170,*"),
                    Margin = new Thickness(row.Indent / 2, 1, 0, 1)
                };
                line.Children.Add(new TextBlock { Text = row.Keys, FontWeight = FontWeight.Bold });
                var text = new TextBlock { Text = row.Text, TextWrapping = TextWrapping.Wrap };
                Grid.SetColumn(text, 1);
                line.Children.Add(text);
                rows.Children.Add(line);
            }
        }

        var close = new Button
        {
            Content = "Close",
            IsDefault = true,
            IsCancel = true,
            Margin = new Thickness(16, 8),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(close, Dock.Bottom);
        var dialog = new Window
        {
            Title = "Keys and gestures",
            Width = 560,
            Height = 640,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new DockPanel { Children = { close, new ScrollViewer { Content = rows } } }
        };
        close.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
    }
}

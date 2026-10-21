using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Chitin;

// The drawing-sheet chrome around the viewport: tool tray, rulers, notes, title block and Find.
public partial class MainWindow
{
    private sealed record TrayTool(string Letter, string Name, string Gesture, string Options, string? Command,
        ToolKind? Kind);

    private static readonly TrayTool[] TrayTools =
    {
        new("P", "Points", "Esc", "1-9 drop · Ctrl+1-9 select", "Leave tool / unselect", null),
        new("T", "Trace", "right-drag", "Ctrl snaps 5° · Shift 15°", null, null),
        new("B", "Box", "B", "width, depth, height · 1 / 2 lock axis", "Box tool", ToolKind.Box),
        new("D", "Subdivide", "D", "click a face · 2-9 cells · Enter", "Subdivision tool", ToolKind.Subdivision),
        new("E", "Ellipse", "E", "click a face · drag the radius", "Ellipse tool", ToolKind.Ellipse)
    };

    private readonly List<(TrayTool Tool, Control Cell)> _trayCells = new();
    private readonly Dictionary<string, TextBlock> _title = new();
    private readonly List<Button> _densitySteps = new();
    private TextBox _zoomBox = null!;
    private string _zoomShown = "";
    private string? _themePalette;
    private string _notesKey = "";
    private bool _chromePending;
    private List<Command> _findRows = new();

    // Detailed panels (Ctrl+K). View state: not saved, not undoable, compact on launch.
    internal bool Detailed { get; private set; }
    private int _findIndex;

    internal static string ToolHint(ToolKind? tool)
    {
        return tool switch
        {
            ToolKind.Box => "Drag width, then depth, then height · 1 / 2 force an axis · Esc leaves",
            ToolKind.Subdivision => "Click a box face or four corners · 2-9 cells · Enter commits",
            ToolKind.Ellipse => "Click a box face or four corners, then drag from the centre",
            _ => "Drag a point to move it · 1-9 drops a point at the cursor · right-drag pulls a trace line"
        };
    }

    private void BuildSheet()
    {
        TopRuler.Viewport = Viewport;
        LeftRuler.Viewport = Viewport;
        BuildTray();
        BuildTitleBlock();

        // Only typing opens the list; clearing the field on close must not reopen it.
        FindBox.TextChanged += (_, _) =>
        {
            if (FindBox.IsFocused)
            {
                _findIndex = 0;
                ShowFind();
            }
        };
        FindBox.GotFocus += (_, _) => ShowFind();
        FindBox.LostFocus += (_, _) =>
        {
            if (!FindPopup.IsPointerOverPopup)
            {
                FindPopup.IsOpen = false;
            }
        };
        // Tunnel: the text box would otherwise take the arrows and Enter itself.
        FindBox.AddHandler(KeyDownEvent, FindBox_KeyDown, RoutingStrategies.Tunnel);

        Viewport.ViewChanged += ScheduleChrome;
        Viewport.World.StatusChanged += ScheduleChrome;
        Viewport.CurrentFileChanged += ScheduleChrome;
        Viewport.World.Changed += ScheduleChrome;
        ModeButton.Click += (_, _) => RunAsync(() => Viewport.RunCommandAsync("Detailed panels"));
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
        }

        TopRuler.InvalidateVisual();
        LeftRuler.InvalidateVisual();
        UpdateTray();
        UpdateTitleBlock();
        UpdateNotes();
        UpdateInspector();
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
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("39,*"), MinHeight = 39 };
            grid.Children.Add(new TextBlock
            {
                Text = tool.Letter,
                FontSize = 19,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            var label = new StackPanel { Margin = new Thickness(2, 4, 8, 4), VerticalAlignment = VerticalAlignment.Center };
            label.Children.Add(new TextBlock { Text = tool.Name });
            label.Children.Add(new TextBlock { Classes = { "caps" }, Text = tool.Gesture.ToUpperInvariant() });
            label.Children.Add(new TextBlock
            {
                Classes = { "options" },
                Text = tool.Options,
                TextWrapping = TextWrapping.Wrap,
                IsVisible = false
            });
            Grid.SetColumn(label, 1);
            grid.Children.Add(label);

            Control cell;
            if (tool.Command is { } name)
            {
                var button = new Button
                {
                    Classes = { "cell" },
                    Content = grid,
                    BorderThickness = new Thickness(0, 0, 0, 1)
                };
                AutomationProperties.SetName(button, $"{tool.Name} tool");
                button.Click += (_, _) => RunAsync(() => Viewport.RunCommandAsync(name));
                cell = button;
            }
            else
            {
                cell = new Border { Child = grid, BorderThickness = new Thickness(0, 0, 0, 1) };
                cell.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Ink"));
            }

            TrayPanel.Children.Add(cell);
            _trayCells.Add((tool, cell));
        }
    }

    private void UpdateTray()
    {
        foreach ((TrayTool tool, Control cell) in _trayCells)
        {
            bool active = tool.Command != null && Viewport.Tool == tool.Kind;
            cell.Classes.Set("on", active);
            foreach (TextBlock options in cell.GetLogicalDescendants().OfType<TextBlock>()
                .Where(block => block.Classes.Contains("options")))
            {
                options.IsVisible = active && Detailed;
            }
        }
    }

    private void BuildTitleBlock()
    {
        // ISO 7200: small spaced label over the value. Zoom and point are set large.
        void Cell(string key, string label, int row, int column, bool big = false)
        {
            var value = new TextBlock { Classes = { big ? "big" : "value" } };
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Classes = { "caps" }, Text = label });
            panel.Children.Add(value);
            if (key == "point")
            {
                var coords = new TextBlock();
                panel.Children.Add(coords);
                _title["coords"] = coords;
            }

            var border = new Border
            {
                Classes = { "cell" },
                Child = panel,
                BorderThickness = new Thickness(column == 0 ? 0 : 1, row == 0 ? 0 : 1, 0, 0)
            };
            Grid.SetRow(border, row);
            Grid.SetColumn(border, column);
            Grid.SetRowSpan(border, big ? 2 : 1);
            TitleBlock.Children.Add(border);
            _title[key] = value;
        }

        Cell("zoom", "ZOOM", 0, 0, big: true);
        Cell("point", "POINT", 0, 1, big: true);
        Cell("density", "DENSITY", 0, 2);
        Cell("cone", "CONE", 0, 3);
        Cell("tool", "TOOL", 0, 4);
        Cell("sheet", "SHEET", 1, 2);
        Cell("size", "SIZE", 1, 3);
        Cell("palette", "PALETTE", 1, 4);

        // Detailed mode: zoom typed in place, density stepped by its two commands.
        _zoomBox = new TextBox
        {
            FontSize = 17,
            FontWeight = FontWeight.Bold,
            MinHeight = 0,
            MinWidth = 0,
            Width = 70,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0, 0, 0, 1),
            IsVisible = false
        };
        AutomationProperties.SetName(_zoomBox, "Zoom percent");
        _zoomBox.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Escape)
            {
                if (e.Key == Key.Enter)
                {
                    ApplyZoomBox();
                }
                else
                {
                    _zoomBox.Text = _zoomShown;
                }

                Viewport.Focus();
                e.Handled = true;
            }
        };
        _zoomBox.LostFocus += (_, _) => ApplyZoomBox();
        ((StackPanel)_title["zoom"].Parent!).Children.Add(_zoomBox);

        var densityPanel = (StackPanel)_title["density"].Parent!;
        densityPanel.Children.Remove(_title["density"]);
        var steps = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach ((string text, string command) in new[] { ("-", "Lower density"), ("+", "Raise density") })
        {
            var step = new Button
            {
                Classes = { "cell" },
                Content = new TextBlock { Text = text },
                Padding = new Thickness(4, 0),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left,
                IsVisible = false
            };
            AutomationProperties.SetName(step, command);
            step.Click += (_, _) => RunAsync(() => Viewport.RunCommandAsync(command));
            _densitySteps.Add(step);
            steps.Children.Add(step);
            if (text == "-")
            {
                steps.Children.Add(_title["density"]);
            }
        }

        densityPanel.Children.Add(steps);
    }

    // Only an edited value applies: the shown value is rounded, and re-applying it would move the view.
    private void ApplyZoomBox()
    {
        if (_zoomBox.Text == _zoomShown)
        {
            return;
        }

        string text = (_zoomBox.Text ?? "").Trim().TrimEnd('%');
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double percent) &&
            double.IsFinite(percent))
        {
            Viewport.ZoomAt(new Point(Viewport.Bounds.Width / 2, Viewport.Bounds.Height / 2), percent / 100);
        }

        ScheduleChrome();
    }

    private static void Set(TextBlock block, string text)
    {
        if (block.Text != text)
        {
            block.Text = text;
        }
    }

    private void UpdateTitleBlock()
    {
        World world = Viewport.World;
        CultureInfo inv = CultureInfo.InvariantCulture;
        string zoom = string.Format(inv, "{0:0}%", world.Zoom * 100);
        Set(_title["zoom"], zoom);
        _title["zoom"].IsVisible = !Detailed;
        _zoomBox.IsVisible = Detailed;
        if (!_zoomBox.IsFocused && _zoomBox.Text != zoom)
        {
            _zoomBox.Text = zoom;
        }

        _zoomShown = zoom;

        foreach (Button step in _densitySteps)
        {
            step.IsVisible = Detailed;
        }

        if (world.SelectionIdx >= 0 && world.VanishingPoints[world.SelectionIdx] is { } point)
        {
            Set(_title["point"], $"VP {world.SelectionIdx + 1}");
            Set(_title["coords"], string.Format(inv, "{0:0}, {1:0}",
                point.Pos.X + world.PicturePlaneWidthHalf, point.Pos.Y + world.PicturePlaneHeightHalf));
            Set(_title["density"], point.Density == 0 ? "auto" : (point.Density / 4).ToString(inv));
        }
        else
        {
            Set(_title["point"], "none");
            Set(_title["coords"], "");
            Set(_title["density"], "-");
        }

        string cone = "off";
        if (world.ShowCone)
        {
            cone = ConeOfVision.TryBuild(world.VanishingPoints[0]?.Pos, world.VanishingPoints[1]?.Pos,
                world.PicturePlaneWidth, out ConeOfVision built)
                ? string.Format(inv, "{0:0}° · d {1:0}", built.FovDegrees, built.Distance)
                : "needs two points";
        }

        Set(_title["cone"], cone);
        Set(_title["tool"], TrayTools.First(tool => tool.Kind == Viewport.Tool && tool.Command != null).Name);
        Set(_title["sheet"], Viewport.CurrentFile?.Name ?? "untitled");
        Set(_title["size"], string.Format(inv, "{0} × {1}", world.PicturePlaneWidth, world.PicturePlaneHeight));
        Set(_title["palette"], world.PaletteName);
    }

    // NOTE 1 is the tool's hint; later notes are prompts, marked with a square in the prompt colour.
    internal List<string> NoteTexts()
    {
        World world = Viewport.World;
        var notes = new List<string> { ToolHint(Viewport.Tool) };
        if (world.StatusMessage is { } message)
        {
            notes.Add(message);
        }

        if (world.TraceLines.Count > 1 &&
            Geom.GetLineIntersection(world.TraceLines[0].Start, world.TraceLines[0].End,
                world.TraceLines[1].Start, world.TraceLines[1].End, world.CrazyFar, out _))
        {
            notes.Add("Trace lines cross: Ctrl+Shift+1-9 drops a point there");
        }

        return notes;
    }

    private void UpdateNotes()
    {
        List<string> notes = NoteTexts();
        string key = string.Join("\n", notes);
        if (key == _notesKey)
        {
            return;
        }

        _notesKey = key;
        Notes.Children.Clear();
        for (int i = 0; i < notes.Count; i++)
        {
            // A dock panel, so the text gets the remaining width and trims instead of running on.
            var row = new DockPanel();
            var label = new TextBlock
            {
                Classes = { "caps" },
                Text = $"NOTE {i + 1}",
                Width = 56,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(label, Dock.Left);
            row.Children.Add(label);
            if (i > 0)
            {
                var mark = new Border { Width = 8, Height = 8, Margin = new Thickness(0, 0, 6, 0) };
                mark.Bind(Border.BackgroundProperty, this.GetResourceObservable("Prompt"));
                DockPanel.SetDock(mark, Dock.Left);
                row.Children.Add(mark);
            }

            row.Children.Add(new TextBlock { Text = notes[i], TextTrimming = TextTrimming.CharacterEllipsis });
            Notes.Children.Add(row);
        }
    }

    private void ShowFind()
    {
        _findRows = Commands.Find(Viewport.Commands, FindBox.Text ?? "");
        _findIndex = Math.Clamp(_findIndex, 0, Math.Max(0, _findRows.Count - 1));
        FindList.Children.Clear();
        if (_findRows.Count == 0)
        {
            var empty = new Panel { Height = 25 };
            var hatch = new Hatch();
            hatch.Bind(Hatch.StrokeProperty, this.GetResourceObservable("Hair"));
            empty.Children.Add(hatch);
            empty.Children.Add(new TextBlock { Text = "No command matches", Margin = new Thickness(8, 4) });
            FindList.Children.Add(empty);
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
            row.Bind(Button.BorderBrushProperty, this.GetResourceObservable("Hair"));
            row.Classes.Set("on", i == _findIndex);
            row.Click += (_, _) => RunFind(index);
            FindList.Children.Add(row);
        }

        FindPopup.IsOpen = true;
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

    private void CloseFind()
    {
        FindBox.Text = "";
        FindPopup.IsOpen = false;
        _findIndex = 0;
        Viewport.Focus();
    }
}

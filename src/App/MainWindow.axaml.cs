using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Chitin;

public partial class MainWindow : Window
{
    private bool _forceClose;
    private bool _confirmingClose;
    private readonly Dictionary<string, MenuItem> _menuItems = new();

    public MainWindow()
    {
        InitializeComponent();
        Viewport.AddCommand(new Command("Points panel", "View", "Ctrl+K",
            new[] { new KeyGesture(Key.K, KeyModifiers.Control) }, _ =>
            {
                TogglePoints();
                return Task.CompletedTask;
            }, InMenu: true, Separator: true));
        Viewport.AddCommand(new Command("Find a command", "View", "Ctrl+F",
            new[] { new KeyGesture(Key.F, KeyModifiers.Control) }, _ =>
            {
                OpenFind();
                return Task.CompletedTask;
            }, InMenu: true));
        Viewport.AddCommand(new Command("Keys and gestures", "Help", "F1",
            new[] { new KeyGesture(Key.F1) }, _ => ShowKeysAsync(), InMenu: true));
        Viewport.AddCommand(new Command("About...", "Help", "", Array.Empty<KeyGesture>(),
            _ => new AboutWindow().ShowDialog(this), InMenu: true));
        BuildMenu();
        BuildSheet();
        Opened += (_, _) =>
        {
            Viewport.World.DirtyChanged += UpdateTitle;
            Viewport.CurrentFileChanged += UpdateTitle;
            Viewport.Focus();
        };
        Closing += Window_Closing;
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        Title = WindowTitle(Viewport.CurrentFile?.Name, Viewport.World.IsDirty);
    }

    internal static string WindowTitle(string? fileName, bool dirty)
    {
        return $"{fileName ?? "Untitled"}{(dirty ? " *" : "")} - Chitin";
    }

    private async void Window_Closing(object? sender, WindowClosingEventArgs e)
    {
        try
        {
            if (_forceClose || !Viewport.World.IsDirty)
            {
                return;
            }

            e.Cancel = true;
            if (_confirmingClose)
            {
                return;
            }

            _confirmingClose = true;
            if (await Viewport.ConfirmDiscardAsync())
            {
                _forceClose = true;
                Close();
            }
        }
        catch (Exception exception)
        {
            Viewport.World.SetError($"Close failed: {exception.Message}");
        }
        finally
        {
            _confirmingClose = false;
        }
    }

    private void Menu_Closed(object? sender, RoutedEventArgs e)
    {
        Viewport.Focus();
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        // In a text field (Find, zoom) only the panel keys pass: Ctrl+Z there must not undo the document.
        if (FocusManager?.GetFocusedElement() is TextBox && e.Key is not (Key.K or Key.F))
        {
            return;
        }

        // Focus is elsewhere (the menu): shortcuts with Ctrl or Alt still reach the viewport.
        // Other keys stay with the menu, which uses arrows, Enter and Alt to navigate.
        if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt)) != 0 &&
            Viewport.HasKey(e.Key, e.KeyModifiers))
        {
            Viewport.ProcessKey(e);
        }
    }

    // One top-level menu per group, in table order; each item runs its command.
    private void BuildMenu()
    {
        var menus = new Dictionary<string, MenuItem>();
        foreach (Command command in Viewport.Commands.Where(command => command.InMenu))
        {
            if (!menus.TryGetValue(command.Group, out MenuItem? top))
            {
                top = new MenuItem { Header = command.Group };
                menus[command.Group] = top;
                MainMenu.Items.Add(top);
            }

            if (command.Separator && top.Items.Count > 0)
            {
                top.Items.Add(new Separator());
            }

            var item = new MenuItem { Header = command.Name, InputGesture = command.Keys.FirstOrDefault() };
            item.Click += (_, _) => RunAsync(() => Viewport.RunCommandAsync(command));
            top.Items.Add(item);
            _menuItems[command.Name] = item;
        }

        menus["Edit"].SubmenuOpened += EditMenu_Opened;
    }

    internal MenuItem MenuItemFor(string name) => _menuItems[name];

    private void EditMenu_Opened(object? sender, RoutedEventArgs e)
    {
        // The item names the command it reverts: "Undo Move vanishing point".
        _menuItems["Undo"].IsEnabled = Viewport.History.CanUndo;
        _menuItems["Undo"].Header = Viewport.History.UndoName is { } undo ? $"Undo {undo}" : "Undo";
        _menuItems["Redo"].IsEnabled = Viewport.History.CanRedo;
        _menuItems["Redo"].Header = Viewport.History.RedoName is { } redo ? $"Redo {redo}" : "Redo";
    }

    // The one catch for menu commands; the viewport methods report their own expected failures.
    private async void RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            Viewport.World.SetError($"Command failed: {exception.Message}");
        }
        finally
        {
            Viewport.InvalidateVisual();
            Viewport.Focus();
        }
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Chitin;

// The fields clamp to 1-8192 themselves, so the dialog needs no validation of its own.
public partial class ResizeDialog : Window
{
    public int NewWidth { get; private set; }
    public int NewHeight { get; private set; }

    private readonly int _width;
    private readonly int _height;

    public ResizeDialog() : this(640, 480)
    {
    }

    public ResizeDialog(int width, int height)
    {
        InitializeComponent();
        _width = width;
        _height = height;
        WidthBox.Value = width;
        HeightBox.Value = height;
    }

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        NewWidth = (int)(WidthBox.Value ?? _width);
        NewHeight = (int)(HeightBox.Value ?? _height);
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Chitin;

// The fields clamp to 1-8192 themselves, so the dialog needs no validation of its own.
public partial class ResizeDialog : Window
{
    public int NewWidth { get; private set; }
    public int NewHeight { get; private set; }

    public ResizeDialog() : this(640, 480)
    {
    }

    public ResizeDialog(int width, int height)
    {
        InitializeComponent();
        WidthBox.Value = width;
        HeightBox.Value = height;
    }

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        NewWidth = (int)(WidthBox.Value ?? World.MinDocumentSize);
        NewHeight = (int)(HeightBox.Value ?? World.MinDocumentSize);
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}

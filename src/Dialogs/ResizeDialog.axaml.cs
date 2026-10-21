using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Chitin;

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
        this.FindControl<TextBox>("WidthBox")!.Text = width.ToString();
        this.FindControl<TextBox>("HeightBox")!.Text = height.ToString();
        this.FindControl<TextBox>("WidthBox")!.TextChanged += (_, _) => Validate();
        this.FindControl<TextBox>("HeightBox")!.TextChanged += (_, _) => Validate();
        Validate();
    }

    private void Validate()
    {
        bool validWidth = int.TryParse(this.FindControl<TextBox>("WidthBox")!.Text, out int width) &&
            width >= World.MinDocumentSize && width <= World.MaxDocumentSize;
        bool validHeight = int.TryParse(this.FindControl<TextBox>("HeightBox")!.Text, out int height) &&
            height >= World.MinDocumentSize && height <= World.MaxDocumentSize;
        bool valid = validWidth && validHeight;

        this.FindControl<Button>("OkButton")!.IsEnabled = valid;
        TextBlock validation = this.FindControl<TextBlock>("ValidationText")!;
        validation.IsVisible = !valid;
        validation.Text = valid
            ? string.Empty
            : $"Use whole numbers between {World.MinDocumentSize} and {World.MaxDocumentSize}.";
    }

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        if (!int.TryParse(this.FindControl<TextBox>("WidthBox")!.Text, out int width) ||
            !int.TryParse(this.FindControl<TextBox>("HeightBox")!.Text, out int height) ||
            width < World.MinDocumentSize || width > World.MaxDocumentSize ||
            height < World.MinDocumentSize || height > World.MaxDocumentSize)
        {
            return;
        }

        NewWidth = width;
        NewHeight = height;
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}

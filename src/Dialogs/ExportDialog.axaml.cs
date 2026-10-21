using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Chitin;

public partial class ExportDialog : Window
{
    public bool IsSvg { get; private set; }
    public int OutputWidth { get; private set; }
    public int OutputHeight { get; private set; }
    public bool Transparent { get; private set; }

    private readonly int _planeWidth;
    private readonly int _planeHeight;
    private bool _linking;

    public ExportDialog() : this(640, 480)
    {
    }

    public ExportDialog(int width, int height)
    {
        InitializeComponent();
        _planeWidth = width;
        _planeHeight = height;
        OutputWidth = width;
        OutputHeight = height;
        NumericUpDown widthBox = this.FindControl<NumericUpDown>("WidthBox")!;
        NumericUpDown heightBox = this.FindControl<NumericUpDown>("HeightBox")!;
        // The output keeps the plane's aspect ratio, so PNG and SVG show the same area.
        int longest = Math.Max(width, height);
        widthBox.Maximum = Math.Max(1, World.MaxDocumentSize * width / longest);
        heightBox.Maximum = Math.Max(1, World.MaxDocumentSize * height / longest);
        widthBox.Value = width;
        heightBox.Value = height;
        widthBox.ValueChanged += (_, _) => Link(widthBox, heightBox, _planeWidth, _planeHeight);
        heightBox.ValueChanged += (_, _) => Link(heightBox, widthBox, _planeHeight, _planeWidth);
        this.FindControl<RadioButton>("PngRadio")!.IsCheckedChanged += (_, _) => UpdateFormat();
        this.FindControl<RadioButton>("SvgRadio")!.IsCheckedChanged += (_, _) => UpdateFormat();
        UpdateFormat();
    }

    private void Link(NumericUpDown source, NumericUpDown target, int sourceSize, int targetSize)
    {
        if (_linking || source.Value is not { } value)
        {
            return;
        }

        _linking = true;
        target.Value = Math.Clamp(Math.Round(value * targetSize / sourceSize), 1, target.Maximum);
        _linking = false;
    }

    private void UpdateFormat()
    {
        bool svg = this.FindControl<RadioButton>("SvgRadio")!.IsChecked == true;
        this.FindControl<CheckBox>("TransparentBox")!.IsEnabled = !svg;
    }

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        IsSvg = this.FindControl<RadioButton>("SvgRadio")!.IsChecked == true;
        OutputWidth = (int)(this.FindControl<NumericUpDown>("WidthBox")!.Value ?? 640);
        OutputHeight = (int)(this.FindControl<NumericUpDown>("HeightBox")!.Value ?? 480);
        Transparent = !IsSvg && this.FindControl<CheckBox>("TransparentBox")!.IsChecked == true;
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}

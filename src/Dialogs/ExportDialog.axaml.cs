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
        // The output keeps the plane's aspect ratio, so PNG and SVG show the same area.
        int longest = Math.Max(width, height);
        WidthBox.Maximum = Math.Max(1, World.MaxDocumentSize * width / longest);
        HeightBox.Maximum = Math.Max(1, World.MaxDocumentSize * height / longest);
        WidthBox.Value = width;
        HeightBox.Value = height;
        WidthBox.ValueChanged += (_, _) => Link(WidthBox, HeightBox, _planeWidth, _planeHeight);
        HeightBox.ValueChanged += (_, _) => Link(HeightBox, WidthBox, _planeHeight, _planeWidth);
        PngRadio.IsCheckedChanged += (_, _) => UpdateFormat();
        SvgRadio.IsCheckedChanged += (_, _) => UpdateFormat();
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
        TransparentBox.IsEnabled = SvgRadio.IsChecked != true;
    }

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        IsSvg = SvgRadio.IsChecked == true;
        OutputWidth = (int)(WidthBox.Value ?? _planeWidth);
        OutputHeight = (int)(HeightBox.Value ?? _planeHeight);
        Transparent = !IsSvg && TransparentBox.IsChecked == true;
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}

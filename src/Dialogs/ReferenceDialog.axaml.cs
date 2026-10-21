using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Chitin;

public partial class ReferenceDialog : Window
{
    private readonly World _world;
    private readonly Action _changed;
    private bool _loading;
    private bool _hidden;

    public ReferenceDialog() : this(new World(), () => { })
    {
    }

    public ReferenceDialog(World world, Action changed)
    {
        InitializeComponent();
        _world = world;
        _changed = changed;

        _loading = true;
        ReferenceSettings settings = world.Reference;
        _hidden = settings.Hidden;
        this.FindControl<Slider>("OpacitySlider")!.Value = settings.Opacity * 100;
        this.FindControl<Slider>("ScaleSlider")!.Value = settings.Scale * 100;
        this.FindControl<CheckBox>("FlipHBox")!.IsChecked = settings.FlipH;
        this.FindControl<CheckBox>("FlipVBox")!.IsChecked = settings.FlipV;
        this.FindControl<NumericUpDown>("OffsetXBox")!.Value = (decimal)settings.Offset.X;
        this.FindControl<NumericUpDown>("OffsetYBox")!.Value = (decimal)settings.Offset.Y;

        this.FindControl<Slider>("OpacitySlider")!.ValueChanged += (_, _) => Apply();
        this.FindControl<Slider>("ScaleSlider")!.ValueChanged += (_, _) => Apply();
        this.FindControl<CheckBox>("FlipHBox")!.IsCheckedChanged += (_, _) => Apply();
        this.FindControl<CheckBox>("FlipVBox")!.IsCheckedChanged += (_, _) => Apply();
        this.FindControl<NumericUpDown>("OffsetXBox")!.ValueChanged += (_, _) => Apply();
        this.FindControl<NumericUpDown>("OffsetYBox")!.ValueChanged += (_, _) => Apply();
        _loading = false;
    }

    private void Apply()
    {
        if (_loading)
        {
            return;
        }

        _world.SetReferenceSettings(new ReferenceSettings
        {
            Opacity = this.FindControl<Slider>("OpacitySlider")!.Value / 100,
            Scale = this.FindControl<Slider>("ScaleSlider")!.Value / 100,
            FlipH = this.FindControl<CheckBox>("FlipHBox")!.IsChecked == true,
            FlipV = this.FindControl<CheckBox>("FlipVBox")!.IsChecked == true,
            Offset = new Vec2(
                (double)(this.FindControl<NumericUpDown>("OffsetXBox")!.Value ?? 0),
                (double)(this.FindControl<NumericUpDown>("OffsetYBox")!.Value ?? 0)),
            Hidden = _hidden
        });
        _changed();
    }

    private void Fit_Click(object? sender, RoutedEventArgs e)
    {
        _loading = true;
        this.FindControl<Slider>("ScaleSlider")!.Value = 100;
        this.FindControl<NumericUpDown>("OffsetXBox")!.Value = 0;
        this.FindControl<NumericUpDown>("OffsetYBox")!.Value = 0;
        _loading = false;
        Apply();
    }

    private void Reset_Click(object? sender, RoutedEventArgs e)
    {
        _hidden = false;
        _loading = true;
        this.FindControl<Slider>("OpacitySlider")!.Value = 100;
        this.FindControl<Slider>("ScaleSlider")!.Value = 100;
        this.FindControl<CheckBox>("FlipHBox")!.IsChecked = false;
        this.FindControl<CheckBox>("FlipVBox")!.IsChecked = false;
        this.FindControl<NumericUpDown>("OffsetXBox")!.Value = 0;
        this.FindControl<NumericUpDown>("OffsetYBox")!.Value = 0;
        _loading = false;
        Apply();
    }

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        Apply();
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}

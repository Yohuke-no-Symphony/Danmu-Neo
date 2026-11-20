using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Services.Appearance;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>根据自身和父级背景的实际叠色提供可读的文字画刷。</summary>
public sealed class AdaptiveForegroundBorder : Border
{
    /// <summary>对比度足够时使用的文字画刷。</summary>
    public static readonly StyledProperty<IBrush?> PreferredForegroundProperty =
        AvaloniaProperty.Register<AdaptiveForegroundBorder, IBrush?>(nameof(PreferredForeground));

    /// <summary>根据背景计算的只读文字画刷。</summary>
    public static readonly DirectProperty<AdaptiveForegroundBorder, IBrush> ForegroundProperty =
        AvaloniaProperty.RegisterDirect<AdaptiveForegroundBorder, IBrush>(nameof(Foreground), border => border.Foreground);

    private readonly List<Visual> backgroundAncestors = [];
    private readonly HashSet<AvaloniaObject> observedBrushes = [];
    private IBrush foreground = Brushes.Black;
    private bool isAttached;

    /// <summary>首选文字画刷。</summary>
    public IBrush? PreferredForeground
    {
        get => GetValue(PreferredForegroundProperty);
        set => SetValue(PreferredForegroundProperty, value);
    }

    /// <summary>子控件可绑定的文字画刷。</summary>
    public IBrush Foreground => foreground;

    /// <summary>创建随主题变化更新文字对比度的边框。</summary>
    public AdaptiveForegroundBorder()
    {
        ActualThemeVariantChanged += OnActualThemeVariantChanged;
    }

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(Border);

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        isAttached = true;
        foreach (var ancestor in this.GetVisualAncestors())
        {
            if (ancestor is Border or Panel or ContentPresenter or TextBlock or TopLevel)
            {
                backgroundAncestors.Add(ancestor);
                ancestor.PropertyChanged += OnAncestorPropertyChanged;
            }
        }

        RefreshBrushSubscriptions();
        UpdateForeground();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        isAttached = false;
        foreach (var ancestor in backgroundAncestors)
        {
            ancestor.PropertyChanged -= OnAncestorPropertyChanged;
        }

        backgroundAncestors.Clear();
        ClearBrushSubscriptions();
        base.OnDetachedFromVisualTree(e);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BackgroundProperty || change.Property == PreferredForegroundProperty)
        {
            if (isAttached)
            {
                RefreshBrushSubscriptions();
            }

            UpdateForeground();
        }
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        UpdateForeground();
    }

    private void OnAncestorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property.Name == nameof(Background))
        {
            RefreshBrushSubscriptions();
            UpdateForeground();
        }
    }

    private void RefreshBrushSubscriptions()
    {
        ClearBrushSubscriptions();
        ObserveBrush(Background);
        ObserveBrush(PreferredForeground);
        foreach (var ancestor in backgroundAncestors)
        {
            ObserveBrush(GetBackground(ancestor));
        }
    }

    private void ObserveBrush(IBrush? brush)
    {
        if (brush is AvaloniaObject observable && observedBrushes.Add(observable))
        {
            observable.PropertyChanged += OnBrushPropertyChanged;
        }
    }

    private void ClearBrushSubscriptions()
    {
        foreach (var brush in observedBrushes)
        {
            brush.PropertyChanged -= OnBrushPropertyChanged;
        }

        observedBrushes.Clear();
    }

    private void OnBrushPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == SolidColorBrush.ColorProperty || change.Property == Brush.OpacityProperty)
        {
            UpdateForeground();
        }
    }

    private void UpdateForeground()
    {
        var background = ActualThemeVariant == ThemeVariant.Dark ? Colors.Black : Colors.White;
        for (var index = backgroundAncestors.Count - 1; index >= 0; index--)
        {
            if (GetBackground(backgroundAncestors[index]) is ISolidColorBrush brush)
            {
                background = ColorContrast.CompositeOverOpaqueBackground(brush, background);
            }
        }

        if (Background is ISolidColorBrush ownBackground)
        {
            background = ColorContrast.CompositeOverOpaqueBackground(ownBackground, background);
        }

        SetAndRaise(ForegroundProperty, ref foreground,
            ColorContrast.ResolveForeground(background, PreferredForeground as ISolidColorBrush));
    }

    private static IBrush? GetBackground(Visual visual)
    {
        return visual switch
        {
            Border border => border.Background,
            Panel panel => panel.Background,
            ContentPresenter presenter => presenter.Background,
            TextBlock text => text.Background,
            TopLevel topLevel => topLevel.Background,
            _ => null
        };
    }
}

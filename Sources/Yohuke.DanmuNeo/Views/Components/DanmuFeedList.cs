using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Models.State;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>联动所有弹幕行的列宽，并在结束拖动时提交比例。</summary>
public sealed class DanmuFeedList : ItemsControl
{
    private const double KEYBOARD_RATIO_STEP = 0.02;
    private GridLength identityColumnWidth = new(DanmuColumnWidths.DEFAULT_IDENTITY_RATIO, GridUnitType.Star);
    private GridLength bodyColumnWidth = new(1 - DanmuColumnWidths.DEFAULT_IDENTITY_RATIO, GridUnitType.Star);
    private double displayedRatio = DanmuColumnWidths.DEFAULT_IDENTITY_RATIO;
    private IPointer? resizePointer;
    private TopLevel? coordinateRoot;
    private double startingX;
    private (double Width, double Ratio, double Minimum, double Maximum) dragGeometry;

    /// <summary>拖动结束后提交的用户名区比例。</summary>
    public static readonly StyledProperty<double> IdentityColumnRatioProperty =
        AvaloniaProperty.Register<DanmuFeedList, double>(nameof(IdentityColumnRatio),
            DanmuColumnWidths.DEFAULT_IDENTITY_RATIO, defaultBindingMode: BindingMode.TwoWay,
            coerce: static (_, ratio) => DanmuColumnWidths.ClampIdentityRatio(ratio));

    /// <summary>用于区分普通与紧凑模式的拖动事务。</summary>
    public static readonly StyledProperty<bool> IsCompactDisplayProperty =
        AvaloniaProperty.Register<DanmuFeedList, bool>(nameof(IsCompactDisplay));

    /// <summary>所有弹幕行共用的用户名区宽度。</summary>
    public static readonly DirectProperty<DanmuFeedList, GridLength> IdentityColumnWidthProperty =
        AvaloniaProperty.RegisterDirect<DanmuFeedList, GridLength>(nameof(IdentityColumnWidth), list => list.IdentityColumnWidth);

    /// <summary>所有弹幕行共用的正文区宽度。</summary>
    public static readonly DirectProperty<DanmuFeedList, GridLength> BodyColumnWidthProperty =
        AvaloniaProperty.RegisterDirect<DanmuFeedList, GridLength>(nameof(BodyColumnWidth), list => list.BodyColumnWidth);

    /// <summary>连接行内分割线的指针及键盘事件。</summary>
    public DanmuFeedList()
    {
        AddHandler(PointerPressedEvent, OnDividerPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnDividerKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>当前模式在配置中保存的比例。</summary>
    public double IdentityColumnRatio
    {
        get => GetValue(IdentityColumnRatioProperty);
        set => SetValue(IdentityColumnRatioProperty, value);
    }

    /// <summary>是否正在显示紧凑弹幕。</summary>
    public bool IsCompactDisplay
    {
        get => GetValue(IsCompactDisplayProperty);
        set => SetValue(IsCompactDisplayProperty, value);
    }

    /// <summary>当前用户名区宽度，拖动中随指针更新。</summary>
    public GridLength IdentityColumnWidth => identityColumnWidth;

    /// <summary>当前正文区宽度，拖动中随指针更新。</summary>
    public GridLength BodyColumnWidth => bodyColumnWidth;

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(ItemsControl);

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IdentityColumnRatioProperty || change.Property == IsCompactDisplayProperty)
        {
            FinishResize(save: false);
            ApplyColumnWidths(IdentityColumnRatio);
        }
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (ReferenceEquals(e.Pointer, resizePointer))
        {
            e.Handled = true;
            UpdateDrag(e);
        }
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (ReferenceEquals(e.Pointer, resizePointer) && e.InitialPressMouseButton == MouseButton.Left)
        {
            e.Handled = true;
            UpdateDrag(e);
            FinishResize(save: true);
        }
    }

    /// <inheritdoc />
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (ReferenceEquals(e.Pointer, resizePointer))
        {
            FinishResize(save: true);
        }
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        FinishResize(save: false);
        base.OnDetachedFromVisualTree(e);
    }

    private void OnDividerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (FindDivider(e.Source) is not { } divider)
        {
            return;
        }

        e.Handled = true;
        e.PreventGestureRecognition();
        if (resizePointer is not null ||
            e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed ||
            GetResizeGeometry(divider) is not { } geometry || TopLevel.GetTopLevel(this) is not { } root)
        {
            return;
        }

        divider.Focus(NavigationMethod.Pointer);
        resizePointer = e.Pointer;
        coordinateRoot = root;
        startingX = e.GetPosition(root).X;
        dragGeometry = geometry;
        PseudoClasses.Set(":resizingColumns", true);
        e.Pointer.Capture(this);
    }

    private void OnDividerKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Left or Key.Right) || e.KeyModifiers != KeyModifiers.None ||
            FindDivider(e.Source) is not { } divider)
        {
            return;
        }

        e.Handled = true;
        if (resizePointer is null && GetResizeGeometry(divider) is { } geometry)
        {
            var delta = e.Key == Key.Right ? KEYBOARD_RATIO_STEP : -KEYBOARD_RATIO_STEP;
            var ratio = Math.Clamp(geometry.Ratio + delta, geometry.Minimum, geometry.Maximum);
            if (!ratio.Equals(geometry.Ratio))
            {
                SetCurrentValue(IdentityColumnRatioProperty, ratio);
            }
        }
    }

    private void UpdateDrag(PointerEventArgs e)
    {
        var delta = e.GetPosition(coordinateRoot).X - startingX;
        var ratio = Math.Clamp(dragGeometry.Ratio + delta / dragGeometry.Width, dragGeometry.Minimum, dragGeometry.Maximum);
        ApplyColumnWidths(ratio.Equals(dragGeometry.Ratio) ? IdentityColumnRatio : ratio);
    }

    private void FinishResize(bool save)
    {
        if (resizePointer is not { } pointer)
        {
            return;
        }

        resizePointer = null;
        coordinateRoot = null;
        PseudoClasses.Set(":resizingColumns", false);
        if (ReferenceEquals(pointer.Captured, this))
        {
            pointer.Capture(null);
        }

        if (save && !IdentityColumnRatio.Equals(displayedRatio))
        {
            SetCurrentValue(IdentityColumnRatioProperty, displayedRatio);
        }

        ApplyColumnWidths(IdentityColumnRatio);
    }

    private void ApplyColumnWidths(double ratio)
    {
        displayedRatio = ratio;
        SetAndRaise(IdentityColumnWidthProperty, ref identityColumnWidth, new(ratio, GridUnitType.Star));
        SetAndRaise(BodyColumnWidthProperty, ref bodyColumnWidth, new(1 - ratio, GridUnitType.Star));
    }

    private static Thumb? FindDivider(object? source)
    {
        var thumb = (source as Visual)?.FindAncestorOfType<Thumb>(includeSelf: true);
        return thumb?.Classes.Contains("danmuColumnDivider") == true ? thumb : null;
    }

    private static (double Width, double Ratio, double Minimum, double Maximum)? GetResizeGeometry(Thumb divider)
    {
        if (divider.Parent is not Grid { ColumnDefinitions.Count: 3 } row)
        {
            return null;
        }

        var identity = row.ColumnDefinitions[0];
        var body = row.ColumnDefinitions[2];
        var width = identity.ActualWidth + body.ActualWidth;
        if (width <= 0)
        {
            return null;
        }

        var minimum = Math.Max(DanmuColumnWidths.MIN_IDENTITY_RATIO, identity.MinWidth / width);
        var maximum = Math.Min(DanmuColumnWidths.MAX_IDENTITY_RATIO, 1 - body.MinWidth / width);
        return minimum < maximum ? (width, identity.ActualWidth / width, minimum, maximum) : null;
    }
}

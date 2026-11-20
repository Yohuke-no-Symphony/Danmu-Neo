using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>复用弹幕行的控件，取消消息换绑时仍悬挂的交互。</summary>
public sealed partial class DanmuFeedRow : Button
{
    private ContextMenu? activeMenu;
    private long bindingRevision;
    private long? activationRevision;

    /// <summary>创建工作台和样式预览共用的弹幕行。</summary>
    public DanmuFeedRow()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnActivationPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnActivationPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnActivationKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnActivationKeyUp, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(ClickEvent, OnActivationCompleted);
        AddHandler(ContextRequestedEvent, OnActivationCompleted);
    }

    internal void OpenMessageMenu(ContextMenu menu)
    {
        CloseActiveMenu();
        activeMenu = menu;
        menu.Closed += OnActiveMenuClosed;
        menu.Open(this);
    }

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(Button);

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        bindingRevision++;
        CloseActiveMenu();
        base.OnDataContextChanged(e);
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        bindingRevision++;
        CloseActiveMenu();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnActivationPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind is
            PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.RightButtonPressed)
        {
            activationRevision = bindingRevision;
        }
    }

    private void OnActivationPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton is MouseButton.Left or MouseButton.Right)
        {
            activationRevision = null;
        }
    }

    private void OnActivationKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space or Key.Apps || e.Key == Key.F10 && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            activationRevision = bindingRevision;
        }
    }

    private void OnActivationKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space or Key.Apps or Key.F10)
        {
            activationRevision = null;
        }
    }

    private void OnActivationCompleted(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DanmuFeedItem || activationRevision is { } revision && revision != bindingRevision)
        {
            e.Handled = true;
            return;
        }

        activationRevision = null;
    }

    private void OnActiveMenuClosed(object? sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu && ReferenceEquals(menu, activeMenu))
        {
            menu.Closed -= OnActiveMenuClosed;
            activeMenu = null;
        }
    }

    private void CloseActiveMenu()
    {
        if (activeMenu is not { } menu)
        {
            return;
        }

        activeMenu = null;
        menu.Closed -= OnActiveMenuClosed;
        menu.Close();
    }
}

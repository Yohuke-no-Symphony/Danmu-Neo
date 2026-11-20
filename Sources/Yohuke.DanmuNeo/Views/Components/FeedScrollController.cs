using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 在列表底部跟随新消息，向上翻阅时保留条目锚点。
/// </summary>
internal sealed class FeedScrollController : IDisposable
{
    private const double LATEST_OFFSET_THRESHOLD = 1;
    private readonly ItemsControl itemsControl;
    private ScrollViewer? scrollViewer;
    private INotifyCollectionChanged? items;
    private ScrollBar? verticalScrollBar;
    private Control? readingAnchor;
    private object? readingItem;
    private double readingAnchorY;
    private double readingOffset;
    private long session;
    private bool isScrollPending;
    private bool isFollowingLatest = true;
    private bool isUserScrollPending;
    private bool isScrollUpdateRequired;
    private bool isUpdatingScroll;
    private bool isDisposed;

    /// <summary>
    /// 连接使用外部滚动容器的消息列表。
    /// </summary>
    public FeedScrollController(ScrollViewer scrollViewer, ItemsControl itemsControl)
    {
        this.itemsControl = itemsControl;
        itemsControl.ContainerPrepared += OnContainerPrepared;
        itemsControl.ContainerClearing += OnContainerClearing;
        SetScrollViewer(scrollViewer);
    }

    /// <summary>
    /// 连接使用原生虚拟化和滚动容器的消息列表。
    /// </summary>
    public FeedScrollController(ListBox listBox)
    {
        itemsControl = listBox;
        listBox.PropertyChanged += OnListBoxPropertyChanged;
        SetScrollViewer(listBox.Scroll as ScrollViewer);
    }

    /// <summary>
    /// 切换监听集合；传入空集合时停止自动滚动。
    /// </summary>
    public void Attach(INotifyCollectionChanged? collection)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (items is not null)
        {
            items.CollectionChanged -= OnCollectionChanged;
        }

        items = collection;
        session++;
        isScrollPending = false;
        isUserScrollPending = false;
        isScrollUpdateRequired = false;
        readingAnchor = null;
        readingItem = null;

        if (items is not null)
        {
            items.CollectionChanged += OnCollectionChanged;
            ResetToLatest();
        }
    }

    /// <summary>
    /// 结束历史翻阅，回到底部并恢复新消息跟随。
    /// </summary>
    public void ScrollToLatest()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (items is not null)
        {
            ResetToLatest();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        Attach(null);
        itemsControl.ContainerPrepared -= OnContainerPrepared;
        itemsControl.ContainerClearing -= OnContainerClearing;
        itemsControl.PropertyChanged -= OnListBoxPropertyChanged;
        SetScrollViewer(null);
        isDisposed = true;
    }

    private void OnListBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ListBox.ScrollProperty && sender is ListBox listBox)
        {
            SetScrollViewer(listBox.Scroll as ScrollViewer);
        }
    }

    private void SetScrollViewer(ScrollViewer? viewer)
    {
        if (ReferenceEquals(scrollViewer, viewer))
        {
            return;
        }

        if (scrollViewer is { } previous)
        {
            previous.PropertyChanged -= OnScrollPropertyChanged;
            previous.LayoutUpdated -= OnLayoutUpdated;
            previous.TemplateApplied -= OnScrollTemplateApplied;
            previous.RemoveHandler(InputElement.PointerWheelChangedEvent, OnPointerWheelChanged);
            previous.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
            previous.RemoveHandler(Gestures.ScrollGestureEvent, OnScrollGesture);
            AttachScrollBar(null);

            if (itemsControl is not ListBox)
            {
                foreach (var container in itemsControl.GetRealizedContainers())
                {
                    previous.UnregisterAnchorCandidate(container);
                }
            }
        }

        session++;
        isScrollPending = false;
        isUserScrollPending = false;
        isScrollUpdateRequired = false;
        scrollViewer = viewer;

        if (viewer is null)
        {
            return;
        }

        viewer.PropertyChanged += OnScrollPropertyChanged;
        viewer.LayoutUpdated += OnLayoutUpdated;
        viewer.TemplateApplied += OnScrollTemplateApplied;
        viewer.AddHandler(InputElement.PointerWheelChangedEvent, OnPointerWheelChanged,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        viewer.AddHandler(InputElement.KeyDownEvent, OnKeyDown,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        viewer.AddHandler(Gestures.ScrollGestureEvent, OnScrollGesture, RoutingStrategies.Bubble, true);
        AttachScrollBar(viewer.GetVisualDescendants().OfType<ScrollBar>()
            .FirstOrDefault(control => control.Orientation == Orientation.Vertical));
        QueueScrollUpdate();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            ResetToLatest();
        }
        else
        {
            QueueScrollUpdate();
        }
    }

    private void OnScrollPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (items is null || isUpdatingScroll || sender is not ScrollViewer viewer)
        {
            return;
        }

        if (e.Property == ScrollViewer.OffsetProperty)
        {
            if (isUserScrollPending)
            {
                UpdateFollowingFromUserScroll();
            }
            else if (!isScrollPending && isFollowingLatest && !IsAtLatest(viewer))
            {
                isFollowingLatest = false;
                CaptureReadingAnchor();
            }
            else if (!isFollowingLatest && !isScrollPending)
            {
                QueueScrollUpdate();
            }
        }
        else if (isFollowingLatest &&
                 (e.Property == ScrollViewer.ExtentProperty || e.Property == ScrollViewer.ViewportProperty))
        {
            QueueScrollUpdate();
        }
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (items is not null && !isFollowingLatest && !isScrollPending && !isUpdatingScroll)
        {
            CaptureReadingAnchor();
        }
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y != 0 && e.KeyModifiers != KeyModifiers.Shift)
        {
            if (e.Route == RoutingStrategies.Tunnel)
            {
                BeginUserScroll();
            }
            else
            {
                CompleteUserScroll();
            }
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.PageUp or Key.PageDown or Key.Home or Key.End or Key.Up or Key.Down)
        {
            if (e.Route == RoutingStrategies.Tunnel)
            {
                BeginUserScroll();
            }
            else
            {
                CompleteUserScroll();
            }
        }
    }

    private void OnScrollTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        AttachScrollBar(e.NameScope.Find<ScrollBar>("PART_VerticalScrollBar"));
    }

    private void AttachScrollBar(ScrollBar? scrollBar)
    {
        if (verticalScrollBar is not null)
        {
            verticalScrollBar.Scroll -= OnScrollbarScroll;
        }

        verticalScrollBar = scrollBar;

        if (verticalScrollBar is not null)
        {
            verticalScrollBar.Scroll += OnScrollbarScroll;
        }
    }

    private void OnScrollbarScroll(object? sender, ScrollEventArgs e)
    {
        if (items is not null && e.ScrollEventType != ScrollEventType.EndScroll)
        {
            BeginUserScroll();
            CompleteUserScroll();
        }
    }

    private void OnScrollGesture(object? sender, ScrollGestureEventArgs e)
    {
        if (e.Delta.Y != 0)
        {
            BeginUserScroll();
            CompleteUserScroll();
        }
    }

    private void BeginUserScroll()
    {
        if (items is null)
        {
            return;
        }

        session++;
        isScrollUpdateRequired |= isScrollPending;
        isScrollPending = false;
        isUserScrollPending = true;
    }

    private void CompleteUserScroll()
    {
        if (items is not null && isUserScrollPending)
        {
            UpdateFollowingFromUserScroll();
            isUserScrollPending = false;

            if (isScrollUpdateRequired || isFollowingLatest)
            {
                isScrollUpdateRequired = false;
                QueueScrollUpdate();
            }
        }
    }

    private void UpdateFollowingFromUserScroll()
    {
        isFollowingLatest = scrollViewer is { } viewer && IsAtLatest(viewer);

        if (isFollowingLatest)
        {
            readingAnchor = null;
            readingItem = null;
        }
        else
        {
            CaptureReadingAnchor();
        }
    }

    private void CaptureReadingAnchor()
    {
        if (itemsControl is ListBox || scrollViewer is not { } viewer)
        {
            return;
        }

        readingOffset = viewer.Offset.Y;
        readingAnchor = null;
        readingItem = null;

        if (itemsControl.ItemsPanelRoot is not { } itemsPanel)
        {
            return;
        }

        var firstY = double.PositiveInfinity;

        foreach (var container in itemsControl.GetRealizedContainers())
        {
            if (container.TranslatePoint(default, itemsPanel) is not { } position || container.Bounds.Height <= 0)
            {
                continue;
            }

            var y = position.Y - readingOffset;
            if (y + container.Bounds.Height > 0 && y < viewer.Viewport.Height && y < firstY)
            {
                readingAnchor = container;
                readingItem = itemsControl.ItemFromContainer(container);
                readingAnchorY = y;
                firstY = y;
            }
        }
    }

    private void OnContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        scrollViewer?.RegisterAnchorCandidate(e.Container);
    }

    private void OnContainerClearing(object? sender, ContainerClearingEventArgs e)
    {
        scrollViewer?.UnregisterAnchorCandidate(e.Container);
    }

    private static bool IsAtLatest(ScrollViewer viewer)
    {
        return viewer.ScrollBarMaximum.Y - viewer.Offset.Y <= LATEST_OFFSET_THRESHOLD;
    }

    private void ResetToLatest()
    {
        session++;
        isScrollPending = false;
        isUserScrollPending = false;
        isScrollUpdateRequired = false;
        isFollowingLatest = true;
        readingAnchor = null;
        readingItem = null;
        QueueScrollUpdate();
    }

    private void QueueScrollUpdate()
    {
        if (isScrollPending || items is null || scrollViewer is not { } viewer ||
            !isFollowingLatest && itemsControl is ListBox)
        {
            return;
        }

        isScrollPending = true;
        var requestedSession = session;
        Dispatcher.UIThread.Post(() =>
        {
            if (requestedSession != session || isDisposed)
            {
                return;
            }

            isUpdatingScroll = true;

            try
            {
                viewer.UpdateLayout();

                if (requestedSession != session || isDisposed || items is null)
                {
                    return;
                }

                if (isFollowingLatest)
                {
                    ScrollToEnd(viewer);
                }
                else if (itemsControl is not ListBox)
                {
                    RestoreReadingAnchor(viewer);
                }

                viewer.UpdateLayout();
            }
            finally
            {
                isUpdatingScroll = false;

                if (requestedSession == session)
                {
                    isScrollPending = false;
                }
            }

            if (requestedSession == session && !isDisposed && items is not null && !isFollowingLatest)
            {
                CaptureReadingAnchor();
            }
        }, DispatcherPriority.Background);
    }

    private void ScrollToEnd(ScrollViewer viewer)
    {
        if (itemsControl is ListBox { ItemCount: > 0 } listBox &&
            listBox.ContainerFromIndex(listBox.ItemCount - 1) is null)
        {
            listBox.ScrollIntoView(listBox.ItemCount - 1);
            viewer.UpdateLayout();
        }

        viewer.Offset = new(viewer.Offset.X, viewer.ScrollBarMaximum.Y);
    }

    private void RestoreReadingAnchor(ScrollViewer viewer)
    {
        var offset = readingOffset;

        if (readingAnchor is not null && itemsControl.IndexFromContainer(readingAnchor) >= 0 &&
            ReferenceEquals(readingItem, itemsControl.ItemFromContainer(readingAnchor)) &&
            itemsControl.ItemsPanelRoot is { } itemsPanel &&
            readingAnchor.TranslatePoint(default, itemsPanel) is { } position)
        {
            offset = position.Y - readingAnchorY;
        }

        viewer.Offset = new(viewer.Offset.X, offset);
    }
}

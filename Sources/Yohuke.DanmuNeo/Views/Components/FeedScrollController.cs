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
    private const double READING_OFFSET_THRESHOLD = 0.1;
    private const int MAX_POSITION_CORRECTIONS = 8;
    private readonly ItemsControl itemsControl;
    private readonly List<(object Item, double Y)> readingAnchors = [];
    private ScrollViewer? scrollViewer;
    private INotifyCollectionChanged? items;
    private ScrollBar? verticalScrollBar;
    private long session;
    private double readingOffset;
    private int readingCorrections;
    private bool isScrollPending;
    private bool followWhenVisible;
    private bool isAnchorRealizationPending;
    private bool hasRequestedAnchorRealization;
    private bool isFollowingLatest = true;
    private bool isUserScrollPending;
    private bool isUpdatingScroll;
    private bool isDisposed;

    /// <summary>
    /// 连接使用外部滚动容器的消息列表。
    /// </summary>
    public FeedScrollController(ScrollViewer scrollViewer, ItemsControl itemsControl)
    {
        this.itemsControl = itemsControl;
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
        InvalidatePendingScroll();
        isUserScrollPending = false;
        readingAnchors.Clear();

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
            previous.AttachedToVisualTree -= OnScrollViewerAttached;
            previous.DetachedFromVisualTree -= OnScrollViewerDetached;
            previous.TemplateApplied -= OnScrollTemplateApplied;
            previous.RemoveHandler(InputElement.PointerWheelChangedEvent, OnPointerWheelChanged);
            previous.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
            previous.RemoveHandler(Gestures.ScrollGestureEvent, OnScrollGesture);
            AttachScrollBar(null);
        }

        InvalidatePendingScroll();
        isUserScrollPending = false;
        scrollViewer = viewer;

        if (viewer is null)
        {
            return;
        }

        viewer.PropertyChanged += OnScrollPropertyChanged;
        viewer.LayoutUpdated += OnLayoutUpdated;
        viewer.AttachedToVisualTree += OnScrollViewerAttached;
        viewer.DetachedFromVisualTree += OnScrollViewerDetached;
        viewer.TemplateApplied += OnScrollTemplateApplied;
        viewer.AddHandler(InputElement.PointerWheelChangedEvent, OnPointerWheelChanged,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        viewer.AddHandler(InputElement.KeyDownEvent, OnKeyDown,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        viewer.AddHandler(Gestures.ScrollGestureEvent, OnScrollGesture, RoutingStrategies.Bubble, true);
        AttachScrollBar(viewer.GetVisualDescendants().OfType<ScrollBar>()
            .FirstOrDefault(control => control.Orientation == Orientation.Vertical));
        QueueScrollToLatest();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            ResetToLatest();
        }
        else
        {
            QueueScrollToLatest();
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
            if (!isUserScrollPending && !isScrollPending && isFollowingLatest && !IsAtLatest(viewer))
            {
                isFollowingLatest = false;
                CaptureReadingAnchors(viewer);
            }
        }
        else if (isFollowingLatest &&
                 (e.Property == ScrollViewer.ExtentProperty || e.Property == ScrollViewer.ViewportProperty))
        {
            QueueScrollToLatest();
        }
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (items is null || isUpdatingScroll || isUserScrollPending ||
            scrollViewer is not { Viewport.Height: > 0 } viewer || !CanPosition(viewer) ||
            itemsControl.ItemsPanelRoot is null)
        {
            return;
        }

        if (isFollowingLatest)
        {
            if (followWhenVisible)
            {
                QueueScrollToLatest();
            }
        }
        else
        {
            RestoreReadingPosition(viewer);
        }
    }

    private void OnScrollViewerAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        QueueScrollToLatest();
    }

    private void OnScrollViewerDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        InvalidatePendingScroll();
        isUserScrollPending = false;
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
        var reachedLatest = sender is ScrollBar { Maximum: > 0 } scrollBar &&
                            scrollBar.Maximum - scrollBar.Value <= LATEST_OFFSET_THRESHOLD;
        if (items is not null && (e.ScrollEventType != ScrollEventType.EndScroll || reachedLatest))
        {
            BeginUserScroll();
            CompleteUserScroll(reachedLatest);
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

        InvalidatePendingScroll();
        isUserScrollPending = true;
    }

    private void CompleteUserScroll(bool reachedLatest = false)
    {
        if (items is null || !isUserScrollPending || scrollViewer is not { } viewer)
        {
            return;
        }

        isUserScrollPending = false;
        isFollowingLatest = reachedLatest || IsAtLatest(viewer);
        if (isFollowingLatest)
        {
            readingAnchors.Clear();
            QueueScrollToLatest(afterInput: true);
        }
        else
        {
            CaptureReadingAnchors(viewer);
        }
    }

    private void CaptureReadingAnchors(ScrollViewer viewer)
    {
        if (viewer.Viewport.Height <= 0 || itemsControl.ItemsPanelRoot is not { } panel)
        {
            return;
        }

        readingAnchors.Clear();
        readingCorrections = 0;
        hasRequestedAnchorRealization = false;
        readingOffset = viewer.Offset.Y;
        foreach (var container in itemsControl.GetRealizedContainers())
        {
            if (container is { IsEffectivelyVisible: true, Bounds.Height: > 0 } &&
                GetViewportY(container, panel, viewer) is { } y &&
                y + container.Bounds.Height > 0 && y < viewer.Viewport.Height &&
                itemsControl.ItemFromContainer(container) is { } item)
            {
                readingAnchors.Add((item, y));
            }
        }

        readingAnchors.Sort(static (left, right) => left.Y.CompareTo(right.Y));
    }

    private void RestoreReadingPosition(ScrollViewer viewer)
    {
        if (readingAnchors.Count == 0)
        {
            CaptureReadingAnchors(viewer);
            return;
        }

        var (index, y) = FindReadingAnchor();
        var offset = 0.0;
        if (index >= 0)
        {
            if (itemsControl.ItemsPanelRoot is not { } panel ||
                itemsControl.ContainerFromIndex(index) is not { } container ||
                GetViewportY(container, panel, viewer) is not { } currentY)
            {
                if (isAnchorRealizationPending)
                {
                    return;
                }

                if (hasRequestedAnchorRealization)
                {
                    CaptureReadingAnchors(viewer);
                }
                else
                {
                    QueueAnchorRealization(viewer);
                }

                return;
            }

            offset = viewer.Offset.Y + currentY - y;
        }

        offset = Math.Clamp(offset, 0, viewer.ScrollBarMaximum.Y);
        if (Math.Abs(offset - viewer.Offset.Y) <= READING_OFFSET_THRESHOLD ||
            readingCorrections >= MAX_POSITION_CORRECTIONS)
        {
            CaptureReadingAnchors(viewer);
            return;
        }

        readingCorrections++;
        SetVerticalOffset(viewer, offset);
    }

    private (int Index, double Y) FindReadingAnchor()
    {
        foreach (var (item, y) in readingAnchors)
        {
            var index = itemsControl.Items.IndexOf(item);
            if (index >= 0)
            {
                return (index, y);
            }
        }

        return (-1, 0);
    }

    private void QueueAnchorRealization(ScrollViewer viewer)
    {
        isAnchorRealizationPending = true;
        var requestedSession = session;
        Dispatcher.UIThread.Post(() =>
        {
            if (requestedSession != session || isDisposed)
            {
                return;
            }

            isAnchorRealizationPending = false;
            if (isFollowingLatest || !CanPosition(viewer))
            {
                return;
            }

            var (index, _) = FindReadingAnchor();
            if (index >= 0 && itemsControl.ContainerFromIndex(index) is null)
            {
                hasRequestedAnchorRealization = true;
                isUpdatingScroll = true;
                try
                {
                    SetVerticalOffset(viewer, readingOffset);
                    viewer.UpdateLayout();
                    (index, _) = FindReadingAnchor();
                    // ScrollIntoView 可以同步触发布局，需在布局事件之外调用。
                    if (requestedSession == session && index >= 0 && itemsControl.ContainerFromIndex(index) is null)
                    {
                        itemsControl.ScrollIntoView(index);
                        viewer.UpdateLayout();
                    }
                }
                finally
                {
                    isUpdatingScroll = false;
                }
            }

            if (requestedSession == session)
            {
                RestoreReadingPosition(viewer);
            }
        }, DispatcherPriority.Render);
    }

    private static double? GetViewportY(Control container, Panel panel, ScrollViewer viewer)
    {
        return container.TranslatePoint(default, panel)?.Y - viewer.Offset.Y;
    }

    private bool IsLastItemInView(ScrollViewer viewer)
    {
        return itemsControl.ItemCount == 0 ||
               itemsControl.ItemsPanelRoot is { } panel &&
               itemsControl.ContainerFromIndex(itemsControl.ItemCount - 1) is { Bounds.Height: > 0 } last &&
               GetViewportY(last, panel, viewer) is { } y &&
               y + last.Bounds.Height > 0 && y + last.Bounds.Height <= viewer.Viewport.Height + LATEST_OFFSET_THRESHOLD;
    }

    private static bool CanPosition(ScrollViewer viewer)
    {
        return viewer.IsEffectivelyVisible && viewer.GetVisualRoot() is not null;
    }

    private static void SetVerticalOffset(ScrollViewer viewer, double offset)
    {
        viewer.SetCurrentValue(ScrollViewer.OffsetProperty, viewer.Offset.WithY(offset));
    }

    private static bool IsAtLatest(ScrollViewer viewer)
    {
        return viewer.ScrollBarMaximum.Y - viewer.Offset.Y <= LATEST_OFFSET_THRESHOLD;
    }

    private void ResetToLatest()
    {
        InvalidatePendingScroll();
        isUserScrollPending = false;
        isFollowingLatest = true;
        readingAnchors.Clear();
        QueueScrollToLatest();
    }

    private void InvalidatePendingScroll()
    {
        session++;
        isScrollPending = false;
        followWhenVisible = false;
        isAnchorRealizationPending = false;
        hasRequestedAnchorRealization = false;
        readingCorrections = 0;
    }

    private void QueueScrollToLatest(bool afterInput = false)
    {
        if (isScrollPending || !isFollowingLatest || items is null || scrollViewer is not { } viewer)
        {
            return;
        }

        followWhenVisible = !CanPosition(viewer);
        if (followWhenVisible)
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
                if (!CanPosition(viewer))
                {
                    followWhenVisible = true;
                    return;
                }

                viewer.UpdateLayout();
                var attempts = itemsControl is ListBox ? 1 : MAX_POSITION_CORRECTIONS;
                for (var attempt = 0; attempt < attempts && requestedSession == session && !isDisposed && items is not null;
                     attempt++)
                {
                    if (itemsControl.ItemCount > 0 &&
                        itemsControl.ContainerFromIndex(itemsControl.ItemCount - 1) is null)
                    {
                        itemsControl.ScrollIntoView(itemsControl.ItemCount - 1);
                        viewer.UpdateLayout();
                        if (requestedSession != session)
                        {
                            return;
                        }
                    }

                    SetVerticalOffset(viewer, viewer.ScrollBarMaximum.Y);
                    viewer.UpdateLayout();
                    if (itemsControl is ListBox || IsAtLatest(viewer) && IsLastItemInView(viewer))
                    {
                        break;
                    }
                }
            }
            finally
            {
                isUpdatingScroll = false;

                if (requestedSession == session)
                {
                    isScrollPending = false;
                }
            }
        }, itemsControl is ListBox || afterInput ? DispatcherPriority.Background : DispatcherPriority.Render);
    }
}

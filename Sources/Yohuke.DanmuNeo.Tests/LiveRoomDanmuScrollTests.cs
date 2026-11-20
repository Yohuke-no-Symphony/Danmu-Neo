using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Views.Components;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 弹幕翻阅、阅读锚点和排队自动滚动的真实界面回归测试。
/// </summary>
public class LiveRoomDanmuScrollTests
{
    /// <summary>
    /// 键盘翻阅发生在排队跟随之前时，保持用户选择的位置。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void KeyboardScrollCancelsQueuedFollowing(bool compact)
    {
        using var context = new LiveRoomFeedViewTestContext();
        var view = CreateView(context, compact);
        var window = CreateWindow(context, view);

        try
        {
            ShowWindow(window);
            var scroll = view.FindControl<ScrollViewer>("DanmuScrollViewer")!;
            var latest = view.GetVisualDescendants().OfType<Button>()
                .Last(control => control.Classes.Contains("danmuFeedButton"));
            latest.Focus();
            context.Room.AddDanmu(new() { RoomId = "100", Uid = 30, Content = "排队中的最新弹幕" });
            window.KeyPressQwerty(PhysicalKey.PageUp, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.PageUp, RawInputModifiers.None);
            PumpLayout(window);

            Assert.InRange(scroll.Offset.Y, 0, scroll.ScrollBarMaximum.Y - 1);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 翻阅时插入不同高度的新弹幕并裁剪旧记录，仍保留正在阅读的条目位置。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncomingDanmuPreservesReadingAnchor(bool compact)
    {
        using var context = new LiveRoomFeedViewTestContext();
        var view = CreateView(context, compact);
        var window = CreateWindow(context, view);

        try
        {
            ShowWindow(window);
            var scroll = view.FindControl<ScrollViewer>("DanmuScrollViewer")!;
            var oldest = context.Room.DanmuItems[0];
            WheelUp(window, scroll);
            PumpLayout(window);
            var anchor = view.GetVisualDescendants().OfType<Button>()
                .Where(control => control.Classes.Contains("danmuFeedButton"))
                .First(control => control.TranslatePoint(default, scroll)!.Value.Y >= 0);
            var anchorItem = anchor.DataContext;
            var before = anchor.TranslatePoint(default, scroll)!.Value.Y;
            context.Room.AddDanmu(new()
            {
                RoomId = "100", Uid = 30, UserName = "新用户", MedalName = "长牌子", MedalLevel = 30,
                Content = new string('长', 180)
            });
            PumpLayout(window);

            Assert.Same(anchorItem, anchor.DataContext);
            Assert.InRange(anchor.TranslatePoint(default, scroll)!.Value.Y, before - 0.5, before + 0.5);
            Assert.Equal(300, context.Room.DanmuItems.Count);
            Assert.DoesNotContain(oldest, context.Room.DanmuItems);
            Assert.Equal(new string('长', 180), context.Room.DanmuItems[^1].Content);
            Assert.InRange(scroll.Offset.Y, 0, scroll.ScrollBarMaximum.Y - 1);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 新消息布局期间的临时偏移归零不能覆盖用户的历史阅读位置。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlatformOffsetResetDuringIncomingDanmuPreservesReadingAnchor(bool compact)
    {
        using var context = new LiveRoomFeedViewTestContext();
        var view = CreateView(context, compact);
        var window = CreateWindow(context, view);

        try
        {
            ShowWindow(window);
            var scroll = view.FindControl<ScrollViewer>("DanmuScrollViewer")!;
            WheelUp(window, scroll);
            PumpLayout(window);
            var anchor = GetReadingAnchor(view, scroll);
            var item = anchor.DataContext;
            var before = anchor.TranslatePoint(default, scroll)!.Value.Y;

            context.Room.AddDanmu(new() { RoomId = "100", Content = new string('长', 180) });
            scroll.Offset = default;
            context.Room.AddDanmu(new() { RoomId = "100", Content = "同一次布局中的第二条弹幕" });
            PumpLayout(window);

            Assert.Same(item, anchor.DataContext);
            Assert.InRange(anchor.TranslatePoint(default, scroll)!.Value.Y, before - 0.5, before + 0.5);
            Assert.InRange(scroll.Offset.Y, 0, scroll.ScrollBarMaximum.Y - 1);
            Assert.Equal(300, context.Room.DanmuItems.Count);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 无用户滚动输入的焦点回迁不能恢复自动跟随或拉回列表底部。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    public void FocusChangeWhileReadingDoesNotResumeFollowing(bool compact, int focusIndex)
    {
        using var context = new LiveRoomFeedViewTestContext();
        var view = CreateView(context, compact);
        var window = CreateWindow(context, view);

        try
        {
            ShowWindow(window);
            var scroll = view.FindControl<ScrollViewer>("DanmuScrollViewer")!;
            var latest = view.GetVisualDescendants().OfType<Button>()
                .Where(control => control.Classes.Contains("danmuFeedButton"))
                .Reverse().ElementAt(focusIndex);
            WheelUp(window, scroll);
            PumpLayout(window);
            var anchor = GetReadingAnchor(view, scroll);
            var before = anchor.TranslatePoint(default, scroll)!.Value.Y;

            Assert.False(latest.IsFocused);
            Assert.True(latest.Focus());
            context.Room.AddDanmu(new() { RoomId = "100", Content = "焦点变化后到达的新弹幕" });
            PumpLayout(window);

            Assert.InRange(anchor.TranslatePoint(default, scroll)!.Value.Y, before - 0.5, before + 0.5);
            Assert.InRange(scroll.Offset.Y, 0, scroll.ScrollBarMaximum.Y - 1);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 新消息之间再次滚动时，后续布局以用户新选择的位置为准。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void WheelBetweenIncomingMessagesPreservesNewReadingPosition(bool compact)
    {
        using var context = new LiveRoomFeedViewTestContext();
        var view = CreateView(context, compact);
        var window = CreateWindow(context, view);

        try
        {
            ShowWindow(window);
            var scroll = view.FindControl<ScrollViewer>("DanmuScrollViewer")!;
            WheelUp(window, scroll);
            PumpLayout(window);
            context.Room.AddDanmu(new() { RoomId = "100", Content = "再次滚动前的新弹幕" });
            WheelUp(window, scroll);
            var anchor = GetReadingAnchor(view, scroll);
            var item = anchor.DataContext;
            var before = anchor.TranslatePoint(default, scroll)!.Value.Y;
            context.Room.AddDanmu(new() { RoomId = "100", Content = new string('长', 180) });
            PumpLayout(window);

            Assert.Same(item, anchor.DataContext);
            Assert.InRange(anchor.TranslatePoint(default, scroll)!.Value.Y, before - 0.5, before + 0.5);
            Assert.InRange(scroll.Offset.Y, 0, scroll.ScrollBarMaximum.Y - 1);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 真实滚动条拖动期间的新消息与临时归零保留锚点，拖回底部后恢复跟随。
    /// </summary>
    [AvaloniaFact]
    public void ScrollbarDragPreservesReadingAcrossIncomingMessagesAndRestoresFollowing()
    {
        using var context = new LiveRoomFeedViewTestContext();
        var view = CreateView(context, false);
        var window = CreateWindow(context, view);

        try
        {
            ShowWindow(window);
            var scroll = view.FindControl<ScrollViewer>("DanmuScrollViewer")!;
            var scrollBar = scroll.GetVisualDescendants().OfType<ScrollBar>()
                .Single(control => control.Orientation == Avalonia.Layout.Orientation.Vertical);
            scrollBar.AllowAutoHide = false;
            PumpLayout(window);
            var thumb = scrollBar.GetVisualDescendants().OfType<Thumb>().Single();
            var start = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), window)!.Value;
            var target = start - new Vector(0, 60);
            window.MouseMove(start);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(target, RawInputModifiers.LeftMouseButton);
            PumpLayout(window);
            Assert.InRange(scroll.Offset.Y, 0, scroll.ScrollBarMaximum.Y - 1);
            var anchor = GetReadingAnchor(view, scroll);
            var before = anchor.TranslatePoint(default, scroll)!.Value.Y;
            context.Room.AddDanmu(new() { RoomId = "100", Content = "拖动滚动条期间的新消息" });
            scroll.Offset = default;
            context.Room.AddDanmu(new() { RoomId = "100", Content = new string('长', 180) });
            PumpLayout(window);
            window.MouseUp(target, MouseButton.Left);
            PumpLayout(window);

            Assert.InRange(anchor.TranslatePoint(default, scroll)!.Value.Y, before - 0.5, before + 0.5);
            Assert.InRange(scroll.Offset.Y, 0, scroll.ScrollBarMaximum.Y - 1);

            start = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), window)!.Value;
            target = start + new Vector(0, scroll.Viewport.Height);
            window.MouseMove(start);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(target, RawInputModifiers.LeftMouseButton);
            window.MouseUp(target, MouseButton.Left);
            PumpLayout(window);
            Assert.Equal(scroll.ScrollBarMaximum.Y, scroll.Offset.Y, 0.5);
            context.Room.AddDanmu(new() { RoomId = "100", Content = "拖回底部后跟随新弹幕" });
            PumpLayout(window);
            Assert.Equal(scroll.ScrollBarMaximum.Y, scroll.Offset.Y, 0.5);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 真实滚轮撤销旧回到最新的请求，后续批量插入继续保留阅读锚点。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void WheelCancelsQueuedFollowingAndPreservesAnchorAcrossBurst(bool compact)
    {
        using var context = new LiveRoomFeedViewTestContext();
        var view = CreateView(context, compact);
        var window = CreateWindow(context, view);

        try
        {
            ShowWindow(window);
            var scroll = view.FindControl<ScrollViewer>("DanmuScrollViewer")!;
            context.Room.AddDanmu(new() { RoomId = "100", Content = "滚轮之前排队的新消息" });
            WheelUp(window, scroll);
            PumpLayout(window);
            Assert.InRange(scroll.Offset.Y, 0, scroll.ScrollBarMaximum.Y - 1);
            var anchor = GetReadingAnchor(view, scroll);
            var before = anchor.TranslatePoint(default, scroll)!.Value.Y;

            for (var index = 0; index < 12; index++)
            {
                context.Room.AddDanmu(new() { RoomId = "100", Content = new string('长', index * 9 + 1) });
            }

            PumpLayout(window);

            Assert.InRange(anchor.TranslatePoint(default, scroll)!.Value.Y, before - 0.5, before + 0.5);
            Assert.InRange(scroll.Offset.Y, 0, scroll.ScrollBarMaximum.Y - 1);
            Assert.Equal(300, context.Room.DanmuItems.Count);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 键盘翻阅与滚轮回到底部后均使用实际用户输入更新跟随状态。
    /// </summary>
    [AvaloniaFact]
    public void KeyboardReadingAndWheelReturnToLatestUpdateFollowing()
    {
        using var context = new LiveRoomFeedViewTestContext();
        var view = CreateView(context, false);
        var window = CreateWindow(context, view);

        try
        {
            ShowWindow(window);
            var scroll = view.FindControl<ScrollViewer>("DanmuScrollViewer")!;
            var latest = view.GetVisualDescendants().OfType<Button>()
                .Last(control => control.Classes.Contains("danmuFeedButton"));
            latest.Focus();
            window.KeyPressQwerty(PhysicalKey.PageUp, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.PageUp, RawInputModifiers.None);
            PumpLayout(window);
            Assert.InRange(scroll.Offset.Y, 0, scroll.ScrollBarMaximum.Y - 1);
            context.Room.AddDanmu(new() { RoomId = "100", Content = "键盘翻阅中的新弹幕" });
            PumpLayout(window);
            Assert.InRange(scroll.Offset.Y, 0, scroll.ScrollBarMaximum.Y - 1);

            WheelToLatest(window, scroll);
            PumpLayout(window);
            context.Room.AddDanmu(new() { RoomId = "100", Content = "用户回到底部后的新弹幕" });
            PumpLayout(window);

            Assert.Equal(scroll.ScrollBarMaximum.Y, scroll.Offset.Y, 0.5);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 回到底部后恢复跟随，清空列表也复位到最新位置。
    /// </summary>
    [AvaloniaFact]
    public void ReturningToLatestAndResetResumeFollowing()
    {
        using var context = new LiveRoomFeedViewTestContext();
        var view = CreateView(context, false);
        var window = CreateWindow(context, view);

        try
        {
            ShowWindow(window);
            var scroll = view.FindControl<ScrollViewer>("DanmuScrollViewer")!;
            Assert.Equal(scroll.ScrollBarMaximum.Y, scroll.Offset.Y, 0.5);
            var previousBottom = scroll.ScrollBarMaximum.Y;
            window.Height -= 120;
            PumpLayout(window);
            Assert.True(scroll.ScrollBarMaximum.Y > previousBottom);
            Assert.Equal(scroll.ScrollBarMaximum.Y, scroll.Offset.Y, 0.5);
            WheelUp(window, scroll);
            PumpLayout(window);
            WheelToLatest(window, scroll);
            PumpLayout(window);
            context.Room.AddDanmu(new() { RoomId = "100", Uid = 30, Content = "恢复跟随" });
            PumpLayout(window);
            Assert.Equal(scroll.ScrollBarMaximum.Y, scroll.Offset.Y, 0.5);
            WheelUp(window, scroll);
            PumpLayout(window);
            context.Room.DanmuItems.Clear();
            context.Room.AddDanmu(new() { RoomId = "100", Uid = 30, Content = "重置后新消息" });
            PumpLayout(window);

            Assert.Equal(0, scroll.Offset.Y);
            var first = GetReadingAnchor(view, scroll);
            Assert.Same(Assert.Single(context.Room.DanmuItems), first.DataContext);
            Assert.InRange(first.TranslatePoint(default, scroll)!.Value.Y, 0, 0.5);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 切换房间废弃旧回调与集合监听，Dock 重挂保留新房间的阅读锚点。
    /// </summary>
    [AvaloniaFact]
    public void SwitchingRoomAndReattachingIgnoresPreviousSession()
    {
        using var context = new LiveRoomFeedViewTestContext();
        var view = CreateView(context, false);
        var window = CreateWindow(context, view);
        var nextRoom = new LiveRoomTabViewModel(new() { RoomId = "200" });

        for (var index = 0; index < 300; index++)
        {
            nextRoom.DanmuItems.Add(new() { RoomId = "200", UserName = "新房间用户", Content = $"新房间历史 {index}" });
        }

        try
        {
            ShowWindow(window);
            var scroll = view.FindControl<ScrollViewer>("DanmuScrollViewer")!;
            context.Room.AddDanmu(new() { RoomId = "100", Content = "旧房间排队回调" });
            view.DataContext = nextRoom;
            WheelUp(window, scroll);
            context.Room.DanmuItems.Clear();
            context.Room.AddDanmu(new() { RoomId = "100", Content = "旧房间重置事件" });
            PumpLayout(window);
            Assert.InRange(scroll.Offset.Y, 0, scroll.ScrollBarMaximum.Y - 1);
            nextRoom.AddDanmu(new() { RoomId = "200", Content = "新房间阅读中新消息" });
            PumpLayout(window);
            Assert.InRange(scroll.Offset.Y, 0, scroll.ScrollBarMaximum.Y - 1);
            var anchor = GetReadingAnchor(view, scroll);
            var anchorItem = anchor.DataContext;
            var anchorY = anchor.TranslatePoint(default, scroll)!.Value.Y;
            window.Content = null;
            PumpLayout(window);
            window.Content = view;
            PumpLayout(window);
            anchor = GetReadingAnchor(view, scroll);
            Assert.Same(anchorItem, anchor.DataContext);
            Assert.InRange(anchor.TranslatePoint(default, scroll)!.Value.Y, anchorY - 0.5, anchorY + 0.5);
            nextRoom.AddDanmu(new() { RoomId = "200", Content = "重新挂载后的新消息" });
            PumpLayout(window);
            Assert.Same(anchorItem, anchor.DataContext);
            Assert.InRange(anchor.TranslatePoint(default, scroll)!.Value.Y, anchorY - 0.5, anchorY + 0.5);
        }
        finally
        {
            window.Close();
        }
    }

    private static LiveRoomWorkspaceView CreateView(LiveRoomFeedViewTestContext context, bool compact)
    {
        context.ViewModel.IsCompactDanmuDisplay = compact;
        context.Room.DanmuItems.Clear();

        for (var index = 0; index < 300; index++)
        {
            context.Room.DanmuItems.Add(new DanmuFeedItem
            {
                RoomId = "100", UserUid = "30", UserName = $"用户 {index}", Content = $"历史弹幕 {index}"
            });
        }

        return new(new(context.ViewModel, context.Room)) { DataContext = context.Room };
    }

    private static Window CreateWindow(LiveRoomFeedViewTestContext context, LiveRoomWorkspaceView view)
    {
        return new() { Width = 1100, Height = 800, DataContext = context.ViewModel, Content = view };
    }

    private static void ShowWindow(Window window)
    {
        window.Show();
        PumpLayout(window);
    }

    private static void PumpLayout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void WheelUp(Window window, ScrollViewer scroll)
    {
        var point = scroll.TranslatePoint(new Point(scroll.Bounds.Width / 2, scroll.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        window.MouseWheel(point, new Vector(0, 6));
    }

    private static void WheelToLatest(Window window, ScrollViewer scroll)
    {
        var point = scroll.TranslatePoint(new Point(scroll.Bounds.Width / 2, scroll.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        window.MouseWheel(point, new Vector(0, -1000));
    }

    private static Button GetReadingAnchor(LiveRoomWorkspaceView view, ScrollViewer scroll)
    {
        return view.GetVisualDescendants().OfType<Button>()
            .Where(control => control.Classes.Contains("danmuFeedButton"))
            .First(control => control.TranslatePoint(default, scroll)!.Value.Y >= 0);
    }
}

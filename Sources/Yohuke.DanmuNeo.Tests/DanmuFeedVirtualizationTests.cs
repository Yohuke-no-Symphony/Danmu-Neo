using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Views.Components;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证正式弹幕视图按视口复用行，以及回收期间的输入与布局。</summary>
public sealed class DanmuFeedVirtualizationTests
{
    /// <summary>滚过多个互不重叠的区域时，仅创建附近的行，并把正文控件复用到其他消息。</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void BoundedViewportReusesRowsWithCurrentContentAndStripe(bool compact)
    {
        using var fixture = new LiveRoomFeedViewTestContext();
        fixture.ViewModel.IsCompactDanmuDisplay = compact;
        Populate(fixture);
        using var context = new WorkbenchContext(fixture.ViewModel);
        using var panels = new WorkbenchPanelSet(context);
        var window = CreateWindow(panels.Danmu);

        try
        {
            window.Show();
            PumpLayout(window);
            var list = GetList(panels.Danmu);
            var scroll = GetScroll(panels.Danmu);
            var seen = new Dictionary<Button, (DanmuFeedItem Item, EmoticonTextBlock Text)>();
            var previousItems = new HashSet<DanmuFeedItem>();
            var reused = false;

            foreach (var delta in new[] { 0, 1000, -16, -20, 12, -1000 })
            {
                if (delta != 0)
                {
                    ScrollByWheel(window, scroll, delta);
                }

                var rows = list.GetRealizedContainers().OfType<Button>().ToArray();
                Assert.InRange(rows.Length, 1, 64);
                Assert.Null(list.ContainerFromIndex(list.IndexFromContainer(rows[0]) < 150 ? 299 : 0));
                var currentItems = new HashSet<DanmuFeedItem>();

                foreach (var row in rows)
                {
                    var item = Assert.IsType<DanmuFeedItem>(row.DataContext);
                    Assert.DoesNotContain(item, previousItems);
                    Assert.True(currentItems.Add(item));
                    var itemIndex = list.IndexFromContainer(row);
                    Assert.Same(fixture.Room.DanmuItems[itemIndex], item);
                    Assert.Equal(itemIndex % 2 == 1, row.Classes.Contains("alternate"));
                    var text = Assert.Single(row.GetVisualDescendants().OfType<EmoticonTextBlock>());
                    Assert.Equal(item.Content, text.ContentText);
                    var avatar = Assert.Single(row.GetVisualDescendants().OfType<UserAvatarView>());
                    Assert.Equal(item.AvatarUid, avatar.UserUid);

                    if (seen.TryGetValue(row, out var previous) && !ReferenceEquals(item, previous.Item))
                    {
                        Assert.Same(previous.Text, text);
                        reused = true;
                    }

                    seen[row] = (item, text);
                }

                previousItems = currentItems;
            }

            Assert.True(reused, "滚动过程中应将同一正文控件重新绑定到另一条弹幕");
            Assert.InRange(seen.Count, 1, 64);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>旧行按下后回收给同一房间的新消息时，过期的释放和点击均不会写入草稿。</summary>
    [AvaloniaFact]
    public void PressedRowCannotInsertAnotherMessageAfterRecycling()
    {
        using var fixture = new LiveRoomFeedViewTestContext();
        Populate(fixture);
        fixture.Room.InputDraft = "保留草稿";
        using var context = new WorkbenchContext(fixture.ViewModel);
        using var panels = new WorkbenchPanelSet(context);
        var window = CreateWindow(panels.Danmu);

        try
        {
            window.Show();
            PumpLayout(window);
            var list = GetList(panels.Danmu);
            var row = GetInteriorRow(panels.Danmu);
            var point = Center(GetBody(row), window);
            var index = list.IndexFromContainer(row);
            var replacement = new DanmuFeedItem { RoomId = "100", UserUid = "42", UserName = "替换项", Content = "替换弹幕" };
            window.MouseMove(point);
            window.MouseDown(point, MouseButton.Left);
            Assert.True(row.IsPressed);
            fixture.Room.DanmuItems[index] = replacement;
            PumpLayout(window);

            Assert.Same(row, list.ContainerFromItem(replacement));
            Assert.Same(replacement, row.DataContext);
            row.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.MouseUp(point, MouseButton.Left);
            PumpLayout(window);

            Assert.Equal("保留草稿", fixture.Room.InputDraft);
            var currentPoint = Center(GetBody(row), window);
            window.MouseMove(currentPoint);
            window.MouseDown(currentPoint, MouseButton.Left);
            window.MouseUp(currentPoint, MouseButton.Left);
            PumpLayout(window);
            Assert.Contains(replacement.Content, fixture.Room.InputDraft);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>回收已打开右键菜单的行时关闭菜单，并取消仍在进行的权限查询。</summary>
    [AvaloniaFact]
    public void RecyclingMenuOwnerCancelsItsPermissionRequest()
    {
        using var fixture = new LiveRoomFeedViewTestContext();
        Populate(fixture);
        var permission = new TaskCompletionSource<RoomModerationPermission>();
        fixture.ModerationService.RespondAsync = _ => permission.Task;
        using var context = new WorkbenchContext(fixture.ViewModel);
        using var panels = new WorkbenchPanelSet(context);
        var window = CreateWindow(panels.Danmu);

        try
        {
            window.Show();
            PumpLayout(window);
            var list = GetList(panels.Danmu);
            var row = GetInteriorRow(panels.Danmu);
            var index = list.IndexFromContainer(row);
            var replacement = new DanmuFeedItem { RoomId = "100", UserUid = "42", UserName = "替换项", Content = "替换弹幕" };
            var point = Center(GetBody(row), window);
            window.MouseMove(point);
            window.MouseDown(point, MouseButton.Right);
            window.MouseUp(point, MouseButton.Right);
            PumpLayout(window);
            var token = Assert.Single(fixture.ModerationService.PermissionTokens);
            Assert.False(token.IsCancellationRequested);

            fixture.Room.DanmuItems[index] = replacement;
            PumpLayout(window);

            Assert.Same(row, list.ContainerFromItem(replacement));
            Assert.True(token.IsCancellationRequested);
            Assert.Empty(fixture.ModerationService.MuteRequests);
        }
        finally
        {
            permission.TrySetCanceled();
            window.Close();
        }
    }

    /// <summary>列宽拖动应用到当前行和回收后进入视口的行，两种显示模式各自记忆比例。</summary>
    [AvaloniaFact]
    public async Task DraggedColumnRatioAppliesAfterScrollingAndSwitchingDensity()
    {
        using var fixture = new LiveRoomFeedViewTestContext();
        fixture.ViewModel.IsCompactDanmuDisplay = false;
        fixture.ViewModel.Settings.DanmuColumns.CompactIdentityRatio = 0.3;
        Populate(fixture);
        using var context = new WorkbenchContext(fixture.ViewModel);
        using var panels = new WorkbenchPanelSet(context);
        var window = CreateWindow(panels.Danmu);

        try
        {
            window.Show();
            PumpLayout(window);
            var list = GetList(panels.Danmu);
            var row = GetInteriorRow(panels.Danmu);
            var divider = Assert.Single(row.GetVisualDescendants().OfType<Thumb>(),
                thumb => thumb.Classes.Contains("danmuColumnDivider"));
            var start = Center(divider, window);
            var end = start + new Vector(55, 0);
            var regularRatio = fixture.ViewModel.DanmuIdentityColumnRatio;
            window.MouseMove(start);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            PumpLayout(window);
            Assert.Equal(regularRatio, fixture.ViewModel.DanmuIdentityColumnRatio);
            Assert.True(list.IdentityColumnWidth.Value > regularRatio);
            window.MouseUp(end, MouseButton.Left);
            PumpLayout(window);
            var draggedRatio = fixture.ViewModel.DanmuIdentityColumnRatio;
            Assert.True(draggedRatio > regularRatio);
            Assert.Equal(0.3, fixture.ViewModel.Settings.DanmuColumns.CompactIdentityRatio);

            Assert.Null(list.ContainerFromIndex(0));
            ScrollByWheel(window, GetScroll(panels.Danmu), 1000);
            AssertColumnRatio(Assert.IsAssignableFrom<Button>(list.ContainerFromIndex(0)), draggedRatio);
            fixture.ViewModel.IsCompactDanmuDisplay = true;
            PumpLayout(window);
            AssertColumnRatio(GetInteriorRow(panels.Danmu), 0.3);
            fixture.ViewModel.IsCompactDanmuDisplay = false;
            PumpLayout(window);
            AssertColumnRatio(GetInteriorRow(panels.Danmu), draggedRatio);
            await fixture.ViewModel.SaveAsync();
        }
        finally
        {
            window.Close();
        }
    }

    private static void Populate(LiveRoomFeedViewTestContext fixture)
    {
        fixture.Room.DanmuItems.Clear();

        for (var index = 0; index < 300; index++)
        {
            fixture.Room.DanmuItems.Add(new()
            {
                RoomId = "100", UserUid = (42 + index).ToString(), UserName = $"用户 {index}", Content = $"弹幕 {index}"
            });
        }
    }

    private static Window CreateWindow(Control content)
    {
        return new() { Width = 640, Height = 240, Content = content };
    }

    private static DanmuFeedList GetList(DanmuPanelView panel)
    {
        return panel.FindControl<DanmuFeedList>("DanmuItemsControl")!;
    }

    private static ScrollViewer GetScroll(DanmuPanelView panel)
    {
        return panel.FindControl<ScrollViewer>("DanmuScrollViewer")!;
    }

    private static Button GetInteriorRow(DanmuPanelView panel)
    {
        var scroll = GetScroll(panel);
        return GetList(panel).GetRealizedContainers().OfType<Button>()
            .Where(row => row.IsEffectivelyVisible && row.Bounds.Height > 0)
            .Select(row => (Row: row, Position: row.TranslatePoint(default, scroll)))
            .Where(candidate => candidate.Position is { } point &&
                                point.Y >= 0 && point.Y + candidate.Row.Bounds.Height <= scroll.Viewport.Height)
            .OrderBy(candidate => candidate.Position!.Value.Y)
            .ElementAt(2).Row;
    }

    private static EmoticonTextBlock GetBody(Button row)
    {
        return Assert.Single(row.GetVisualDescendants().OfType<EmoticonTextBlock>());
    }

    private static Point Center(Control control, Window window)
    {
        return control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
    }

    private static void ScrollByWheel(Window window, ScrollViewer scroll, double delta)
    {
        var point = Center(scroll, window);
        window.MouseMove(point);
        window.MouseWheel(point, new Vector(0, delta));
        PumpLayout(window);
    }

    private static void AssertColumnRatio(Button row, double expected)
    {
        var grid = Assert.Single(row.GetVisualDescendants().OfType<Grid>(),
            control => control.Classes.Contains("danmuFeedLayout"));
        Assert.Equal(new GridLength(expected, GridUnitType.Star), grid.ColumnDefinitions[0].Width);
        Assert.Equal(new GridLength(1 - expected, GridUnitType.Star), grid.ColumnDefinitions[2].Width);
    }

    private static void PumpLayout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}

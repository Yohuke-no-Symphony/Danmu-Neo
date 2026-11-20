using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lucide.Avalonia;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Layouts;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Views;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证 SC 标题栏回到最新、原生列表选择和独立快捷键。
/// </summary>
[Collection(AvaloniaDispatcherTestCollection.NAME)]
public class SuperChatScrollToLatestTests
{
    /// <summary>
    /// 普通和紧凑显示下，停靠与浮动标题栏均能回到最新，并保留列表选择和空列表行为。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task HeaderButtonReturnsNativeListToLatest(bool floating, bool compact)
    {
        using var fixture = new LiveRoomFeedViewTestContext();
        fixture.ViewModel.IsCompactDanmuDisplay = compact;
        PopulateFeed(fixture.Room);
        var main = new MainWindow { DataContext = fixture.ViewModel, Width = 1100, Height = 800 };

        try
        {
            main.Show();
            await main.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            workbench.Layouts.ShowPanel(WorkbenchPanelIds.SUPER_CHAT);
            Window host = main;

            if (floating)
            {
                workbench.Layouts.FloatPanel(WorkbenchPanelIds.SUPER_CHAT);
                host = Assert.Single(workbench.Layouts.FloatingWindows);
            }

            PumpLayout(main, host);
            var list = workbench.Panels.SuperChat.GetVisualDescendants().OfType<ListBox>().Single();
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
            var button = GetHeaderButton(host);
            var chrome = button.FindAncestorOfType<WorkbenchToolChromeControl>();
            Assert.NotNull(chrome);
            Assert.Same(workbench.Layouts.PanelAdapters[WorkbenchPanelIds.SUPER_CHAT].Owner, chrome.DataContext);
            Assert.True(button.IsEffectivelyEnabled);
            Assert.NotNull(button.Command);
            Assert.Equal(22, button.Bounds.Width);
            Assert.Equal(22, button.Bounds.Height);
            var icon = Assert.IsType<LucideIcon>(button.Content);
            Assert.Equal(LucideIconKind.ArrowDownToLine, icon.Kind);
            Assert.InRange(icon.Bounds.Width, 16, button.Bounds.Width);
            Assert.InRange(icon.Bounds.Height, 16, button.Bounds.Height);
            Assert.Equal(scroll.ScrollBarMaximum.Y, scroll.Offset.Y, 0.5);
            list.SelectedIndex = 40;
            var selected = Assert.IsType<SuperChatItem>(list.SelectedItem);
            list.ScrollIntoView(40);
            PumpLayout(host);
            Assert.InRange(scroll.Offset.Y, 0, scroll.ScrollBarMaximum.Y - 1);
            var anchor = Assert.IsType<ListBoxItem>(list.ContainerFromItem(selected));
            var anchorY = anchor.TranslatePoint(default, scroll)!.Value.Y;
            fixture.Room.AddSuperChat(new()
            {
                RoomId = "100", MessageId = "reading-sc", UserName = "翻阅时收到的 SC", PriceText = "¥30",
                Content = new string('长', 180)
            });
            PumpLayout(host);

            anchor = Assert.IsType<ListBoxItem>(list.ContainerFromItem(selected));
            Assert.Equal(anchorY, anchor.TranslatePoint(default, scroll)!.Value.Y, 0.5);
            Assert.InRange(scroll.Offset.Y, 0, scroll.ScrollBarMaximum.Y - 1);
            fixture.Room.AddSuperChat(new()
            {
                RoomId = "100", MessageId = "pending-sc", UserName = "排队中的 SC", PriceText = "¥30",
                Content = new string('新', 180)
            });

            ClickButton(host, button);
            PumpLayout(host);

            Assert.Equal(scroll.ScrollBarMaximum.Y, scroll.Offset.Y, 0.5);
            Assert.Same(selected, list.SelectedItem);
            fixture.Room.AddSuperChat(new()
            {
                RoomId = "100", MessageId = "following-sc", UserName = "恢复跟随后的 SC", PriceText = "¥30",
                Content = "最新 SC"
            });
            PumpLayout(host);
            Assert.Equal(scroll.ScrollBarMaximum.Y, scroll.Offset.Y, 0.5);
            Assert.NotNull(list.ContainerFromItem(fixture.Room.SuperChats[^1]));
            fixture.Room.SuperChats.Clear();
            PumpLayout(host);
            ClickButton(host, button);
            PumpLayout(host);
            Assert.Equal(0, scroll.Offset.Y);
            fixture.Room.AddSuperChat(new() { RoomId = "100", MessageId = "first-sc", Content = "首条 SC" });
            PumpLayout(host);
            var first = Assert.IsType<ListBoxItem>(list.ContainerFromItem(fixture.Room.SuperChats[0]));
            Assert.InRange(first.TranslatePoint(default, scroll)!.Value.Y, 0, 0.5);
            fixture.ViewModel.SelectedWorkspace!.SelectedRoom = null;
            PumpLayout(host);
            Assert.False(button.IsEffectivelyEnabled);
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    /// <summary>
    /// SC 与弹幕快捷键独立作用，从主窗或浮窗输入时保留草稿、光标和输入焦点。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShortcutWorksFromComposerAndKeepsDanmuScrollIndependent(bool floatingComposer)
    {
        using var fixture = new LiveRoomFeedViewTestContext();
        PopulateFeed(fixture.Room);

        for (var index = 0; index < 80; index++)
        {
            fixture.Room.DanmuItems.Add(new() { UserName = "弹幕用户", Content = $"历史弹幕 {index}" });
        }

        var main = new MainWindow { DataContext = fixture.ViewModel, Width = 1100, Height = 800 };

        try
        {
            main.Show();
            await main.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            Window host = main;

            if (floatingComposer)
            {
                workbench.Layouts.FloatPanel(WorkbenchPanelIds.COMPOSER);
                host = Assert.Single(workbench.Layouts.FloatingWindows);
            }

            PumpLayout(main, host);
            host.Activate();
            workbench.Panels.FocusInputDraft();
            PumpLayout(main, host);
            var input = workbench.Panels.Composer.FindControl<TextBox>("InputDraftTextBox")!;
            input.Text = "保留 SC 回到最新前的输入内容";
            input.CaretIndex = 3;
            var list = workbench.Panels.SuperChat.GetVisualDescendants().OfType<ListBox>().Single();
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
            var danmuScroll = workbench.Panels.Danmu.FindControl<ScrollViewer>("DanmuScrollViewer")!;
            list.ScrollIntoView(40);
            danmuScroll.Offset = new(0, 180);
            PumpLayout(main, host);
            Assert.InRange(scroll.Offset.Y, 0, scroll.ScrollBarMaximum.Y - 1);
            Assert.InRange(danmuScroll.Offset.Y, 0, danmuScroll.ScrollBarMaximum.Y - 1);
            Assert.True(input.IsFocused);
            var modifiers = RawInputModifiers.Alt | RawInputModifiers.Shift;

            host.KeyPressQwerty(PhysicalKey.Home, modifiers);
            host.KeyReleaseQwerty(PhysicalKey.Home, modifiers);
            PumpLayout(main, host);

            Assert.Equal(scroll.ScrollBarMaximum.Y, scroll.Offset.Y, 0.5);
            Assert.InRange(danmuScroll.Offset.Y, 0, danmuScroll.ScrollBarMaximum.Y - 1);
            Assert.Equal("保留 SC 回到最新前的输入内容", input.Text);
            Assert.Equal(3, input.CaretIndex);
            Assert.True(input.IsFocused);
            list.ScrollIntoView(40);
            PumpLayout(main, host);
            host.KeyPressQwerty(PhysicalKey.Home, RawInputModifiers.Alt);
            host.KeyReleaseQwerty(PhysicalKey.Home, RawInputModifiers.Alt);
            PumpLayout(main, host);
            Assert.Equal(danmuScroll.ScrollBarMaximum.Y, danmuScroll.Offset.Y, 0.5);
            Assert.InRange(scroll.Offset.Y, 0, scroll.ScrollBarMaximum.Y - 1);
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    /// <summary>
    /// SC 标题栏和快捷键设置在切换语言时即时刷新，浮动重挂后仍使用同一命令。
    /// </summary>
    [AvaloniaFact]
    public async Task HeaderLabelsRefreshAndCommandSurvivesFloating()
    {
        using var language = new LocalizationTestScope();
        using var fixture = new LiveRoomFeedViewTestContext();
        var main = new MainWindow { DataContext = fixture.ViewModel };

        try
        {
            main.Show();
            await main.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            PumpLayout(main);
            var command = GetHeaderButton(main).Command;
            Assert.NotNull(command);
            workbench.Layouts.FloatPanel(WorkbenchPanelIds.SUPER_CHAT);
            var floating = Assert.Single(workbench.Layouts.FloatingWindows);
            PumpLayout(main, floating);
            var button = GetHeaderButton(floating);
            var group = fixture.ViewModel.ShortcutBindingGroups.Single(item =>
                item.Bindings.Any(binding => binding.ActionKey == "superChat.scrollToTop"));
            var binding = Assert.Single(group.Bindings);

            foreach (var languageId in new[] { "zh-CN", "en-US" })
            {
                Localization.SetLanguage(languageId);
                PumpLayout(main, floating);

                Assert.Same(button, GetHeaderButton(floating));
                Assert.Same(command, button.Command);
                Assert.Equal(languageId == "zh-CN" ? "回到最新" : "Go to latest", ToolTip.GetTip(button));
                Assert.Equal(ToolTip.GetTip(button), AutomationProperties.GetName(button));
                Assert.Equal("SC", group.Name);
                Assert.Equal(languageId == "zh-CN" ? "SC 回到最新" : "Go to latest SC", binding.Name);
            }
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    private static void PopulateFeed(LiveRoomTabViewModel room)
    {
        room.SuperChats.Clear();

        for (var index = 0; index < 100; index++)
        {
            room.SuperChats.Add(new()
            {
                Kind = index % 5 == 0 ? LivePaidEventKind.GuardPurchase : LivePaidEventKind.SuperChat,
                UserName = $"用户 {index}", Content = $"历史 SC {index}", PriceText = "¥30", GuardLevel = 3
            });
        }
    }

    private static Button GetHeaderButton(Window window)
    {
        return Assert.Single(window.GetVisualDescendants().OfType<Button>(),
            button => button.Name == "SuperChatScrollToLatestButton" && button.IsEffectivelyVisible);
    }

    private static void ClickButton(Window window, Button button)
    {
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }

    private static void PumpLayout(params Window[] windows)
    {
        for (var pass = 0; pass < 2; pass++)
        {
            Dispatcher.UIThread.RunJobs();

            foreach (var window in windows)
            {
                window.UpdateLayout();
            }
        }
    }
}

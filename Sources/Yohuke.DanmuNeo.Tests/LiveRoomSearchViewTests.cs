using Yohuke.DanmuCore.Models.Search;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lucide.Avalonia;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Views.Components;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 添加弹窗的真实绑定和布局测试。
/// </summary>
public class LiveRoomSearchViewTests
{
    /// <summary>
    /// 搜索结果显示主播头像，并通过主视图模型命令添加房间。
    /// </summary>
    [AvaloniaFact]
    public async Task ResultRowShowsOwnerAvatarAndAddsRoom()
    {
        using var context = new LiveRoomSearchTestContext();
        var avatarPath = Path.Combine(context.StateService.ConfigDirectory, "search-avatar.png");
        using (var file = File.Create(avatarPath))
        {
            using var asset = AssetLoader.Open(new("avares://Yohuke.DanmuNeo/Assets/favicon.png"));
            asset.CopyTo(file);
        }
        context.UserAvatars.AvatarPath = avatarPath;

        var result = LiveRoomSearchTestContext.CreateResult();
        context.SearchService.RespondAsync = (_, _) => Task.FromResult(new LiveRoomSearchPage { Results = [result] });
        var viewModel = context.ViewModel;
        var target = viewModel.Workspaces[0];
        viewModel.OpenAddRoomDialog(target);
        viewModel.LiveRoomSearchText = "主播";
        await viewModel.SearchLiveRoomsAsync();
        var view = new AddWorkspaceDialogView { DataContext = viewModel };
        var window = new Window { Width = 820, Height = 600, Content = view };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var avatar = Assert.Single(view.GetVisualDescendants().OfType<UserAvatarView>());
            Assert.Equal(42L, avatar.UserUid);
            Assert.Same(viewModel.UserAvatars, avatar.AvatarService);
            await avatar.PendingLoad.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(avatar.AvatarImage);
            Assert.Equal(avatarPath, avatar.CurrentAvatarPath);

            var button = view.GetVisualDescendants().OfType<Button>()
                .Single(control => control.Name == "AddSearchedRoomButton");
            Assert.Same(viewModel.AddSearchedRoomCommand, button.Command);
            Assert.Same(Assert.Single(viewModel.LiveRoomSearchResults), button.CommandParameter);
            Assert.True(button.IsEffectivelyEnabled);
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!
                .Value;
            window.MouseMove(point);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            var execution = viewModel.AddSearchedRoomCommand.ExecutionTask;
            Assert.NotNull(execution);
            await execution;

            Assert.Equal("100", Assert.Single(target.Rooms).RoomId);
            Assert.False(viewModel.IsAddDialogOpen);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 短窗口中收起空标题、突出直播状态，并让底部操作保持可见。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(820, false)]
    [InlineData(440, true)]
    public void ShortDialogKeepsResultsCompactAndFooterVisible(int width, bool dark)
    {
        using var context = new LiveRoomSearchTestContext();
        var viewModel = context.ViewModel;
        viewModel.OpenAddRoomDialog(null);
        var longTitle = new string('题', 80);

        for (var index = 0; index < 42; index++)
        {
            viewModel.LiveRoomSearchResults.Add(new(new()
            {
                RoomId = (100 + index).ToString(),
                OwnerUid = "42",
                OwnerName = index < 2 ? $"主播 {index + 1}" : new string('长', 80),
                Title = index switch { 0 => "", 1 => "  ", _ => longTitle },
                IsLive = index == 1
            }));
        }

        var view = new AddWorkspaceDialogView { DataContext = viewModel };
        var window = new Window
        {
            Width = width,
            Height = 450,
            Content = view,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(view.GetVisualDescendants().OfType<Expander>());
            var manualRoomId = view.GetVisualDescendants().OfType<TextBox>()
                .Single(control => control.Name == "ManualRoomIdTextBox");
            Assert.True(manualRoomId.IsEffectivelyVisible);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            foreach (var button in view.GetVisualDescendants().OfType<Button>()
                         .Where(control => control.Content is "取消" or "手动添加"))
            {
                var point = button.TranslatePoint(new Point(0, button.Bounds.Height), window)!.Value;
                Assert.InRange(point.Y, 0, window.Bounds.Height);
            }

            var results = view.GetVisualDescendants().OfType<ItemsControl>()
                .Single(control => control.Name == "LiveRoomSearchResultsControl");
            var scrollViewer = results.GetVisualAncestors().OfType<ScrollViewer>().First();
            Assert.True(scrollViewer.Extent.Height > scrollViewer.Viewport.Height);
            Assert.True(scrollViewer.Extent.Width <= scrollViewer.Viewport.Width + 0.5);

            var resultTexts = results.GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.IsEffectivelyVisible).ToArray();
            Assert.DoesNotContain(resultTexts, text => string.IsNullOrWhiteSpace(text.Text));
            Assert.Contains(resultTexts, text => text.Text == longTitle);
            var liveLabel = Assert.Single(resultTexts, text => text.Text == Localization.Get("Room.Search.Live"));
            Assert.Same(viewModel.LiveRoomSearchResults[1], liveLabel.DataContext);
            var liveIcon = Assert.Single(results.GetVisualDescendants().OfType<LucideIcon>(),
                icon => icon.Kind == LucideIconKind.Radio && icon.IsEffectivelyVisible);
            var offlineDetails = Assert.Single(resultTexts, text => text.Text == viewModel.LiveRoomSearchResults[0].DetailLine);
            Assert.NotNull(liveLabel.Foreground);
            Assert.Same(liveLabel.Foreground, liveIcon.Foreground);
            Assert.NotSame(offlineDetails.Foreground, liveLabel.Foreground);

            var previewDirectory = Environment.GetEnvironmentVariable("YOHUKE_SEARCH_QA_OUTPUT");

            if (!string.IsNullOrWhiteSpace(previewDirectory))
            {
                Directory.CreateDirectory(previewDirectory);
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save(Path.Combine(previewDirectory, dark ? "live-room-search-dark.png" : "live-room-search.png"));
            }
        }
        finally
        {
            window.Close();
        }
    }
    /// <summary>同级手动表单能通过真实绑定离线添加，无需展开或在线查找。</summary>
    [AvaloniaFact]
    public async Task ManualRoomFormAddsOfflineThroughBoundCommand()
    {
        using var context = new LiveRoomSearchTestContext();
        context.ViewModel.Accounts[0].Cookie = "";
        context.ViewModel.OpenAddRoomDialog(null);
        var view = new AddWorkspaceDialogView { DataContext = context.ViewModel };
        var window = new Window { Width = 820, Height = 600, Content = view };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var roomId = view.GetVisualDescendants().OfType<TextBox>()
                .Single(control => control.Name == "ManualRoomIdTextBox");
            var previewDirectory = Environment.GetEnvironmentVariable("YOHUKE_SEARCH_QA_OUTPUT");

            if (!string.IsNullOrWhiteSpace(previewDirectory))
            {
                window.UpdateLayout();
                Directory.CreateDirectory(previewDirectory);
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save(Path.Combine(previewDirectory, "live-room-manual.png"));
            }

            roomId.Text = "12345";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("12345", context.ViewModel.NewRoomId);
            var button = view.GetVisualDescendants().OfType<Button>()
                .Single(control => Equals(control.Content, "手动添加"));
            Assert.True(button.Command!.CanExecute(null));
            button.Command.Execute(null);
            await view.ConfirmAddDialogCommand.ExecutionTask!;

            Assert.Equal("12345", Assert.Single(context.ViewModel.Workspaces[0].Rooms).RoomId);
            Assert.Empty(context.SearchService.FollowRequests);
            Assert.Empty(context.SearchService.Requests);
        }
        finally
        {
            window.Close();
        }
    }

}

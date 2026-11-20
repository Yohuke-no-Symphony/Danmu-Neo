using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>独立的当前房间礼物面板。</summary>
public partial class GiftPanelView : WorkbenchRoomPanel
{
    private readonly FeedScrollController scrollController;
    private readonly WorkbenchFeedInteraction interactions = null!;

    /// <summary>创建并连接工作台礼物面板。</summary>
    public GiftPanelView(WorkbenchContext context, WorkbenchInteractionCoordinator coordinator) : this()
    {
        interactions = new(this, context, coordinator);
        InitializeContext(context);
    }

    /// <summary>创建供 XAML 加载器使用的礼物面板。</summary>
    public GiftPanelView()
    {
        InitializeComponent();
        scrollController = new(GiftListBox);
        ScrollToLatestCommand = new RelayCommand(scrollController.ScrollToLatest, () => DataContext is LiveRoomTabViewModel);
    }

    /// <summary>回到最新礼物记录并恢复自动跟随。</summary>
    public IRelayCommand ScrollToLatestCommand { get; }

    /// <inheritdoc/>
    protected override void OnCurrentRoomChanged(LiveRoomTabViewModel? oldRoom, LiveRoomTabViewModel? newRoom)
    {
        scrollController.Attach(newRoom?.GiftItems);
        ScrollToLatestCommand.NotifyCanExecuteChanged();
    }

    /// <inheritdoc/>
    protected override void DisposePanel()
    {
        scrollController.Dispose();
        interactions?.Dispose();
    }

    private async void FeedUserName_OnClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is Button { Content: TextBlock { Text: { } name } })
        {
            await interactions.CopyAsync(name);
        }
    }

    private void Gift_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: GiftFeedItem item } button)
        {
            interactions.Insert(button, item);
        }
    }

    private void Gift_OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is Button { DataContext: GiftFeedItem item } button && Context.CurrentRoom is { } room)
        {
            e.Handled = true;
            interactions.CreateDanmuContextMenu(item, room).Open(button);
        }
    }
}

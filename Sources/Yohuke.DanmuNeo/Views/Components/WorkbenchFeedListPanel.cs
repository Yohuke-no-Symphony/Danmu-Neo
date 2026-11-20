using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>为礼物和红包列表共享滚动、复制与消息交互。</summary>
public abstract class WorkbenchFeedListPanel : WorkbenchRoomPanel
{
    private FeedScrollController scrollController = null!;
    private WorkbenchFeedInteraction interactions = null!;

    /// <summary>创建面板标题栏使用的滚动命令。</summary>
    protected WorkbenchFeedListPanel()
    {
        ScrollToLatestCommand = new RelayCommand(() => scrollController.ScrollToLatest(),
            () => DataContext is LiveRoomTabViewModel);
    }

    /// <summary>回到最新记录并恢复自动跟随。</summary>
    public IRelayCommand ScrollToLatestCommand { get; }

    /// <summary>将面板的列表连接到滚动控制器。</summary>
    protected void InitializeFeedList(ListBox list)
    {
        scrollController = new(list);
    }

    /// <summary>连接工作台上下文与共用的消息交互。</summary>
    protected void InitializeFeedContext(WorkbenchContext context, WorkbenchInteractionCoordinator coordinator)
    {
        interactions = new(this, context, coordinator);
        InitializeContext(context);
    }

    /// <summary>取得面板当前需要跟随的消息集合。</summary>
    protected abstract INotifyCollectionChanged? GetFeedItems(LiveRoomTabViewModel? room);

    /// <inheritdoc/>
    protected sealed override void OnCurrentRoomChanged(LiveRoomTabViewModel? oldRoom, LiveRoomTabViewModel? newRoom)
    {
        scrollController.Attach(GetFeedItems(newRoom));
        ScrollToLatestCommand.NotifyCanExecuteChanged();
    }

    /// <inheritdoc/>
    protected sealed override void DisposePanel()
    {
        scrollController.Dispose();
        interactions?.Dispose();
    }

    /// <summary>复制所点击消息的完整用户名。</summary>
    protected async void FeedUserName_OnClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is Button { Content: TextBlock { Text: { } name } })
        {
            await interactions.CopyAsync(name);
        }
    }

    /// <summary>将消息摘要插入来源房间的输入框。</summary>
    protected void Message_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DanmuFeedItem item } button)
        {
            interactions.Insert(button, item);
        }
    }

    /// <summary>打开消息的复制、转发和用户管理菜单。</summary>
    protected void Message_OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is Button { DataContext: DanmuFeedItem item } button && Context.CurrentRoom is { } room)
        {
            e.Handled = true;
            interactions.CreateDanmuContextMenu(item, room).Open(button);
        }
    }
}

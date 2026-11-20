using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 独立弹幕列表，保留翻阅锚点和上下文操作。
/// </summary>
public partial class DanmuPanelView : WorkbenchRoomPanel
{
    private readonly FeedScrollController scrollController;
    private readonly WorkbenchFeedInteraction interactions = null!;

    /// <summary>
    /// 创建并连接弹幕面板。
    /// </summary>
    public DanmuPanelView(WorkbenchContext context, WorkbenchInteractionCoordinator coordinator) : this()
    {
        interactions = new(this, context, coordinator);
        InitializeContext(context);
    }

    /// <summary>创建供 XAML 加载器使用的弹幕面板。</summary>
    public DanmuPanelView()
    {
        ScrollToLatestCommand = new RelayCommand(ScrollToLatest, () => DataContext is LiveRoomTabViewModel);
        InitializeComponent();
        DanmuItemsControl.AddHandler(Button.ClickEvent, Danmu_OnClick);
        DanmuItemsControl.AddHandler(ContextRequestedEvent, Danmu_OnContextRequested);
        DanmuItemsControl.ContainerClearing += Danmu_OnContainerClearing;
        scrollController = new(DanmuScrollViewer, DanmuItemsControl);
    }

    /// <summary>回到最新弹幕并恢复自动跟随的界面命令。</summary>
    public IRelayCommand ScrollToLatestCommand { get; }

    /// <summary>结束历史翻阅并回到最新弹幕。</summary>
    public void ScrollToLatest()
    {
        scrollController.ScrollToLatest();
    }

    /// <inheritdoc/>
    protected override void OnCurrentRoomChanged(LiveRoomTabViewModel? oldRoom, LiveRoomTabViewModel? newRoom)
    {
        scrollController.Attach(newRoom?.DanmuItems);
        ScrollToLatestCommand.NotifyCanExecuteChanged();
    }

    /// <inheritdoc/>
    protected override void DisposePanel()
    {
        DanmuItemsControl.ContainerClearing -= Danmu_OnContainerClearing;
        scrollController.Dispose();
        interactions?.Dispose();
    }

    internal ContextMenu CreateDanmuContextMenu(DanmuFeedItem item, LiveRoomTabViewModel room)
    {
        return interactions.CreateDanmuContextMenu(item, room);
    }

    private void Danmu_OnContainerClearing(object? sender, ContainerClearingEventArgs e)
    {
        if (e.Container is DanmuFeedRow row)
        {
            interactions?.ForgetActivation(row);
        }
    }

    private async void Danmu_OnClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Button { DataContext: DanmuFeedItem item } button)
        {
            if (button is DanmuFeedRow)
            {
                interactions.Insert(button, item);
            }
            else if (button.Classes.Contains("feedUserNameButton"))
            {
                e.Handled = true;
                await interactions.CopyAsync(item.UserName);
            }
        }
    }

    private void Danmu_OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Source is Control source && source.FindAncestorOfType<DanmuFeedRow>(includeSelf: true) is
            { DataContext: DanmuFeedItem item } row && Context.CurrentRoom is { } room && room.DanmuItems.Contains(item))
        {
            e.Handled = true;
            row.OpenMessageMenu(interactions.CreateDanmuContextMenu(item, room));
        }
    }
}

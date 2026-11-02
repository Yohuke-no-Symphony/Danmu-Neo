using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 独立 SC 与上舰消息面板。
/// </summary>
public partial class SuperChatPanelView : WorkbenchRoomPanel
{
    private readonly WorkbenchFeedInteraction interactions = null!;

    /// <summary>
    /// 创建并连接 SC 面板。
    /// </summary>
    public SuperChatPanelView(WorkbenchContext context, WorkbenchInteractionCoordinator coordinator) : this()
    {
        interactions = new(this, context, coordinator);
        InitializeContext(context);
    }

    /// <summary>创建供 XAML 加载器使用的 SC 面板。</summary>
    public SuperChatPanelView()
    {
        ScrollToTopCommand = new RelayCommand(ScrollToTop, () => DataContext is LiveRoomTabViewModel);
        InitializeComponent();
    }

    /// <summary>将当前房间的 SC 和上舰记录列表滚回顶部。</summary>
    public IRelayCommand ScrollToTopCommand { get; }

    /// <summary>通过列表原生滚动接口回到最新记录，保留选中项。</summary>
    public void ScrollToTop()
    {
        SuperChatListBox.ScrollIntoView(0);

        if (SuperChatListBox.Scroll is { } scroll)
        {
            scroll.Offset = new(scroll.Offset.X, 0);
        }
    }

    /// <inheritdoc/>
    protected override void OnCurrentRoomChanged(LiveRoomTabViewModel? oldRoom, LiveRoomTabViewModel? newRoom)
    {
        ScrollToTopCommand.NotifyCanExecuteChanged();
    }

    /// <inheritdoc/>
    protected override void DisposePanel()
    {
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

    private async void SuperChat_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SuperChatItem item } button)
        {
            return;
        }

        if (item.IsGuardPurchase)
        {
            await interactions.CopyAsync(item.UserName);
        }
        else if (item.CanInsertContent)
        {
            interactions.Insert(button, item);
        }
    }
}

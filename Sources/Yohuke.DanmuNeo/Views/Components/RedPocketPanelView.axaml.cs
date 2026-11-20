using System.Collections.Specialized;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>显示当前房间的红包和开奖状态。</summary>
public partial class RedPocketPanelView : WorkbenchFeedListPanel
{
    /// <summary>创建并连接工作台红包面板。</summary>
    public RedPocketPanelView(WorkbenchContext context, WorkbenchInteractionCoordinator coordinator) : this()
    {
        InitializeFeedContext(context, coordinator);
    }

    /// <summary>创建供 XAML 加载器使用的红包面板。</summary>
    public RedPocketPanelView()
    {
        InitializeComponent();
        InitializeFeedList(RedPocketListBox);
    }

    /// <inheritdoc/>
    protected override INotifyCollectionChanged? GetFeedItems(LiveRoomTabViewModel? room)
    {
        return room?.RedPocketItems;
    }
}

using System.Collections.Specialized;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>独立的当前房间礼物面板。</summary>
public partial class GiftPanelView : WorkbenchFeedListPanel
{
    /// <summary>创建并连接工作台礼物面板。</summary>
    public GiftPanelView(WorkbenchContext context, WorkbenchInteractionCoordinator coordinator) : this()
    {
        InitializeFeedContext(context, coordinator);
    }

    /// <summary>创建供 XAML 加载器使用的礼物面板。</summary>
    public GiftPanelView()
    {
        InitializeComponent();
        InitializeFeedList(GiftListBox);
    }

    /// <inheritdoc/>
    protected override INotifyCollectionChanged? GetFeedItems(LiveRoomTabViewModel? room)
    {
        return room?.GiftItems;
    }
}

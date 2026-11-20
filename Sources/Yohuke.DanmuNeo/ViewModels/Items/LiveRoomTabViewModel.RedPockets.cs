using System.Collections.ObjectModel;
using Avalonia.Threading;
using Yohuke.DanmuCore.Apis.Models.Bilibili;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.ViewModels.Items;

public partial class LiveRoomTabViewModel
{
    private const int MAX_RED_POCKET_ITEMS = 300;
    private readonly ObservableCollection<RedPocketFeedItem> redPocketItems = [];

    private void OnRedPocketReceived(object? sender, BilibiliRedPocketMessage message)
    {
        var context = activeReception;
        Dispatcher.UIThread.Post(() =>
        {
            if (context is not null && ReferenceEquals(sender, context.Socket) && IsCurrentReception(context))
            {
                AddRedPocket(message);
            }
        });
    }

    internal void AddRedPocket(BilibiliRedPocketMessage message)
    {
        if (message.RedPocket.LotId <= 0)
        {
            return;
        }

        var item = redPocketItems.FirstOrDefault(existing =>
            existing.RoomId == message.RoomId && existing.LotId == message.RedPocket.LotId);
        if (item is not null)
        {
            item.Update(message);
            return;
        }

        redPocketItems.Add(new(message));
        Trim(redPocketItems, MAX_RED_POCKET_ITEMS, oldestFirst: true);
    }
}

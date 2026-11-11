using Avalonia.Threading;
using Yohuke.DanmuNeo.Apis.Models.Bilibili;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.ViewModels.Items;

public partial class LiveRoomTabViewModel
{
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
        if (message.LotId <= 0)
        {
            return;
        }

        var item = DanmuItems.OfType<RedPocketFeedItem>()
            .FirstOrDefault(existing => existing.RoomId == message.RoomId && existing.LotId == message.LotId);
        if (item is not null)
        {
            item.Update(message);
            return;
        }

        DanmuItems.Insert(0, new RedPocketFeedItem(message));
        TrimDanmuItems();
    }
}

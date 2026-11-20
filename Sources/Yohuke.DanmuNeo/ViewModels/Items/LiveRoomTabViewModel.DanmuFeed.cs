using System.Collections.Specialized;
using Avalonia.Threading;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.ViewModels.Items;

public partial class LiveRoomTabViewModel
{
    private const int MAX_DANMU_ITEMS = 300;
    private const int NEW_ITEM_INDEX = -1;

    private readonly DanmuFeedCollection danmuItems = new();
    private readonly List<DanmuFeedUpdate> pendingDanmuFeedUpdates = [];
    private bool isDanmuFeedPublicationQueued;

    private void QueueDanmuFeedUpdate(DanmuFeedUpdate update)
    {
        pendingDanmuFeedUpdates.Add(update);
        if (pendingDanmuFeedUpdates.Count >= MAX_DANMU_ITEMS)
        {
            PublishPendingDanmuFeedUpdates();
        }
    }

    private void ScheduleDanmuFeedPublication()
    {
        if (isDanmuFeedPublicationQueued || pendingDanmuFeedUpdates.Count == 0)
        {
            return;
        }

        isDanmuFeedPublicationQueued = true;
        // Default-priority reception callbacks enter the batch before it reaches the list.
        Dispatcher.UIThread.Post(() =>
        {
            isDanmuFeedPublicationQueued = false;
            PublishPendingDanmuFeedUpdates();
        }, DispatcherPriority.Input);
    }

    private void OnDanmuFeedItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            pendingDanmuFeedUpdates.Clear();
        }
    }

    private void PublishPendingDanmuFeedUpdates()
    {
        if (isDisposed || pendingDanmuFeedUpdates.Count == 0)
        {
            return;
        }

        var updates = pendingDanmuFeedUpdates.ToArray();
        pendingDanmuFeedUpdates.Clear();
        var projected = new List<(DanmuFeedItem Item, int OriginalIndex)>(danmuItems.Count + 1);
        for (var index = 0; index < danmuItems.Count; index++)
        {
            projected.Add((danmuItems[index], index));
        }

        foreach (var update in updates)
        {
            ApplyDanmuFeedUpdate(projected, update);
        }
        danmuItems.ApplyBatch(projected);

        if (emoticonCacheService is not { } cache)
        {
            return;
        }

        foreach (var (item, message, token) in updates)
        {
            if (message is { Emoticons.Count: > 0 } && !token.IsCancellationRequested && danmuItems.Contains(item))
            {
                _ = LoadDanmuEmoticonsAsync(item, message.Emoticons, cache, token);
            }
        }
    }

    private static void ApplyDanmuFeedUpdate(List<(DanmuFeedItem Item, int OriginalIndex)> projected,
        DanmuFeedUpdate update)
    {
        var (item, message, _) = update;
        if (message is null)
        {
            if (!string.IsNullOrEmpty(item.SendRecordId))
            {
                var failedIndex = projected.FindIndex(row => row.Item.IsLocalRecord &&
                    row.Item.SendRecordId == item.SendRecordId);
                if (failedIndex >= 0)
                {
                    projected.RemoveAt(failedIndex);
                }
            }

            if (item.IsSendAccepted)
            {
                return;
            }
        }

        var index = projected.Count;
        if (item.IsHistory && message is not null)
        {
            index = message.Timestamp > 0 ? projected.Count : 0;
            while (index > 0 && projected[index - 1].Item.Time >= item.Time)
            {
                index--;
            }
        }

        projected.Insert(index, (item, NEW_ITEM_INDEX));
        if (projected.Count > MAX_DANMU_ITEMS)
        {
            projected.RemoveRange(0, projected.Count - MAX_DANMU_ITEMS);
        }
    }
}

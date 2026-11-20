using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.ViewModels.Items;

internal sealed class DanmuFeedCollection : ObservableCollection<DanmuFeedItem>
{
    private static readonly PropertyChangedEventArgs COUNT_CHANGED = new(nameof(Count));
    private static readonly PropertyChangedEventArgs ITEMS_CHANGED = new("Item[]");

    internal void InsertRange(int index, DanmuFeedItem[] added)
    {
        if (added.Length == 0)
        {
            return;
        }

        CheckReentrancy();
        ((List<DanmuFeedItem>)Items).InsertRange(index, added);
        OnPropertyChanged(COUNT_CHANGED);
        OnPropertyChanged(ITEMS_CHANGED);
        OnCollectionChanged(new(NotifyCollectionChangedAction.Add, (IList)added, index));
    }

    internal void ApplyBatch(IReadOnlyList<(DanmuFeedItem Item, int OriginalIndex)> projected)
    {
        // Retained rows keep their original relative order, even when several entries reference the same item.
        var end = Count;
        for (var index = projected.Count - 1; index >= 0; index--)
        {
            var originalIndex = projected[index].OriginalIndex;
            if (originalIndex >= 0)
            {
                RemoveRange(originalIndex + 1, end - originalIndex - 1);
                end = originalIndex;
            }
        }
        RemoveRange(0, end);

        var position = 0;
        while (position < projected.Count)
        {
            if (projected[position].OriginalIndex >= 0)
            {
                position++;
                continue;
            }

            var start = position;
            while (++position < projected.Count && projected[position].OriginalIndex < 0)
            {
            }

            var added = new DanmuFeedItem[position - start];
            for (var index = 0; index < added.Length; index++)
            {
                added[index] = projected[start + index].Item;
            }
            InsertRange(start, added);
        }
    }

    private void RemoveRange(int index, int count)
    {
        if (count == 0)
        {
            return;
        }

        CheckReentrancy();
        var items = (List<DanmuFeedItem>)Items;
        var removed = new DanmuFeedItem[count];
        items.CopyTo(index, removed, 0, count);
        items.RemoveRange(index, count);
        OnPropertyChanged(COUNT_CHANGED);
        OnPropertyChanged(ITEMS_CHANGED);
        OnCollectionChanged(new(NotifyCollectionChangedAction.Remove, (IList)removed, index));
    }
}

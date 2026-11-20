using System.Collections.Specialized;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证显示合批的范围通知及消息接收边界。</summary>
[Collection(AvaloniaDispatcherTestCollection.NAME)]
public sealed class DanmuFeedBatchTests
{
    /// <summary>同一轮实时突发只裁掉一个旧区间并追加一个新区间。</summary>
    [AvaloniaFact]
    public void LiveBurstPublishesTwoRangesAndPreservesRetainedReferences()
    {
        var socket = new FakeLiveDanmuSocket { KeepRunning = true };
        using var room = CreateRoom(socket, new());
        for (var index = 0; index < 300; index++)
        {
            room.DanmuItems.Add(new() { RoomId = "100", Content = $"旧消息 {index}" });
        }

        var previous = room.DanmuItems.ToArray();
        var expected = Enumerable.Range(0, 40).Select(index => $"新消息 {index}").ToArray();
        var changes = new List<NotifyCollectionChangedEventArgs>();
        room.DanmuItems.CollectionChanged += (_, change) => changes.Add(change);
        room.StartListening();
        for (var index = 0; index < expected.Length; index++)
        {
            socket.EmitDanmu(new() { RoomId = "100", Content = expected[index], Timestamp = 1800000000 - index });
        }

        Dispatcher.UIThread.RunJobs(DispatcherPriority.Default);
        Assert.Empty(changes);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(300, room.DanmuItems.Count);
        Assert.Same(previous[40], room.DanmuItems[0]);
        Assert.Same(previous[^1], room.DanmuItems[259]);
        Assert.Equal(expected, room.DanmuItems.Skip(260).Select(item => item.Content));
        Assert.Collection(changes, change =>
        {
            Assert.Equal(NotifyCollectionChangedAction.Remove, change.Action);
            Assert.Equal(0, change.OldStartingIndex);
            Assert.Equal(40, change.OldItems!.Count);
        }, change =>
        {
            Assert.Equal(NotifyCollectionChangedAction.Add, change.Action);
            Assert.Equal(260, change.NewStartingIndex);
            Assert.Equal(40, change.NewItems!.Count);
        });
    }

    /// <summary>停止监听提交已接收的弹幕，并拒绝旧连接仍在排队的消息。</summary>
    [AvaloniaFact]
    public void StoppingPublishesAcceptedMessagesAndRejectsQueuedOldMessages()
    {
        var socket = new FakeLiveDanmuSocket { KeepRunning = true };
        using var room = CreateRoom(socket, new());
        room.StartListening();
        socket.EmitDanmu(new() { RoomId = "100", Content = "已接收" });
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Default);
        Assert.Empty(room.DanmuItems);

        socket.EmitDanmu(new() { RoomId = "100", Content = "尚未接收" });
        room.StopListening();
        Assert.Equal("已接收", Assert.Single(room.DanmuItems).Content);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("已接收", Assert.Single(room.DanmuItems).Content);
    }

    /// <summary>清空列表后继续显示新弹幕。</summary>
    [AvaloniaFact]
    public void ClearingDropsPendingRowsAndKeepsNewMessages()
    {
        var socket = new FakeLiveDanmuSocket { KeepRunning = true };
        using var room = CreateRoom(socket, new());
        room.StartListening();
        socket.EmitDanmu(new() { RoomId = "100", Content = "清空前" });
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Default);

        room.DanmuItems.Clear();
        socket.EmitDanmu(new() { RoomId = "100", Content = "清空后" });
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("清空后", Assert.Single(room.DanmuItems).Content);
    }

    /// <summary>同批失败和成功记录按发送顺序处理，翻译历史保留重试结果。</summary>
    [AvaloniaFact]
    public async Task SuccessfulRetrySupersedesFailureWithinTheSameBatch()
    {
        var sender = new FakeDanmuSendService();
        sender.AcceptanceResults.Enqueue(false);
        sender.AcceptanceResults.Enqueue(true);
        using var room = CreateRoom(new(), sender);
        var request = new DanmuSendRequest { RoomId = "100", Contents = [new() { Body = "测试重试" }] };
        var changes = new List<NotifyCollectionChangedEventArgs>();
        room.DanmuItems.CollectionChanged += (_, change) => changes.Add(change);

        var failure = await sender.SendAsync(request, null, new());
        var retry = await sender.SendAsync(request, null, new(), failure.Progress);
        Dispatcher.UIThread.RunJobs();

        Assert.True(retry.IsComplete);
        Assert.Same(Assert.Single(retry.Results).Record, Assert.Single(room.TranslateHistoryItems));
        Assert.Empty(room.DanmuItems);
        Assert.Empty(changes);
    }

    private static LiveRoomTabViewModel CreateRoom(FakeLiveDanmuSocket socket, FakeDanmuSendService sender)
    {
        var account = new BilibiliAccount { Id = "account", Cookie = "fake" };
        var room = new LiveRoomTabViewModel(new() { RoomId = "100" });
        room.Configure(_ => account, (_, _) => account, () => new(), () => new(), () => [], _ => [],
            sender, new FakeRoomAvatarService(), () => Task.CompletedTask,
            liveDanmuSocketFactory: new SequenceLiveDanmuSocketFactory(socket));
        return room;
    }
}

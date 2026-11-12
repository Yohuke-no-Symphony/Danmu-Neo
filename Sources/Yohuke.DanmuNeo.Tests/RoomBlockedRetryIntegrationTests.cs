using System.Threading;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Yohuke.DanmuCore.Apis.Models.Bilibili;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证同传、双语歌词、自动歌词和转发共享屏蔽重发及房间生命周期。</summary>
public class RoomBlockedRetryIntegrationTests
{
    /// <summary>零宽子段部分失败后恢复原文草稿，续发更新原历史且不重复已接受正文。</summary>
    [AvaloniaFact]
    public async Task PartialDraftRetryRestoresOriginalAndContinuesWithoutDuplicateHistory()
    {
        var transport = new ScriptedDanmuSendTransport();
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.Blocked });
        transport.Results.Enqueue(new());
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.Blocked });
        using var room = CreateRoom(transport, limit: 12);
        room.InputDraft = "甲乙丙丁戊己庚辛";

        await room.SendDraftAsync();
        Dispatcher.UIThread.RunJobs();

        Assert.True(room.IsDraftSendFailed);
        Assert.Equal("甲乙丙丁戊己庚辛", room.InputDraft);
        Assert.Equal(2, room.TranslateHistoryItems.Count);
        var failedId = Assert.Single(room.TranslateHistoryItems, item => !item.IsSendAccepted).SendRecordId;

        await room.SendDraftAsync();
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(room.InputDraft);
        Assert.False(room.IsDraftSendFailed);
        Assert.Equal(3, room.TranslateHistoryItems.Count);
        Assert.All(room.TranslateHistoryItems, record => Assert.True(record.IsSendAccepted));
        Assert.Contains(room.TranslateHistoryItems, record => record.SendRecordId == failedId);
        Assert.DoesNotContain(room.DanmuItems, record => record.IsLocalRecord);
        Assert.Equal(["【甲乙丙丁】", "【甲\u200B乙】", "【…丙\u200B丁】", "【…丙丁戊】", "【…己庚辛】"],
            transport.Messages);
    }

    /// <summary>双语歌词只处理被屏蔽语言，失败时停留原行，成功后才标记并前进。</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BilingualRetryPreservesOriginalAndAdvancesOnlyWhenComplete(bool accepted)
    {
        var transport = new ScriptedDanmuSendTransport();
        transport.Results.Enqueue(new());
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.Blocked });
        if (!accepted)
        {
            transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.Blocked });
        }
        using var room = CreateRoom(transport);
        room.LyricInput = "[00:01]原文\n[00:10]下一句";
        room.TranslatedLyricInput = "[00:01]译文";
        room.ApplyLyric();
        var line = room.ActiveLyricLine!;

        await room.SendCurrentLyricAsync();

        Assert.Equal(accepted, line.IsSent);
        Assert.Equal(["【♪原文】", "【♪译文】", "【♪译\u200B文】"], transport.Messages);
        Assert.Equal("原文", line.Content);
        Assert.Equal("译文", line.TranslatedContent);
        if (!accepted)
        {
            Assert.Same(line, room.ActiveLyricLine);
            await room.SendCurrentLyricAsync();
            Assert.Equal("【♪译文】", transport.Messages[^1]);
        }
        Dispatcher.UIThread.RunJobs();

        Assert.True(line.IsSent);
        Assert.Same(room.Lyrics[1], room.ActiveLyricLine);
        Assert.Equal(2, room.TranslateHistoryItems.Count);
        Assert.Single(transport.Messages, message => message == "【♪原文】");
    }

    /// <summary>自动歌词经过统一服务重发，歌词原文和已发送去重状态保持一致。</summary>
    [AvaloniaFact]
    public async Task AutomaticLyricsUseBlockedRetryWithoutChangingLibraryText()
    {
        var transport = new ScriptedDanmuSendTransport();
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.Blocked });
        using var room = CreateRoom(transport);
        room.LyricInput = "[00:00]原文\n[00:10]下一句";
        room.ApplyLyric();
        var playback = room.StartAutoLyricAsync();
        try
        {
            Assert.True(room.Lyrics[0].IsSent);
            Assert.Equal("原文", room.Lyrics[0].Content);
            Assert.Equal(["【♪原文】", "【♪原\u200B文】"], transport.Messages);
        }
        finally
        {
            room.StopAutoLyric();
            await playback.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    /// <summary>转发使用相同重发管线，不修改来源消息。</summary>
    [AvaloniaFact]
    public async Task ForwardedMessagesUseBlockedRetryWithoutChangingSource()
    {
        var source = new ForwardSourceRoomOption
        {
            WorkspaceId = "workspace", RoomStateId = "source", RoomId = "100", RoomName = "来源"
        };
        var message = new BilibiliDanmuMessage { RoomId = "100", Uid = 42, Content = "转发内容" };
        var sockets = new FakeLiveDanmuSocketFactory();
        sockets.Socket.NextMessage = message;
        sockets.Socket.KeepRunning = true;
        var transport = new ScriptedDanmuSendTransport();
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.Blocked });
        var settings = new AppSettings { SendIntervalMs = 0 };
        var account = new BilibiliAccount { Id = "account", Cookie = "test-cookie" };
        var config = new FakeBilibiliDanmuConfigService();
        var sender = new DanmuSendService(danmuConfigService: config, transport: transport);
        var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sender.RecordCreated += (_, record) => accepted.TrySetResult();
        using var room = new LiveRoomTabViewModel(new()
        {
            RoomId = "200",
            ForwardRules =
            [
                new()
                {
                    IsEnabled = true, SourceWorkspaceId = source.WorkspaceId,
                    SourceRoomStateId = source.RoomStateId, SenderUid = "42"
                }
            ]
        });
        room.Configure(_ => account, (_, _) => account, () => settings,
            () => MarkSymbolService.CreateDefaultGroup(settings), () => [], _ => [source], sender,
            new FakeRoomAvatarService(), () => Task.CompletedTask, liveDanmuSocketFactory: sockets,
            userNameService: new FakeBilibiliUserNameService(), danmuConfigService: config);

        room.RestartForwarding();
        await accepted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(["【转发内容】", "【转\u200B发\u200B内\u200B容】"], transport.Messages);
        Assert.Equal("转发内容", message.Content);
    }

    /// <summary>房间释放撤销等待中的零宽重发，迟到结果不回写草稿或历史。</summary>
    [AvaloniaFact]
    public async Task RoomDisposalCancelsRetryIntervalWithoutLateFeedback()
    {
        var transport = new ScriptedDanmuSendTransport();
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.Blocked });
        var clock = new ManualSendTimeProvider();
        using var room = CreateRoom(transport, new() { SendIntervalMs = 750 }, clock: clock);
        room.InputDraft = "原始草稿";
        var sending = room.SendDraftAsync();
        await clock.WaitForTimerAsync();

        room.Dispose();
        await sending.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromMinutes(1));
        Dispatcher.UIThread.RunJobs();

        Assert.Single(transport.Messages);
        Assert.Empty(room.InputDraft);
        Assert.Empty(room.TranslateHistoryItems);
    }

    private static LiveRoomTabViewModel CreateRoom(ScriptedDanmuSendTransport transport,
        AppSettings? settings = null, int limit = 40, TimeProvider? clock = null)
    {
        settings ??= new() { SendIntervalMs = 0 };
        var account = new BilibiliAccount { Id = "account", Cookie = "test-cookie" };
        var config = new FakeBilibiliDanmuConfigService { Config = new() { Length = limit } };
        var sender = new DanmuSendService(danmuConfigService: config, transport: transport, timeProvider: clock);
        var room = new LiveRoomTabViewModel(new() { RoomId = "100" });
        room.Configure(_ => account, (_, _) => account, () => settings,
            () => MarkSymbolService.CreateDefaultGroup(settings), () => [], _ => [], sender,
            new FakeRoomAvatarService(), () => Task.CompletedTask, danmuConfigService: config);
        return room;
    }
}

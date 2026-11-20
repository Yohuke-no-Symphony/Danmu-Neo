using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Yohuke.DanmuCore.Apis.Models.Bilibili;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>锁定共享协议迁移后的 Neo 展示与接收策略。</summary>
[Collection(AvaloniaDispatcherTestCollection.NAME)]
public sealed class CoreReceptionPolicyTests
{
    /// <summary>共享连接发布的非金币礼物不进入 Neo 的接收队列。</summary>
    [AvaloniaTheory]
    [InlineData("silver")]
    [InlineData("unknown")]
    public void SocketGiftFilteringKeepsOnlyGold(string coinType)
    {
        var socket = new FakeLiveDanmuSocket { KeepRunning = true };
        using var context = new RoomModerationTestContext(new SequenceLiveDanmuSocketFactory(socket));
        context.Room.StartListening();
        socket.EmitGift(new()
        {
            RoomId = "100", Uid = 42, UserName = "用户", GiftName = "非金币礼物", Quantity = 1, CoinType = coinType
        });
        socket.EmitGift(new()
        {
            RoomId = "100", Uid = 42, UserName = "用户", GiftName = "金币礼物", Quantity = 1, CoinType = "gold"
        });
        Dispatcher.UIThread.RunJobs();

        Assert.Same(context.Item, Assert.Single(context.Room.DanmuItems));
        var gift = Assert.Single(context.Room.GiftItems);
        Assert.Contains("金币礼物", gift.Content);
        Assert.DoesNotContain("非金币礼物", gift.Content);
    }

    /// <summary>礼物容量独立于弹幕，超过上限后不再更新已移除的连击条目。</summary>
    [Fact]
    public void GiftFeedExpiresOldestRowsAndTheirComboState()
    {
        using var context = new RoomModerationTestContext();
        var combo = new BilibiliGiftMessage
        {
            RoomId = "100", Uid = 42, UserName = "用户", GiftName = "连击礼物", Quantity = 1, BatchComboId = "combo"
        };
        context.Room.AddGift(combo);
        var oldest = Assert.Single(context.Room.GiftItems);

        for (var index = 0; index < 301; index++)
        {
            context.Room.AddDanmu(new() { RoomId = "100", Uid = 42, UserName = "用户", Content = $"弹幕 {index}" });
        }

        Assert.Same(oldest, Assert.Single(context.Room.GiftItems));
        var gift = new BilibiliGiftMessage
        {
            RoomId = "100", Uid = 43, UserName = "另一个用户", GiftName = "普通礼物", Quantity = 1
        };
        for (var index = 0; index < 300; index++)
        {
            context.Room.AddGift(gift);
        }

        context.Room.AddGift(combo);

        Assert.Equal(300, context.Room.GiftItems.Count);
        Assert.DoesNotContain(oldest, context.Room.GiftItems);
        Assert.Contains("连击礼物 × 1", context.Room.GiftItems[^1].Content);
        Assert.Equal("弹幕 1", context.Room.DanmuItems[0].Content);
        Assert.Equal("弹幕 300", context.Room.DanmuItems[^1].Content);
    }

    /// <summary>SC 使用协议有效颜色，缺失颜色由 Neo 自己回退。</summary>
    [Theory]
    [InlineData("", "#2A60B2")]
    [InlineData("#112233", "#112233")]
    public void SuperChatColorFallbackStaysInNeo(string suppliedColor, string expectedColor)
    {
        using var context = new RoomModerationTestContext();
        var message = new BilibiliSuperChatMessage
        {
            MessageId = "sc", RoomId = "100", UserName = "用户", Content = "留言", Price = 30, BorderColor = suppliedColor
        };

        context.Room.AddSuperChat(message);

        Assert.Equal(Color.Parse(expectedColor), Assert.Single(context.Room.SuperChats).BorderColor);
        Assert.Equal(suppliedColor, message.BorderColor);
    }

    /// <summary>接收的红包仅进入独立列表，开奖原位更新且状态不会回退。</summary>
    [AvaloniaFact]
    public void RedPocketDrawNotificationUpdatesTheExistingSummary()
    {
        var socket = new FakeLiveDanmuSocket { KeepRunning = true };
        using var context = new RoomModerationTestContext(new SequenceLiveDanmuSocketFactory(socket));
        context.Room.StartListening();
        socket.EmitRedPocket(new()
        {
            RoomId = "100", Timestamp = 1800000000,
            RedPocket = new() { LotId = 1, SenderUid = 42, SenderName = "发包人", TotalPrice = 2000 }
        });
        Dispatcher.UIThread.RunJobs();
        var item = Assert.Single(context.Room.RedPocketItems);
        Assert.Contains("等待开奖", item.SummaryText);
        socket.EmitRedPocket(new()
        {
            RoomId = "100", Timestamp = 1800000001, IsDrawn = true, RedPocket = new() { LotId = 1 }
        });
        socket.EmitRedPocket(new()
        {
            RoomId = "100", Timestamp = 1800000002, RedPocket = new() { LotId = 1 }
        });

        Dispatcher.UIThread.RunJobs();
        Assert.Same(context.Item, Assert.Single(context.Room.DanmuItems));
        Assert.Same(item, Assert.Single(context.Room.RedPocketItems));
        Assert.Equal("发包人", item.UserName);
        Assert.Contains("已开奖", item.SummaryText);
        Assert.Equal("￥2", item.ValueText);
    }
}

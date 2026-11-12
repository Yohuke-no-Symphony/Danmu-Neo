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

        var gift = Assert.Single(context.Room.DanmuItems, item => item.IsGift);
        Assert.Contains("金币礼物", gift.Content);
        Assert.DoesNotContain("非金币礼物", gift.Content);
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

    /// <summary>没有中奖名单的开奖通知也会原位更新摘要，后续等待通知不能使状态回退。</summary>
    [Fact]
    public void RedPocketDrawNotificationUpdatesTheExistingSummary()
    {
        using var context = new RoomModerationTestContext();
        context.Room.AddRedPocket(new()
        {
            RoomId = "100", Timestamp = 1800000000,
            RedPocket = new() { LotId = 1, SenderUid = 42, SenderName = "发包人", TotalPrice = 2000 }
        });
        var item = Assert.Single(context.Room.DanmuItems.OfType<RedPocketFeedItem>());
        context.Room.AddRedPocket(new()
        {
            RoomId = "100", Timestamp = 1800000001, IsDrawn = true, RedPocket = new() { LotId = 1 }
        });
        context.Room.AddRedPocket(new()
        {
            RoomId = "100", Timestamp = 1800000002, RedPocket = new() { LotId = 1 }
        });

        Assert.Same(item, Assert.Single(context.Room.DanmuItems.OfType<RedPocketFeedItem>()));
        Assert.Equal("发包人", item.UserName);
        Assert.Contains("已开奖", item.Content);
        Assert.Contains("￥2", item.Content);
    }
}

namespace Yohuke.DanmuNeo.Apis.Models.Bilibili;

/// <summary>用于直播弹幕摘要的红包通知或开奖更新。</summary>
public sealed record BilibiliRedPocketMessage
{
    /// <summary>接收消息的直播间 ID。</summary>
    public string RoomId { get; init; } = "";

    /// <summary>红包抽奖 ID。</summary>
    public long LotId { get; init; }

    /// <summary>发包用户 UID，未提供时为 0。</summary>
    public long Uid { get; init; }

    /// <summary>发包用户昵称。</summary>
    public string UserName { get; init; } = "";

    /// <summary>红包类型，未提供时为空。</summary>
    public int? Type { get; init; }

    /// <summary>协议是否明确隐藏发包用户身份。</summary>
    public bool? IsMystery { get; init; }

    /// <summary>红包原始总价值，礼物红包按千分之一元换算。</summary>
    public decimal? TotalPrice { get; init; }

    /// <summary>电池红包原始总额，按百分之一电池换算。</summary>
    public decimal? TotalBattery { get; init; }

    /// <summary>发包通知提供的电池数量。</summary>
    public decimal? Price { get; init; }

    /// <summary>是否已收到该红包的开奖消息。</summary>
    public bool IsDrawn { get; init; }

    /// <summary>服务器事件时间的 Unix 秒，未提供时为 0。</summary>
    public long Timestamp { get; init; }
}

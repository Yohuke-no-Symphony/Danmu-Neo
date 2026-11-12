using Yohuke.DanmuCore.Apis.Models.Bilibili;
using Yohuke.DanmuNeo.I18n;

namespace Yohuke.DanmuNeo.Models.Workspace;

/// <summary>在弹幕列表中原位合并红包内容和开奖状态的摘要。</summary>
public sealed class RedPocketFeedItem : DanmuFeedItem
{
    private const long MAX_UNIX_TIMESTAMP_SECONDS = 253402300799;
    private BilibiliRedPocketMessage message;

    /// <summary>红包抽奖 ID。</summary>
    public long LotId => message.RedPocket.LotId;

    /// <inheritdoc/>
    public override bool IsRedPocket => true;

    internal RedPocketFeedItem(BilibiliRedPocketMessage message)
    {
        this.message = message;
        RoomId = message.RoomId;
        Time = message.Timestamp is > 0 and <= MAX_UNIX_TIMESTAMP_SECONDS
            ? DateTimeOffset.FromUnixTimeSeconds(message.Timestamp)
            : DateTimeOffset.Now;
        RefreshLocalization();
    }

    internal void Update(BilibiliRedPocketMessage update)
    {
        var preferUpdate = !message.IsDrawn || update.IsDrawn;
        if (message.Timestamp > 0 && update.Timestamp > 0 && message.Timestamp != update.Timestamp)
        {
            preferUpdate = update.Timestamp > message.Timestamp;
        }

        var preferred = preferUpdate ? update : message;
        var fallback = preferUpdate ? message : update;
        message = preferred with
        {
            RedPocket = preferred.RedPocket with
            {
                SenderUid = preferred.RedPocket.SenderUid > 0 ? preferred.RedPocket.SenderUid : fallback.RedPocket.SenderUid,
                SenderName = string.IsNullOrWhiteSpace(preferred.RedPocket.SenderName)
                    ? fallback.RedPocket.SenderName : preferred.RedPocket.SenderName,
                Type = preferred.RedPocket.Type ?? fallback.RedPocket.Type,
                IsMystery = preferred.RedPocket.IsMystery ?? fallback.RedPocket.IsMystery,
                TotalPrice = preferred.RedPocket.TotalPrice ?? fallback.RedPocket.TotalPrice,
                TotalBattery = preferred.RedPocket.TotalBattery ?? fallback.RedPocket.TotalBattery,
                Price = preferred.RedPocket.Price ?? fallback.RedPocket.Price
            },
            IsDrawn = update.IsDrawn || message.IsDrawn,
            Timestamp = Math.Max(update.Timestamp, message.Timestamp)
        };
        RefreshLocalization();
    }

    /// <inheritdoc/>
    public override void RefreshLocalization()
    {
        base.RefreshLocalization();
        UserName = string.IsNullOrWhiteSpace(message.RedPocket.SenderName)
            ? Localization.Get("Room.RedPocket.UnknownSender")
            : message.RedPocket.SenderName;
        UserUid = message.RedPocket.IsMystery == true || message.RedPocket.SenderUid <= 0
            ? ""
            : message.RedPocket.SenderUid.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var value = FormatValue();
        Content = Localization.Format(message.IsDrawn ? "Room.RedPocket.DrawnContent" : "Room.RedPocket.WaitingContent",
            value.Length > 0 ? $" · {value}" : "");
    }

    private string FormatValue()
    {
        if (message.RedPocket.Type is 3 or 5 && (message.RedPocket.TotalBattery ?? message.RedPocket.TotalPrice) is { } battery)
        {
            return Localization.Format("Room.RedPocket.BatteryValue", battery / 100m);
        }

        if (message.RedPocket.Type == 4)
        {
            return Localization.Get("Room.RedPocket.IntimacyValue");
        }

        if (message.RedPocket.TotalPrice is { } totalPrice)
        {
            return Localization.Format("Room.RedPocket.MoneyValue", totalPrice / 1000m);
        }

        return message.RedPocket.Price is { } price
            ? Localization.Format("Room.RedPocket.BatteryValue", price)
            : "";
    }
}

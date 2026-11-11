using Yohuke.DanmuNeo.Apis.Models.Bilibili;
using Yohuke.DanmuNeo.I18n;

namespace Yohuke.DanmuNeo.Models.Workspace;

/// <summary>在弹幕列表中原位合并红包内容和开奖状态的摘要。</summary>
public sealed class RedPocketFeedItem : DanmuFeedItem
{
    private const long MAX_UNIX_TIMESTAMP_SECONDS = 253402300799;
    private BilibiliRedPocketMessage message;

    /// <summary>红包抽奖 ID。</summary>
    public long LotId => message.LotId;

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
            Uid = preferred.Uid > 0 ? preferred.Uid : fallback.Uid,
            UserName = string.IsNullOrWhiteSpace(preferred.UserName) ? fallback.UserName : preferred.UserName,
            Type = preferred.Type ?? fallback.Type,
            IsMystery = preferred.IsMystery ?? fallback.IsMystery,
            TotalPrice = preferred.TotalPrice ?? fallback.TotalPrice,
            TotalBattery = preferred.TotalBattery ?? fallback.TotalBattery,
            Price = preferred.Price ?? fallback.Price,
            IsDrawn = update.IsDrawn || message.IsDrawn,
            Timestamp = Math.Max(update.Timestamp, message.Timestamp)
        };
        RefreshLocalization();
    }

    /// <inheritdoc/>
    public override void RefreshLocalization()
    {
        base.RefreshLocalization();
        UserName = string.IsNullOrWhiteSpace(message.UserName)
            ? Localization.Get("Room.RedPocket.UnknownSender")
            : message.UserName;
        UserUid = message.IsMystery == true || message.Uid <= 0
            ? ""
            : message.Uid.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var value = FormatValue();
        Content = Localization.Format(message.IsDrawn ? "Room.RedPocket.DrawnContent" : "Room.RedPocket.WaitingContent",
            value.Length > 0 ? $" · {value}" : "");
    }

    private string FormatValue()
    {
        if (message.Type is 3 or 5 && (message.TotalBattery ?? message.TotalPrice) is { } battery)
        {
            return Localization.Format("Room.RedPocket.BatteryValue", battery / 100m);
        }

        if (message.Type == 4)
        {
            return Localization.Get("Room.RedPocket.IntimacyValue");
        }

        if (message.TotalPrice is { } totalPrice)
        {
            return Localization.Format("Room.RedPocket.MoneyValue", totalPrice / 1000m);
        }

        return message.Price is { } price
            ? Localization.Format("Room.RedPocket.BatteryValue", price)
            : "";
    }
}

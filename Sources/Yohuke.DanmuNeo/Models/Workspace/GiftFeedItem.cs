using System.Globalization;
using Avalonia.Media;
using Yohuke.DanmuNeo.I18n;

namespace Yohuke.DanmuNeo.Models.Workspace;

/// <summary>当前直播间的礼物记录，可原位更新连击数量和累计价值。</summary>
public sealed class GiftFeedItem : DanmuFeedItem
{
    private const decimal UNITS_PER_YUAN = 1000m;
    private string giftName;
    private int count;
    private decimal unitPrice;
    private string giftText = "";

    internal GiftFeedItem(string giftName, int count, decimal unitPrice, string comboIdentity)
    {
        this.giftName = giftName;
        this.count = count;
        this.unitPrice = unitPrice;
        ComboIdentity = comboIdentity;
        RefreshLocalization();
    }

    internal string ComboIdentity { get; }

    private decimal TotalValue => unitPrice / UNITS_PER_YUAN * count;

    /// <summary>礼物名称及累计数量。</summary>
    public string GiftText => giftText;

    /// <summary>以元为单位的累计价值。</summary>
    public string ValueText => string.Create(CultureInfo.InvariantCulture, $"￥{TotalValue:0.###}");

    /// <summary>沿用 DanmuPanel 按人民币累计价值划分的礼物边框色。</summary>
    public Color BorderColor => Color.FromUInt32(TotalValue switch
    {
        >= 2000m => 0xFFAB1A32,
        >= 1000m => 0xFFE54D4D,
        >= 500m => 0xFFE09443,
        >= 100m => 0xFFE2B52B,
        >= 50m => 0xFF427D9E,
        >= 30m => 0xFF3171D2,
        >= 10m => 0xFF39B5C8,
        >= 1m => 0xFF65B87A,
        _ => 0xFF8B9BA8
    });

    internal void UpdateCombo(string name, int quantity, int reportedCount, decimal reportedUnitPrice)
    {
        var previousValue = TotalValue;
        giftName = name;
        count = reportedCount > 0
            ? Math.Max(count, reportedCount)
            : (int)Math.Min(int.MaxValue, (long)count + quantity);
        if (reportedUnitPrice > 0)
        {
            unitPrice = reportedUnitPrice;
        }

        RefreshLocalization();
        if (TotalValue != previousValue)
        {
            OnPropertyChanged(nameof(ValueText));
            OnPropertyChanged(nameof(BorderColor));
        }
    }

    /// <inheritdoc/>
    public override void RefreshLocalization()
    {
        base.RefreshLocalization();
        SetProperty(ref giftText, Localization.Format("Room.GiftContent", giftName, count), nameof(GiftText));
        Content = TotalValue > 0 ? $"{GiftText} · {ValueText}" : GiftText;
    }
}

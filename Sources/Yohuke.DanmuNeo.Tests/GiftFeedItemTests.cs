using Avalonia.Media;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证礼物价格显示和 DanmuPanel 的价值分档。</summary>
public class GiftFeedItemTests
{
    /// <summary>按未舍入的人民币价值分档，保留瓜子价格换算后的精度。</summary>
    [Theory]
    [InlineData(0, "￥0", "#8B9BA8")]
    [InlineData(999, "￥0.999", "#8B9BA8")]
    [InlineData(1000, "￥1", "#65B87A")]
    [InlineData(10000, "￥10", "#39B5C8")]
    [InlineData(30000, "￥30", "#3171D2")]
    [InlineData(50000, "￥50", "#427D9E")]
    [InlineData(100000, "￥100", "#E2B52B")]
    [InlineData(500000, "￥500", "#E09443")]
    [InlineData(1000000, "￥1000", "#E54D4D")]
    [InlineData(2000000, "￥2000", "#AB1A32")]
    public void UsesDanmuPanelValueTiers(int unitPrice, string expectedValue, string expectedColor)
    {
        var item = new GiftFeedItem("礼物", 1, unitPrice, "");

        Assert.Equal(expectedValue, item.ValueText);
        Assert.Equal(Color.Parse(expectedColor), item.BorderColor);
    }
}

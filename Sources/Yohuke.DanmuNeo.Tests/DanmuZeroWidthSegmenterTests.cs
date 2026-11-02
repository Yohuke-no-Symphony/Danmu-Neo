using System.Globalization;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证零宽空格重分段的文字元素、长度预算与原文范围。</summary>
public class DanmuZeroWidthSegmenterTests
{
    /// <summary>只在完整正文文字元素之间插入，保留原有零宽空格和完整标识符。</summary>
    [Theory]
    [InlineData("你好", "你\u200B好")]
    [InlineData("AB 12，CD", "A\u200BB\u200B \u200B1\u200B2\u200B，\u200BC\u200BD")]
    [InlineData("😀👩‍💻e\u0301", "😀\u200B👩‍💻\u200Be\u0301")]
    [InlineData("甲\u200B乙丙", "甲\u200B乙\u200B丙")]
    [InlineData("\u200B甲乙\u200B", "\u200B甲\u200B乙\u200B")]
    public void InsertsOnlyBetweenCompleteBodyElements(string body, string expected)
    {
        var content = new DanmuMessageContent { Body = body, Prefix = "【♪", Suffix = "】" };

        var segment = DanmuMessageSegmenter.NextWithZeroWidthSpaces(content, 0, body.Length, 100);

        Assert.True(DanmuMessageSegmenter.CanInsertZeroWidthSpaces(body, 0, body.Length));
        Assert.Equal("【♪" + expected + "】", segment.Message);
        Assert.Equal(0, segment.BodyStart);
        Assert.Equal(body.Length, segment.BodyLength);
        Assert.Equal(body, content.Body);
    }

    /// <summary>没有可插入间隔时不应安排相同文本的重发。</summary>
    [Theory]
    [InlineData("", false)]
    [InlineData("甲", false)]
    [InlineData("😀", false)]
    [InlineData("e\u0301", false)]
    [InlineData("👩‍💻", false)]
    [InlineData("甲\u200B乙", false)]
    [InlineData("\u200B甲", false)]
    [InlineData("甲\u200B\u200B乙", false)]
    [InlineData("甲乙", true)]
    [InlineData("甲\u200B乙丙", true)]
    public void DetectsWhetherRetryCanChangeBody(string body, bool expected)
    {
        Assert.Equal(expected, DanmuMessageSegmenter.CanInsertZeroWidthSpaces(body, 0, body.Length));
    }

    /// <summary>可插入性只检查被屏蔽的原文范围，不受相邻未处理正文影响。</summary>
    [Fact]
    public void ChangeDetectionRespectsRejectedBodyRange()
    {
        const string BODY = "甲乙\u200B丙丁";

        Assert.False(DanmuMessageSegmenter.CanInsertZeroWidthSpaces(BODY, 1, 4));
        Assert.True(DanmuMessageSegmenter.CanInsertZeroWidthSpaces(BODY, 1, 5));
    }

    /// <summary>续接标记和标识符不参与插入，分段不会越过被屏蔽范围。</summary>
    [Fact]
    public void RetryKeepsOriginalOffsetsAndStopsAtRejectedRangeEnd()
    {
        var content = new DanmuMessageContent { Body = "前缀甲乙丙丁尾巴", Prefix = "【", Suffix = "】" };

        var first = DanmuMessageSegmenter.NextWithZeroWidthSpaces(content, 2, 6, 6);
        var second = DanmuMessageSegmenter.NextWithZeroWidthSpaces(content, first.BodyStart + first.BodyLength, 6, 6);

        Assert.Equal("【…甲\u200B乙】", first.Message);
        Assert.Equal(2, first.BodyStart);
        Assert.Equal(2, first.BodyLength);
        Assert.Equal("【…丙\u200B丁】", second.Message);
        Assert.Equal(4, second.BodyStart);
        Assert.Equal(2, second.BodyLength);
        Assert.Equal(6, second.BodyStart + second.BodyLength);
    }

    /// <summary>插入字符消耗实际长度预算，拆分后原文范围连续且完整。</summary>
    [Theory]
    [InlineData("甲乙丙丁戊己", 8)]
    [InlineData("😀👩‍💻e\u0301甲乙", 12)]
    [InlineData("👨‍👩‍👧‍👦甲乙丙丁", 16)]
    public void ExpandedSegmentsFitBudgetWithoutLosingOrSplittingTextElements(string body, int limit)
    {
        var content = new DanmuMessageContent { Body = body, Prefix = "【♪", Suffix = "】" };
        var boundaries = StringInfo.ParseCombiningCharacters(body).Append(body.Length).ToHashSet();
        var restoredBodies = new List<string>();
        var start = 0;

        while (start < body.Length)
        {
            var segment = DanmuMessageSegmenter.NextWithZeroWidthSpaces(content, start, body.Length, limit);
            var wireBody = segment.Message[content.Prefix.Length..^content.Suffix.Length];
            if (wireBody.StartsWith('…'))
            {
                wireBody = wireBody[1..];
            }

            Assert.Equal(start, segment.BodyStart);
            Assert.True(segment.BodyLength > 0);
            Assert.Contains(segment.BodyStart, boundaries);
            Assert.Contains(segment.BodyStart + segment.BodyLength, boundaries);
            Assert.InRange(segment.Message.Length, 1, limit);
            Assert.StartsWith(content.Prefix, segment.Message);
            Assert.EndsWith(content.Suffix, segment.Message);
            Assert.False(wireBody.StartsWith('\u200B'));
            Assert.False(wireBody.EndsWith('\u200B'));
            Assert.DoesNotContain("\u200B\u200B", wireBody, StringComparison.Ordinal);
            Assert.Equal(body.Substring(start, segment.BodyLength), wireBody.Replace("\u200B", ""));
            restoredBodies.Add(wireBody.Replace("\u200B", ""));
            start += segment.BodyLength;
        }

        Assert.Equal(body.Length, start);
        Assert.Equal(body, string.Concat(restoredBodies));
    }

    /// <summary>恰好达到长度上限时保留全部正文，偏移不包含新增字符。</summary>
    [Fact]
    public void ExactExpandedBudgetPreservesOriginalBodyLength()
    {
        var content = new DanmuMessageContent { Body = "甲乙", Prefix = "【", Suffix = "】" };

        var segment = DanmuMessageSegmenter.NextWithZeroWidthSpaces(content, 0, 2, 5);

        Assert.Equal("【甲\u200B乙】", segment.Message);
        Assert.Equal(5, segment.Message.Length);
        Assert.Equal(2, segment.BodyLength);
    }

    /// <summary>预算仅够一个文字元素时拆成单字，不制造悬空的新增零宽空格。</summary>
    [Fact]
    public void SingleElementSegmentsDoNotContainInsertedSeparators()
    {
        var content = new DanmuMessageContent { Body = "甲乙", Prefix = "【", Suffix = "】" };

        var first = DanmuMessageSegmenter.NextWithZeroWidthSpaces(content, 0, 2, 4);
        var second = DanmuMessageSegmenter.NextWithZeroWidthSpaces(content, 1, 2, 4);

        Assert.Equal("【甲】", first.Message);
        Assert.Equal("【…乙】", second.Message);
        Assert.Equal(1, first.BodyLength);
        Assert.Equal(1, second.BodyLength);
    }

    /// <summary>插入后的长度控制标点断句，续接段仍使用完整原文边界。</summary>
    [Fact]
    public void PunctuationCutUsesExpandedLengthBudget()
    {
        var content = new DanmuMessageContent { Body = "甲乙，丙丁戊己", Prefix = "【", Suffix = "】" };

        var first = DanmuMessageSegmenter.NextWithZeroWidthSpaces(content, 0, content.Body.Length, 10);
        var second = DanmuMessageSegmenter.NextWithZeroWidthSpaces(content, first.BodyLength, content.Body.Length, 10);

        Assert.Equal("【甲\u200B乙\u200B，】", first.Message);
        Assert.Equal(3, first.BodyLength);
        Assert.Equal("【…丙\u200B丁\u200B戊\u200B己】", second.Message);
        Assert.Equal(4, second.BodyLength);
    }

    /// <summary>经验长度约束插入后的实际长度，同时允许至少一个完整文字元素。</summary>
    [Fact]
    public void PreferredLengthBudgetsExpandedTextWithoutBreakingMinimumElement()
    {
        var content = new DanmuMessageContent { Body = "甲乙丙丁", Prefix = "【", Suffix = "】" };
        var emojiContent = content with { Body = "😀甲" };

        var segment = DanmuMessageSegmenter.NextWithZeroWidthSpaces(content, 0, 4, 20, 5);
        var emojiSegment = DanmuMessageSegmenter.NextWithZeroWidthSpaces(emojiContent, 0, emojiContent.Body.Length, 20, 2);

        Assert.Equal("【甲\u200B乙】", segment.Message);
        Assert.Equal(2, segment.BodyLength);
        Assert.Equal("【😀】", emojiSegment.Message);
        Assert.Equal(2, emojiSegment.BodyLength);
    }

    /// <summary>硬上限无法容纳一个完整文字元素时明确失败。</summary>
    [Theory]
    [InlineData("甲", 2)]
    [InlineData("👩‍💻", 6)]
    public void InsufficientBudgetRejectsWholeTextElement(string body, int limit)
    {
        var content = new DanmuMessageContent { Body = body, Prefix = "【", Suffix = "】" };

        Assert.Throws<InvalidOperationException>(() =>
            DanmuMessageSegmenter.NextWithZeroWidthSpaces(content, 0, body.Length, limit));
    }
}

using System.Globalization;
using System.Text;
using Yohuke.DanmuNeo.I18n;

namespace Yohuke.DanmuNeo.Services;

/// <summary>按保守 UTF-16 预算分段，并保护完整的 Unicode 文字元素。</summary>
public static class DanmuMessageSegmenter
{
    private const string CONTINUATION_MARK = "…";
    private const string CUT_PUNCTUATION = " 　/，。：！？,.:!?)）】";
    private const char ZERO_WIDTH_SPACE = '\u200B';

    /// <summary>从尚未成功的正文起点创建下一段。</summary>
    public static DanmuSendSegment Next(DanmuMessageContent content, int start, int maxLength, int? preferredLength = null)
    {
        return CreateSegment(content, start, content.Body.Length, maxLength, preferredLength, false);
    }

    internal static DanmuSendSegment NextWithZeroWidthSpaces(DanmuMessageContent content, int start, int end,
        int maxLength, int? preferredLength = null)
    {
        return CreateSegment(content, start, end, maxLength, preferredLength, true);
    }

    internal static bool CanInsertZeroWidthSpaces(string body, int start, int end)
    {
        var remaining = body[start..end];
        return StringInfo.ParseCombiningCharacters(remaining).Skip(1)
            .Any(boundary => ShouldInsertZeroWidthSpace(remaining, boundary));
    }

    internal static int? GetReducedLength(DanmuMessageContent content, DanmuSendSegment rejected, int refreshedLimit)
    {
        var minimumLength = content.Prefix.Length + content.Suffix.Length +
                            StringInfo.GetNextTextElementLength(content.Body.AsSpan(rejected.BodyStart));
        var reducedLength = refreshedLimit > 0 && refreshedLimit < rejected.Message.Length
            ? refreshedLimit
            : Math.Max(minimumLength, rejected.Message.Length - Math.Max(1, rejected.BodyLength / 2));
        return reducedLength >= minimumLength && reducedLength < rejected.Message.Length ? reducedLength : null;
    }

    private static DanmuSendSegment CreateSegment(DanmuMessageContent content, int start, int rangeEnd,
        int maxLength, int? preferredLength, bool insertZeroWidthSpaces)
    {
        var remaining = content.Body[start..rangeEnd];
        var firstElementLength = StringInfo.GetNextTextElementLength(remaining);
        var minimumLength = content.Prefix.Length + content.Suffix.Length + firstElementLength;
        maxLength = Math.Min(maxLength, Math.Max(preferredLength ?? maxLength, minimumLength));
        var bodyBudget = maxLength - content.Prefix.Length - content.Suffix.Length;
        var continuation = start > 0 && bodyBudget > firstElementLength
            ? CONTINUATION_MARK : "";
        var budget = bodyBudget - continuation.Length;
        var boundaries = StringInfo.ParseCombiningCharacters(remaining);
        var end = 0;
        var wireLength = 0;
        var punctuationEnd = 0;
        for (var index = 0; index < boundaries.Length; index++)
        {
            var boundary = index + 1 < boundaries.Length ? boundaries[index + 1] : remaining.Length;
            var separatorLength = insertZeroWidthSpaces && ShouldInsertZeroWidthSpace(remaining, end) ? 1 : 0;
            var nextWireLength = wireLength + boundary - end + separatorLength;
            if (nextWireLength > budget)
            {
                break;
            }
            end = boundary;
            wireLength = nextWireLength;
            if (wireLength > budget / 2 && CUT_PUNCTUATION.Contains(remaining[boundary - 1]))
            {
                punctuationEnd = boundary;
            }
        }
        if (end == 0)
        {
            throw LocalizedExceptionText.Create("Services.Send.NoBodyBudget", message => new InvalidOperationException(message));
        }
        if (end < remaining.Length && punctuationEnd > 0)
        {
            end = punctuationEnd;
        }
        return new()
        {
            BodyStart = start,
            BodyLength = end,
            Message = content.Prefix + continuation +
                      (insertZeroWidthSpaces ? InsertZeroWidthSpaces(remaining, end, boundaries) : remaining[..end]) +
                      content.Suffix
        };
    }

    private static bool ShouldInsertZeroWidthSpace(string body, int boundary)
    {
        return boundary > 0 && body[boundary - 1] != ZERO_WIDTH_SPACE && body[boundary] != ZERO_WIDTH_SPACE;
    }

    private static string InsertZeroWidthSpaces(string body, int length, int[] boundaries)
    {
        var result = new StringBuilder();
        var start = 0;
        foreach (var end in boundaries.Skip(1).TakeWhile(boundary => boundary < length).Append(length))
        {
            if (ShouldInsertZeroWidthSpace(body, start))
            {
                result.Append(ZERO_WIDTH_SPACE);
            }
            result.Append(body, start, end - start);
            start = end;
        }
        return result.ToString();
    }
}

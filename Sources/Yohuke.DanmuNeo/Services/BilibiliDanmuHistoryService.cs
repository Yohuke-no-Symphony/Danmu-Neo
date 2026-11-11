using System.Globalization;
using Newtonsoft.Json.Linq;
using Yohuke.DanmuNeo.Apis;
using Yohuke.DanmuNeo.Apis.Models.Bilibili;
using Yohuke.DanmuNeo.I18n;

namespace Yohuke.DanmuNeo.Services;

/// <summary>读取近期普通弹幕并转换为统一消息模型。</summary>
public sealed class BilibiliDanmuHistoryService(HttpClient? httpClient = null) : IBilibiliDanmuHistoryService
{
    /// <inheritdoc/>
    public async Task<IReadOnlyList<BilibiliDanmuMessage>> GetRecentAsync(string roomId, string cookie,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        if (!long.TryParse(roomId, out var parsedRoomId) || parsedRoomId <= 0)
        {
            throw new ArgumentException(Localization.Get("Room.InvalidRoomId"), nameof(roomId));
        }

        using var api = new BilibiliApi(cookie, timeout, httpClient);
        var response = await api.GetDanmuHistoryAsync(parsedRoomId, timeout, cancellationToken);
        if (response.Code != 0 || response.Data is null)
        {
            var detail = AppLogSanitizer.Sanitize(response.Message ?? response.Msg ?? "");
            var error = $"{Localization.Format("Room.History.FailedCode", response.Code)} {detail}".Trim();
            throw response.Code == -101
                ? new BilibiliAuthenticationException(error)
                : new InvalidOperationException(error);
        }

        return (response.Data.Room ?? []).Select(item => ParseItem(roomId, item)).ToList();
    }

    internal static BilibiliDanmuMessage ParseItem(string roomId, BilibiliDanmuHistoryItem item)
    {
        var medal = item.User?["medal"] as JObject;
        var reply = item.Reply as JObject;
        var medalGuardLevel = ReadNonNegativeInt(medal?["guard_level"]);
        var timestamp = item.Timestamp;
        if (timestamp > 253402300799)
        {
            timestamp /= 1000;
        }

        if (timestamp <= 0 && DateTime.TryParse(item.Timeline, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var time))
        {
            timestamp = new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Unspecified), TimeSpan.FromHours(8))
                .ToUnixTimeSeconds();
        }

        var result = new BilibiliDanmuMessage
        {
            RoomId = roomId,
            MessageId = item.MessageId ?? "",
            Timestamp = timestamp is > 0 and <= 253402300799 ? timestamp : 0,
            Uid = item.Uid,
            UserName = item.Nickname ?? "",
            Content = item.Text ?? "",
            ReplyUid = Math.Max(0, ReadInteger(reply?["reply_mid"])),
            ReplyUserName = ReadScalarText(reply?["reply_uname"]),
            ReplyUserNameColor = ReadScalarText(reply?["reply_uname_color"]),
            ReplyIsMystery = ReadScalarText(reply?["reply_is_mystery"]) is "1" or "True" or "true",
            IsRoomAdmin = item.IsAdmin?.ToString() is "1" or "True" or "true",
            MedalName = ReadScalarText(medal?["name"]),
            MedalLevel = ReadNonNegativeInt(medal?["level"]),
            MedalGuardLevel = medalGuardLevel is >= 1 and <= 3 ? medalGuardLevel : 0,
            IsEmoticon = ReadScalarText(item.Emoticon?["text"]) is { Length: > 0 }
        };
        if (item.Emots is not null)
        {
            foreach (var mapping in item.Emots.Properties())
            {
                var url = ReadScalarText((mapping.Value as JObject)?["url"]);
                if (!string.IsNullOrWhiteSpace(url))
                {
                    result.Emoticons.Add(new() { Text = mapping.Name, Url = url });
                }
            }
        }

        if (result.IsEmoticon && ReadScalarText(item.Emoticon?["url"]) is { Length: > 0 } emoticonUrl)
        {
            result.Emoticons.Add(new() { Text = result.Content, Url = emoticonUrl });
        }

        return result;
    }

    private static string ReadScalarText(JToken? token)
    {
        return token is JValue value ? Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? "" : "";
    }

    private static long ReadInteger(JToken? token)
    {
        return long.TryParse(ReadScalarText(token), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private static int ReadNonNegativeInt(JToken? token)
    {
        var value = ReadInteger(token);
        return value is >= 0 and <= int.MaxValue ? (int)value : 0;
    }
}

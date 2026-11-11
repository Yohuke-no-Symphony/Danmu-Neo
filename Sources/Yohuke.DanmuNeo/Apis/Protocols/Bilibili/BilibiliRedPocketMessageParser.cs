using System.Globalization;
using Newtonsoft.Json.Linq;
using Yohuke.DanmuNeo.Apis.Models.Bilibili;

namespace Yohuke.DanmuNeo.Apis.Protocols.Bilibili;

internal static class BilibiliRedPocketMessageParser
{
    private const long MAX_UNIX_TIMESTAMP_SECONDS = 253402300799;

    internal static bool SupportsCommand(string? command)
    {
        return command?.Split(':', 2)[0] is
            "POPULARITY_RED_POCKET_NEW" or "POPULARITY_RED_POCKET_V2_NEW" or
            "POPULARITY_RED_POCKET_START" or "POPULARITY_RED_POCKET_V2_START" or
            "POPULARITY_RED_POCKET_WINNER_LIST" or "POPULARITY_RED_POCKET_V2_WINNER_LIST";
    }

    internal static BilibiliRedPocketMessage? Parse(string roomId, JObject json)
    {
        var command = ReadText(json["cmd"]).Split(':', 2)[0];
        if (!SupportsCommand(command))
        {
            return null;
        }

        if (json["data"] is not JObject data)
        {
            throw new InvalidDataException("Red pocket data is missing or invalid.");
        }

        var lotInfo = data["lot_info"] as JObject ?? data["lotInfo"] as JObject;
        var lotId = ReadPositiveLong(data["lot_id"], data["lotId"]);
        var detailLotId = ReadPositiveLong(lotInfo?["lot_id"], lotInfo?["lotId"]);
        if (lotId > 0 && detailLotId > 0 && lotId != detailLotId)
        {
            throw new InvalidDataException("Red pocket detail identity does not match the notification.");
        }

        lotId = lotId > 0 ? lotId : detailLotId;
        if (lotId <= 0)
        {
            throw new InvalidDataException("Red pocket identity is missing or invalid.");
        }

        var sender = data["sender_uinfo"] as JObject ?? data["sender_info"] as JObject;
        var senderBase = sender?["base"] as JObject;
        var detailSender = lotInfo?["sender_uinfo"] as JObject ?? lotInfo?["sender_info"] as JObject;
        var detailSenderBase = detailSender?["base"] as JObject;
        return new()
        {
            RoomId = roomId,
            LotId = lotId,
            Uid = ReadPositiveLong(sender?["uid"], data["sender_uid"], data["uid"],
                detailSender?["uid"], lotInfo?["sender_uid"], lotInfo?["uid"]),
            UserName = ReadText(senderBase?["name"], data["sender_name"], data["uname"],
                detailSenderBase?["name"], lotInfo?["sender_name"], lotInfo?["uname"]),
            Type = ReadNullableInt(data["rp_type"], data["rpType"], lotInfo?["rp_type"], lotInfo?["rpType"]),
            IsMystery = ReadNullableBoolean(senderBase?["is_mystery"], data["is_mystery"],
                detailSenderBase?["is_mystery"], lotInfo?["is_mystery"]),
            TotalPrice = ReadNullableDecimal(data["total_price"], lotInfo?["total_price"]),
            TotalBattery = ReadNullableDecimal(data["total_battery"], lotInfo?["total_battery"]),
            Price = ReadNullableDecimal(data["price"], lotInfo?["price"]),
            IsDrawn = command.EndsWith("_WINNER_LIST", StringComparison.Ordinal),
            Timestamp = ReadTimestamp(data["current_time"], data["timestamp"], lotInfo?["current_time"],
                data["start_time"], lotInfo?["start_time"], json["send_time"])
        };
    }

    private static string ReadText(params JToken?[] tokens)
    {
        foreach (var token in tokens)
        {
            if (token is JValue value)
            {
                var text = Convert.ToString(value.Value, CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return "";
    }

    private static long ReadPositiveLong(params JToken?[] tokens)
    {
        foreach (var token in tokens)
        {
            if (long.TryParse(ReadText(token), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0)
            {
                return value;
            }
        }

        return 0;
    }

    private static int? ReadNullableInt(params JToken?[] tokens)
    {
        foreach (var token in tokens)
        {
            if (int.TryParse(ReadText(token), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= 0)
            {
                return value;
            }
        }

        return null;
    }

    private static decimal? ReadNullableDecimal(params JToken?[] tokens)
    {
        foreach (var token in tokens)
        {
            if (decimal.TryParse(ReadText(token), NumberStyles.Number | NumberStyles.AllowExponent,
                    CultureInfo.InvariantCulture, out var value) && value >= 0)
            {
                return value;
            }
        }

        return null;
    }

    private static bool? ReadNullableBoolean(params JToken?[] tokens)
    {
        foreach (var token in tokens)
        {
            if (bool.TryParse(ReadText(token), out var value))
            {
                return value;
            }

            var integer = ReadNullableInt(token);
            if (integer is 0 or 1)
            {
                return integer == 1;
            }
        }

        return null;
    }

    private static long ReadTimestamp(params JToken?[] tokens)
    {
        foreach (var token in tokens)
        {
            var value = ReadPositiveLong(token);
            value = value > MAX_UNIX_TIMESTAMP_SECONDS ? value / 1000 : value;
            if (value is > 0 and <= MAX_UNIX_TIMESTAMP_SECONDS)
            {
                return value;
            }
        }

        return 0;
    }
}

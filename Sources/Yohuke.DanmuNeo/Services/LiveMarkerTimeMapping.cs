namespace Yohuke.DanmuNeo.Services;

internal static class LiveMarkerTimeMapping
{
    internal static bool TryGetRealTime(DateTimeOffset startTime, TimeSpan liveTime, out DateTimeOffset realTime)
    {
        realTime = default;
        if (liveTime < TimeSpan.Zero)
        {
            return false;
        }

        try
        {
            realTime = startTime + liveTime;
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}

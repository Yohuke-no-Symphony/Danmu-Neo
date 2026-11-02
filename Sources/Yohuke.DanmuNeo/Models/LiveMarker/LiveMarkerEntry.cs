namespace Yohuke.DanmuNeo.Models.LiveMarker;

/// <summary>保存直播内时间及对应实际时间的不可变直播记录。</summary>
public sealed record LiveMarkerEntry
{
    public DateTimeOffset RealDateTime { get; init; }
    public TimeSpan LiveTime { get; init; }
    public string Message { get; init; } = "";
    public LiveMarkerColor MarkerColor { get; init; }
}

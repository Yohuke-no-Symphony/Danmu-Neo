namespace Yohuke.DanmuNeo.Models.State;

/// <summary>保存弹幕用户名区在两种显示密度下所占的宽度比例。</summary>
public sealed class DanmuColumnWidths
{
    internal const double DEFAULT_IDENTITY_RATIO = 0.4;
    internal const double MIN_IDENTITY_RATIO = 0.05;
    internal const double MAX_IDENTITY_RATIO = 0.95;

    /// <summary>普通显示下的用户名区比例。</summary>
    public double RegularIdentityRatio { get; set; } = DEFAULT_IDENTITY_RATIO;

    /// <summary>紧凑显示下的用户名区比例。</summary>
    public double CompactIdentityRatio { get; set; } = DEFAULT_IDENTITY_RATIO;

    internal double GetIdentityRatio(bool compact)
    {
        return compact ? CompactIdentityRatio : RegularIdentityRatio;
    }

    internal bool SetIdentityRatio(bool compact, double ratio)
    {
        ratio = ClampIdentityRatio(ratio);
        if (GetIdentityRatio(compact).Equals(ratio))
        {
            return false;
        }

        if (compact)
        {
            CompactIdentityRatio = ratio;
        }
        else
        {
            RegularIdentityRatio = ratio;
        }

        return true;
    }

    internal void Normalize()
    {
        RegularIdentityRatio = ClampIdentityRatio(RegularIdentityRatio);
        CompactIdentityRatio = ClampIdentityRatio(CompactIdentityRatio);
    }

    internal static double ClampIdentityRatio(double ratio)
    {
        return double.IsFinite(ratio) ? Math.Clamp(ratio, MIN_IDENTITY_RATIO, MAX_IDENTITY_RATIO) : DEFAULT_IDENTITY_RATIO;
    }
}

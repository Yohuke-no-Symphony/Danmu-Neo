namespace Yohuke.DanmuNeo.Models.Workspace;

/// <summary>弹幕正文中独立显示的回复对象。</summary>
public sealed record DanmuReply(long UserUid, string UserName, string UserNameColor);

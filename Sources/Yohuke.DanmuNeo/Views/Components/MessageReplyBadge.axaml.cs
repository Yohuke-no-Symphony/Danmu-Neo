using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Views.Components;

internal sealed partial class MessageReplyBadge : UserControl
{
    private readonly Border frame;
    private readonly Grid content;
    private readonly UserAvatarView avatar;
    private readonly TextBlock name;

    internal MessageReplyBadge(DanmuReply reply, IUserAvatarService? avatars, bool showAvatar)
    {
        AvaloniaXamlLoader.Load(this);
        frame = this.FindControl<Border>("ReplyFrame")!;
        content = this.FindControl<Grid>("ReplyContent")!;
        avatar = this.FindControl<UserAvatarView>("ReplyAvatar")!;
        name = this.FindControl<TextBlock>("ReplyName")!;
        avatar.AvatarService = avatars;
        avatar.UserUid = reply.UserUid;
        avatar.ShowAvatar = showAvatar;
        name.Text = $"@{reply.UserName}";
        AutomationProperties.SetName(this, name.Text);
    }

    internal void UpdateTypography(EmoticonTextBlock owner)
    {
        name.FontFamily = owner.FontFamily;
        name.FontSize = owner.FontSize;
        name.FontWeight = owner.FontWeight;
        name.FontStyle = owner.FontStyle;
        name.FontStretch = owner.FontStretch;
        name.Foreground = owner.Foreground;
        avatar.Width = owner.FontSize * 1.1;
        avatar.Height = avatar.Width;
        content.ColumnSpacing = avatar.ShowAvatar ? owner.FontSize * 0.2 : 0;
        frame.Padding = new(owner.FontSize * 0.25, owner.FontSize * 0.06,
            owner.FontSize * 0.4, owner.FontSize * 0.06);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var desiredSize = base.MeasureOverride(availableSize);
        var textTop = Math.Max(0, avatar.DesiredSize.Height - name.DesiredSize.Height) / 2;
        TextBlock.SetBaselineOffset(this,
            frame.BorderThickness.Top + frame.Padding.Top + textTop + name.TextLayout.Baseline);
        return desiredSize;
    }
}

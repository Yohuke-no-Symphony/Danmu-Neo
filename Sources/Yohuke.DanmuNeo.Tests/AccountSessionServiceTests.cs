using Yohuke.DanmuCore.Models.Accounts;
using Yohuke.DanmuCore.Services;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 确认凭据版本、删除和验证错误不会使失效账号继续发送。
/// </summary>
public class AccountSessionServiceTests
{
    /// <summary>
    /// 验证失效时统一发送服务不会查询配置或发送网络请求。
    /// </summary>
    [Fact]
    public async Task KnownExpiredAccountIsBlockedBeforeSending()
    {
        using var sessions = new BilibiliAccountSessionService(new ControlledAccountValidationService());
        using var accountBinding = new BilibiliAccountSessionBinding(sessions);
        var account = new BilibiliAccount { Cookie = "SESSDATA=offline;bili_jct=csrf" };
        accountBinding.Register(account);
        sessions.ReportAuthenticationFailure(account.Id, account.Cookie);
        using var handler = new ModerationHttpHandler();
        using var client = new HttpClient(handler);
        var config = new FakeBilibiliDanmuConfigService();
        var sender = new DanmuSendService(client, config, accountSessions: sessions);

        var result = await sender.SendAsync("100", "同传", account, new());

        Assert.False(result.IsAccepted);
        Assert.Empty(handler.Requests);
        Assert.Empty(config.GetRequests);
    }
}

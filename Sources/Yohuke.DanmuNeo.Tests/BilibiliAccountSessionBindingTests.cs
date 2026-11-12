using Yohuke.DanmuCore.Services;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证产品账号观察与共享凭据取消边界的衔接。</summary>
public sealed class BilibiliAccountSessionBindingTests
{
    /// <summary>编辑 Cookie 立即撤销旧凭据，并注册新的发送身份。</summary>
    [Fact]
    public void CookieEditCancelsPreviousCredentials()
    {
        using var sessions = new BilibiliAccountSessionService(new ControlledAccountValidationService());
        using var binding = new BilibiliAccountSessionBinding(sessions);
        var account = new BilibiliAccount { Cookie = "SESSDATA=first;bili_jct=csrf" };
        binding.Register(account);
        var previousCookie = account.Cookie;
        var previousToken = sessions.GetCredentialCancellationToken(account.Id, previousCookie);

        account.Cookie = "SESSDATA=second;bili_jct=csrf";

        Assert.True(previousToken.IsCancellationRequested);
        Assert.False(sessions.CanSend(account.Id, previousCookie));
        Assert.True(sessions.CanSend(account.Id, account.Cookie));
    }

    /// <summary>相同 ID 的账号被替换后，旧对象不再影响当前凭据。</summary>
    [Fact]
    public void ReplacingAnAccountUnsubscribesThePreviousObject()
    {
        using var sessions = new BilibiliAccountSessionService(new ControlledAccountValidationService());
        using var binding = new BilibiliAccountSessionBinding(sessions);
        var previous = new BilibiliAccount { Id = "account", Cookie = "SESSDATA=first;bili_jct=csrf" };
        var current = new BilibiliAccount { Id = "account", Cookie = "SESSDATA=second;bili_jct=csrf" };
        binding.Register(previous);
        binding.Register(current);
        var token = sessions.GetCredentialCancellationToken(current.Id, current.Cookie);

        previous.Cookie = "SESSDATA=discarded;bili_jct=csrf";

        Assert.False(token.IsCancellationRequested);
        Assert.True(sessions.CanSend(current.Id, current.Cookie));
    }

    /// <summary>移除绑定会撤销凭据，之后的模型编辑不能重新注册账号。</summary>
    [Fact]
    public void RemovingBindingCancelsCredentialsAndUnsubscribesTheAccount()
    {
        using var sessions = new BilibiliAccountSessionService(new ControlledAccountValidationService());
        using var binding = new BilibiliAccountSessionBinding(sessions);
        var account = new BilibiliAccount { Cookie = "SESSDATA=first;bili_jct=csrf" };
        binding.Register(account);
        var token = sessions.GetCredentialCancellationToken(account.Id, account.Cookie);

        binding.Remove(account.Id);
        account.Cookie = "SESSDATA=second;bili_jct=csrf";

        Assert.True(token.IsCancellationRequested);
        Assert.False(sessions.CanSend(account.Id, account.Cookie));
    }
}

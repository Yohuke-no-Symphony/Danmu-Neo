using System.ComponentModel;
using Yohuke.DanmuCore.Services;
using Yohuke.DanmuNeo.Models.State;

namespace Yohuke.DanmuNeo.Services;

/// <summary>将产品账号的凭据变更绑定到共享账号会话。</summary>
public sealed class BilibiliAccountSessionBinding : IDisposable
{
    private readonly IBilibiliAccountSessionService sessions;
    private readonly Dictionary<string, BilibiliAccount> accounts = new(StringComparer.Ordinal);
    private bool disposed;

    /// <summary>创建账号绑定，不持有共享会话的释放权。</summary>
    public BilibiliAccountSessionBinding(IBilibiliAccountSessionService sessions)
    {
        this.sessions = sessions;
    }

    /// <summary>注册账号并观察后续凭据变化。</summary>
    public void Register(BilibiliAccount account)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (accounts.TryGetValue(account.Id, out var previous))
        {
            if (ReferenceEquals(previous, account))
            {
                return;
            }
            previous.PropertyChanged -= OnAccountChanged;
        }
        accounts[account.Id] = account;
        sessions.Register(account.Id, account.Cookie);
        sessions.UpdateCredentials(account.Id, account.Cookie);
        account.PropertyChanged += OnAccountChanged;
    }

    /// <summary>移除账号绑定并撤销其共享会话。</summary>
    public void Remove(string accountId)
    {
        if (accounts.Remove(accountId, out var account))
        {
            account.PropertyChanged -= OnAccountChanged;
            sessions.Remove(accountId);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        foreach (var id in accounts.Keys.ToArray())
        {
            Remove(id);
        }
    }

    private void OnAccountChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BilibiliAccount.Cookie) && sender is BilibiliAccount account)
        {
            sessions.UpdateCredentials(account.Id, account.Cookie);
        }
    }
}

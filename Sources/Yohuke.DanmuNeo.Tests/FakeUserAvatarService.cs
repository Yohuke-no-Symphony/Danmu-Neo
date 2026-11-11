using System.Threading;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>为现有界面回归测试提供离线的空头像响应。</summary>
public sealed class FakeUserAvatarService : IUserAvatarService
{
    /// <inheritdoc/>
    public event Action<long>? AvatarChanged
    {
        add
        {
        }
        remove
        {
        }
    }

    /// <inheritdoc/>
    public Task<string> GetAvatarAsync(long userUid, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult("");
    }

    /// <inheritdoc/>
    public Task InvalidateAvatarAsync(long userUid, string invalidPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

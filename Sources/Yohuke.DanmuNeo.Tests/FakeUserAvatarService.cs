using System.Threading;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>为界面测试提供离线头像响应。</summary>
public sealed class FakeUserAvatarService : IUserAvatarService
{
    internal string AvatarPath { get; set; } = "";

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
        return Task.FromResult(AvatarPath);
    }

    /// <inheritdoc/>
    public Task InvalidateAvatarAsync(long userUid, string invalidPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

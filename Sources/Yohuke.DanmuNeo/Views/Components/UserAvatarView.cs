using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lucide.Avalonia;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>按可见用户加载头像，并在隐藏或换绑时取消请求和释放图像。</summary>
public sealed partial class UserAvatarView : UserControl
{
    private const int DECODE_WIDTH = 80;
    private readonly Ellipse circle;
    private readonly LucideIcon placeholder;
    private readonly DispatcherTimer refreshTimer = new() { Interval = TimeSpan.FromMinutes(5) };
    private readonly List<Visual> visibilityAncestors = [];
    private IUserAvatarService? avatars;
    private CancellationTokenSource? request;
    private bool isAttached;
    private bool isInViewport;
    private long activeUserUid;
    private int revision;

    /// <summary>需要显示头像的用户 UID。</summary>
    public static readonly StyledProperty<long> UserUidProperty =
        AvaloniaProperty.Register<UserAvatarView, long>(nameof(UserUid));

    /// <summary>是否显示并加载头像。</summary>
    public static readonly StyledProperty<bool> ShowAvatarProperty =
        AvaloniaProperty.Register<UserAvatarView, bool>(nameof(ShowAvatar), true);

    /// <summary>共享的用户头像缓存服务。</summary>
    public static readonly StyledProperty<IUserAvatarService?> AvatarServiceProperty =
        AvaloniaProperty.Register<UserAvatarView, IUserAvatarService?>(nameof(AvatarService));

    /// <summary>创建带占位头像的用户头像控件。</summary>
    public UserAvatarView()
    {
        AvaloniaXamlLoader.Load(this);
        circle = this.FindControl<Ellipse>("AvatarCircle")!;
        placeholder = this.FindControl<LucideIcon>("AvatarPlaceholder")!;
        refreshTimer.Tick += (_, _) => RefreshAvatar();
        EffectiveViewportChanged += (_, args) =>
        {
            isInViewport = args.EffectiveViewport.Intersects(new Rect(Bounds.Size));
            UpdateActivity();
        };
    }

    /// <summary>需要显示头像的用户 UID，零表示使用占位图。</summary>
    public long UserUid
    {
        get => GetValue(UserUidProperty);
        set => SetValue(UserUidProperty, value);
    }

    /// <summary>是否显示并加载头像。</summary>
    public bool ShowAvatar
    {
        get => GetValue(ShowAvatarProperty);
        set => SetValue(ShowAvatarProperty, value);
    }

    /// <summary>共享的用户头像缓存服务。</summary>
    public IUserAvatarService? AvatarService
    {
        get => GetValue(AvatarServiceProperty);
        set => SetValue(AvatarServiceProperty, value);
    }

    internal Bitmap? AvatarImage { get; private set; }
    internal string CurrentAvatarPath { get; private set; } = "";
    internal Task PendingLoad { get; private set; } = Task.CompletedTask;

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == AvatarServiceProperty)
        {
            if (isAttached && avatars is not null)
            {
                avatars.AvatarChanged -= OnAvatarChanged;
            }
            avatars = change.GetNewValue<IUserAvatarService?>();
            if (isAttached)
            {
                CancelRequest();
                activeUserUid = 0;
                ClearImage();
                if (avatars is not null)
                {
                    avatars.AvatarChanged += OnAvatarChanged;
                }
                UpdateActivity();
            }
        }
        else if (change.Property == ShowAvatarProperty)
        {
            IsVisible = ShowAvatar;
            UpdateActivity();
        }
        else if (change.Property == UserUidProperty || change.Property == IsVisibleProperty)
        {
            UpdateActivity();
        }
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        isAttached = true;
        if (avatars is not null)
        {
            avatars.AvatarChanged += OnAvatarChanged;
        }
        foreach (var ancestor in this.GetVisualAncestors())
        {
            visibilityAncestors.Add(ancestor);
            ancestor.PropertyChanged += OnAncestorPropertyChanged;
        }
        UpdateActivity();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        isAttached = false;
        isInViewport = false;
        if (avatars is not null)
        {
            avatars.AvatarChanged -= OnAvatarChanged;
        }
        foreach (var ancestor in visibilityAncestors)
        {
            ancestor.PropertyChanged -= OnAncestorPropertyChanged;
        }
        visibilityAncestors.Clear();
        UpdateActivity();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnAncestorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == IsVisibleProperty)
        {
            UpdateActivity();
        }
    }

    private void UpdateActivity()
    {
        var nextUserUid = isAttached && isInViewport && IsEffectivelyVisible && ShowAvatar ? UserUid : 0;
        if (nextUserUid <= 0 || avatars is null)
        {
            activeUserUid = 0;
            refreshTimer.Stop();
            CancelRequest();
            ClearImage();
        }
        else if (activeUserUid != nextUserUid)
        {
            activeUserUid = nextUserUid;
            ClearImage();
            refreshTimer.Start();
            RefreshAvatar();
        }
    }

    private void OnAvatarChanged(long userUid)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (activeUserUid == userUid)
            {
                RefreshAvatar();
            }
        });
    }

    private void RefreshAvatar()
    {
        if (activeUserUid <= 0)
        {
            return;
        }
        CancelRequest();
        request = new();
        PendingLoad = LoadAvatarAsync(activeUserUid, revision, request.Token, avatars!);
    }

    private async Task LoadAvatarAsync(long userUid, int requestRevision, CancellationToken cancellationToken,
        IUserAvatarService source, bool retryInvalidImage = true)
    {
        Bitmap? image = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = await source.GetAvatarAsync(userUid, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (path.Length == 0)
            {
                return;
            }
            var alreadyLoaded = await Dispatcher.UIThread.InvokeAsync(() =>
                requestRevision != revision || path == CurrentAvatarPath);
            if (alreadyLoaded)
            {
                return;
            }
            image = await Task.Run(() => DecodeAvatar(path), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (image is null)
            {
                await source.InvalidateAvatarAsync(userUid, path, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (retryInvalidImage)
                {
                    await LoadAvatarAsync(userUid, requestRevision, cancellationToken, source, false)
                        .ConfigureAwait(false);
                }
                return;
            }
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (requestRevision != revision || activeUserUid != userUid || cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                var previous = AvatarImage;
                AvatarImage = image;
                CurrentAvatarPath = path;
                circle.Fill = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
                placeholder.IsVisible = false;
                image = null;
                previous?.Dispose();
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            System.Diagnostics.Trace.TraceWarning("User avatar image could not be loaded for {0}: {1}", userUid,
                exception.GetType().Name);
        }
        finally
        {
            image?.Dispose();
        }
    }

    private static Bitmap? DecodeAvatar(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Bitmap.DecodeToWidth(stream, DECODE_WIDTH);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           ArgumentException or NotSupportedException or NullReferenceException)
        {
            return null;
        }
    }

    private void CancelRequest()
    {
        revision++;
        request?.Cancel();
        request?.Dispose();
        request = null;
    }

    private void ClearImage()
    {
        var previous = AvatarImage;
        AvatarImage = null;
        CurrentAvatarPath = "";
        circle.Bind(Shape.FillProperty, new DynamicResourceExtension("AppAvatarBrush"));
        placeholder.IsVisible = true;
        previous?.Dispose();
    }
}

using Yohuke.DanmuNeo.I18n;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.Models.BrowserLogin;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 账号登录设置页。
/// </summary>
public partial class AccountSettingsView : UserControl
{
    private readonly Func<BrowserLoginPlatform, Window, Task<BrowserCookieLoginResult?>> showBrowserLogin;
    private bool isLoginOpen;

    /// <summary>
    /// 初始化账号登录设置页。
    /// </summary>
    public AccountSettingsView()
        : this(ShowBrowserLoginDialogAsync)
    {
    }

    internal AccountSettingsView(Func<BrowserLoginPlatform, Window, Task<BrowserCookieLoginResult?>> showBrowserLogin)
    {
        this.showBrowserLogin = showBrowserLogin;
        InitializeComponent();
    }

    [RelayCommand]
    private async Task SetDefaultAccountAsync(BilibiliAccount? account)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.SetDefaultAccountAsync(account);
        }
    }

    [RelayCommand]
    private async Task DeleteAccountAsync(BilibiliAccount? account)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.DeleteAccountAsync(account);
        }
    }

    private async void BilibiliLogin_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var account = ((sender as Control)?.DataContext as AccountSettingsItemViewModel)?.Account;
        var cookie = await RunLoginDialogAsync(owner => ShowBilibiliLoginDialogAsync(owner, account is not null));

        if (cookie is not null)
        {
            await viewModel.ApplyBilibiliLoginAsync(cookie, account);
        }
    }

    private async void QQMusicBrowserLogin_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var result = await ShowBrowserLoginAsync(BrowserLoginPlatform.QQMusic);

        if (result is not null)
        {
            await viewModel.ApplyQQMusicBrowserLoginAsync(result);
        }
    }

    internal Task<BrowserCookieLoginResult?> ShowBrowserLoginAsync(BrowserLoginPlatform platform)
    {
        return RunLoginDialogAsync(owner => OpenBrowserLoginAsync(platform, owner));
    }

    private async Task<TResult?> RunLoginDialogAsync<TResult>(Func<Window, Task<TResult?>> showDialog)
        where TResult : class
    {
        if (isLoginOpen || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return null;
        }

        isLoginOpen = true;
        try
        {
            return await showDialog(owner);
        }
        finally
        {
            isLoginOpen = false;
        }
    }

    private async Task<string?> ShowBilibiliLoginDialogAsync(Window owner, bool relogin)
    {
        try
        {
            var window = new BilibiliLoginWindow(null, null, relogin);
            string? cookie;
            try
            {
                cookie = await window.ShowDialog<string?>(owner);
            }
            finally
            {
                window.Close();
            }

            if (!owner.IsVisible)
            {
                return null;
            }

            return window.BrowserLoginRequested
                ? (await OpenBrowserLoginAsync(BrowserLoginPlatform.Bilibili, owner))?.Cookie
                : cookie;
        }
        catch (Exception)
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.SetLocalizedStatus("UI.BilibiliLoginWindow.Unavailable");
                viewModel.Log.Error(Localization.Get("App.Log.BilibiliLogin"), viewModel.StatusMessage);
            }

            return null;
        }
    }

    private async Task<BrowserCookieLoginResult?> OpenBrowserLoginAsync(BrowserLoginPlatform platform, Window owner)
    {
        try
        {
            return await showBrowserLogin(platform, owner);
        }
        catch (Exception exception)
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.SetLocalizedStatus("UI.AccountSettingsView.BrowserUnavailable", new Func<string>(() => LocalizedExceptionText.Get(exception)));
                viewModel.LogErrorOnce(Localization.Get("UI.AccountSettingsView.BrowserLogSource"), Localization.Get("UI.AccountSettingsView.BrowserLogUnavailable"), exception);
            }

            return null;
        }
    }

    private static async Task<BrowserCookieLoginResult?> ShowBrowserLoginDialogAsync(BrowserLoginPlatform platform, Window owner)
    {
        var window = new BrowserCookieLoginWindow(platform);

        try
        {
            return await window.ShowDialog<BrowserCookieLoginResult?>(owner);
        }
        finally
        {
            window.Close();
        }
    }
}

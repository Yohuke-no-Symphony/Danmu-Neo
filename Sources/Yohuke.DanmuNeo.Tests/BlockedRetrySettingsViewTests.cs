using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Views;
using Yohuke.DanmuNeo.Views.Components;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>通过真实工作台设置窗口验证屏蔽重发开关的保存、独立性和即时翻译。</summary>
[Collection(AvaloniaDispatcherTestCollection.NAME)]
public class BlockedRetrySettingsViewTests
{
    /// <summary>屏蔽重发默认开启，关闭后保存并重新打开仍关闭，限频开关保持独立。</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BlockedRetryDefaultsOnAndPersistsIndependentlyOfRateRetry(bool rateRetryEnabled)
    {
        using var languageScope = new LocalizationTestScope();
        using var fixture = new WorkspaceSidebarTestContext(240);
        var main = new MainWindow { DataContext = fixture.ViewModel };

        try
        {
            main.Show();
            await main.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            var global = OpenGlobalSettings(workbench);
            var blockedRetry = global.FindControl<CheckBox>("RetryBlockedWithZeroWidthSpaceCheckBox")!;
            var rateRetry = FindRateRetryCheckBox(global);

            Assert.True(fixture.ViewModel.Settings.RetryBlockedWithZeroWidthSpace);
            Assert.True(blockedRetry.IsChecked);
            rateRetry.IsChecked = rateRetryEnabled;
            blockedRetry.IsChecked = false;
            Dispatcher.UIThread.RunJobs();

            Assert.False(fixture.ViewModel.Settings.RetryBlockedWithZeroWidthSpace);
            Assert.Equal(rateRetryEnabled, fixture.ViewModel.Settings.RetryWhenRateLimited);
            Assert.Equal(rateRetryEnabled, rateRetry.IsChecked);
            await fixture.ViewModel.CloseSettingsAsync();
            Dispatcher.UIThread.RunJobs();
            var saved = fixture.StateService.Load().Settings;

            Assert.False(saved.RetryBlockedWithZeroWidthSpace);
            Assert.Equal(rateRetryEnabled, saved.RetryWhenRateLimited);
            var reopened = OpenGlobalSettings(workbench);
            var reopenedBlockedRetry = reopened.FindControl<CheckBox>("RetryBlockedWithZeroWidthSpaceCheckBox")!;
            var reopenedRateRetry = FindRateRetryCheckBox(reopened);

            Assert.False(reopenedBlockedRetry.IsChecked);
            Assert.Equal(rateRetryEnabled, reopenedRateRetry.IsChecked);
            reopenedBlockedRetry.IsChecked = true;
            Dispatcher.UIThread.RunJobs();

            Assert.True(fixture.ViewModel.Settings.RetryBlockedWithZeroWidthSpace);
            Assert.Equal(rateRetryEnabled, fixture.ViewModel.Settings.RetryWhenRateLimited);
            await fixture.ViewModel.CloseSettingsAsync();
            Assert.True(fixture.StateService.Load().Settings.RetryBlockedWithZeroWidthSpace);
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    /// <summary>已打开设置窗口随语言切换更新新开关文案，保留当前关闭选择及限频设置。</summary>
    [AvaloniaFact]
    public async Task LanguageSwitchRefreshesBlockedRetryLabelWithoutChangingSelection()
    {
        using var languageScope = new LocalizationTestScope();
        using var fixture = new WorkspaceSidebarTestContext(240);
        var main = new MainWindow { DataContext = fixture.ViewModel };

        try
        {
            main.Show();
            await main.Initialization;
            Localization.SetLanguage("zh-CN");
            var global = OpenGlobalSettings(Assert.IsType<WorkbenchSession>(main.Workbench));
            var blockedRetry = global.FindControl<CheckBox>("RetryBlockedWithZeroWidthSpaceCheckBox")!;
            var rateRetryEnabled = fixture.ViewModel.Settings.RetryWhenRateLimited;

            Assert.Equal("被屏蔽后加入零宽空格重发", blockedRetry.Content);
            blockedRetry.IsChecked = false;
            Localization.SetLanguage("en-US");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Retry blocked messages with zero-width spaces", blockedRetry.Content);
            Assert.False(blockedRetry.IsChecked);
            Assert.False(fixture.ViewModel.Settings.RetryBlockedWithZeroWidthSpace);
            Assert.Equal(rateRetryEnabled, fixture.ViewModel.Settings.RetryWhenRateLimited);
            Localization.SetLanguage("zh-CN");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("被屏蔽后加入零宽空格重发", blockedRetry.Content);
            Assert.False(blockedRetry.IsChecked);
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    private static GlobalSettingsView OpenGlobalSettings(WorkbenchSession workbench)
    {
        workbench.MenuCatalog.SettingsCommand.Execute(null);
        var settings = Assert.IsAssignableFrom<Window>(workbench.Windows.FindAuxiliary("settings"));
        Dispatcher.UIThread.RunJobs();
        settings.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return settings.GetVisualDescendants().OfType<GlobalSettingsView>().Single();
    }

    private static CheckBox FindRateRetryCheckBox(GlobalSettingsView global)
    {
        return global.FindControl<Panel>("GlobalSendingOptions")!.Children.OfType<CheckBox>()
            .Single(checkBox => checkBox.Content as string == Localization.Get("UI.GlobalSettingsView.RetryRateLimitedMessages"));
    }
}

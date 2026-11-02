using System.Text.Json;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>发送策略与兼容行为的离线回归测试。</summary>
public class SendingSettingsMigrationTests
{
    /// <summary>拆分和旧整包配置迁移旧开关，明确新键优先且写回时删除旧键。</summary>
    [Theory]
    [InlineData("{\"ResendWhenShielded\":false}", false)]
    [InlineData("{\"ResendWhenShielded\":true}", true)]
    [InlineData("{\"RetryWhenRateLimited\":false,\"ResendWhenShielded\":true}", false)]
    [InlineData("{\"ResendWhenShielded\":false,\"RetryWhenRateLimited\":true}", true)]
    [InlineData("{}", true)]
    public void SplitAndLegacySettingsMigrateWithNewKeyPrecedence(string json, bool expected)
    {
        foreach (var legacy in new[] { false, true })
        {
            var directory = Path.Combine(Path.GetTempPath(), "DanmuNeo-send-migration-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var service = new AppStateService(directory);
                File.WriteAllText(legacy ? service.StateFilePath : service.SettingsFilePath,
                    legacy ? "{\"Settings\":" + json + "}" : json);
                var state = service.Load();
                Assert.Equal(expected, state.Settings.RetryWhenRateLimited);
                Assert.True(state.Settings.RetryBlockedWithZeroWidthSpace);
                service.Save(state);
                var saved = File.ReadAllText(service.SettingsFilePath);
                Assert.Contains("RetryWhenRateLimited", saved);
                Assert.Contains("RetryBlockedWithZeroWidthSpace", saved);
                Assert.DoesNotContain("ResendWhenShielded", saved);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }
    }

    /// <summary>新开关在拆分与旧整包配置中独立于限频和旧键，并保留明确的关闭选择。</summary>
    [Theory]
    [InlineData("""{"RetryBlockedWithZeroWidthSpace":false}""", false, true)]
    [InlineData("""{"RetryBlockedWithZeroWidthSpace":true,"RetryWhenRateLimited":false}""", true, false)]
    [InlineData("""{"RetryBlockedWithZeroWidthSpace":false,"ResendWhenShielded":true}""", false, true)]
    [InlineData("""{"RetryBlockedWithZeroWidthSpace":false,"ResendWhenShielded":false}""", false, false)]
    public void ZeroWidthRetrySettingRoundTripsIndependently(string json, bool expectedBlockedRetry, bool expectedRateRetry)
    {
        foreach (var legacy in new[] { false, true })
        {
            var directory = Path.Combine(Path.GetTempPath(), "DanmuNeo-zero-width-settings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var service = new AppStateService(directory);
                File.WriteAllText(legacy ? service.StateFilePath : service.SettingsFilePath,
                    legacy ? "{\"Settings\":" + json + "}" : json);
                var state = service.Load();

                Assert.Equal(expectedBlockedRetry, state.Settings.RetryBlockedWithZeroWidthSpace);
                Assert.Equal(expectedRateRetry, state.Settings.RetryWhenRateLimited);
                service.Save(state);
                var reloaded = service.Load();
                using var saved = JsonDocument.Parse(File.ReadAllText(service.SettingsFilePath));

                Assert.Equal(expectedBlockedRetry, reloaded.Settings.RetryBlockedWithZeroWidthSpace);
                Assert.Equal(expectedRateRetry, reloaded.Settings.RetryWhenRateLimited);
                Assert.Equal(expectedBlockedRetry, saved.RootElement.GetProperty("RetryBlockedWithZeroWidthSpace").GetBoolean());
                Assert.False(saved.RootElement.TryGetProperty("ResendWhenShielded", out _));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }
    }

    /// <summary>损坏配置从备份恢复时保留新开关的明确选择及独立的限频设置。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecoveredBackupKeepsZeroWidthRetrySetting(bool enabled)
    {
        var directory = Path.Combine(Path.GetTempPath(), "DanmuNeo-zero-width-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var service = new AppStateService(directory);
            var state = new AppState
            {
                Settings = new()
                {
                    RetryBlockedWithZeroWidthSpace = enabled,
                    RetryWhenRateLimited = !enabled
                }
            };
            service.Save(state);
            service.Save(state);
            File.WriteAllText(service.SettingsFilePath, "broken-json");

            var restored = service.Load();

            Assert.Equal(enabled, restored.Settings.RetryBlockedWithZeroWidthSpace);
            Assert.Equal(!enabled, restored.Settings.RetryWhenRateLimited);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>备份恢复路径同样迁移旧的发送重试开关。</summary>
    [Fact]
    public void RecoveredBackupMigratesLegacyRetrySetting()
    {
        var directory = Path.Combine(Path.GetTempPath(), "DanmuNeo-send-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var service = new AppStateService(directory);
            service.Save(new());
            service.Save(new());
            var backup = Directory.GetFiles(service.BackupDirectory, "settings.json*.bak").First();
            File.WriteAllText(backup, "{\"ResendWhenShielded\":false}");
            File.WriteAllText(service.SettingsFilePath, "broken-json");
            var state = service.Load();
            Assert.False(state.Settings.RetryWhenRateLimited);
            Assert.True(state.Settings.RetryBlockedWithZeroWidthSpace);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}

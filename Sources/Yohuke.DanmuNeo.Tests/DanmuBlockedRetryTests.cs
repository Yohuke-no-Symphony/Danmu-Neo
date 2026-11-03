using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证明确屏蔽后的有界重发、分段进度和请求生命周期。</summary>
public class DanmuBlockedRetryTests
{
    /// <summary>两种屏蔽字段均触发重发，最终历史和诊断对应实际零宽文本。</summary>
    [Theory]
    [InlineData("message")]
    [InlineData("msg")]
    public async Task BlockedHttpResponseRetriesWithActualTextAndOneFinalRecord(string field)
    {
        using var handler = new DanmuSendHttpHandler { ResponseJson = """{"code":0}""" };
        handler.ResponseBodies.Enqueue("{\"code\":0,\"" + field + "\":\"f\"}");
        using var client = new HttpClient(handler);
        var sender = new DanmuSendService(client, new FakeBilibiliDanmuConfigService());
        var records = new List<DanmuFeedItem>();
        sender.RecordCreated += (_, record) => records.Add(record);
        var request = CreateRequest("同传");

        var result = await sender.SendAsync(request, CreateAccount(), new() { SendIntervalMs = 0 });

        Assert.True(result.IsComplete);
        Assert.Equal(["【同传】", "【同\u200B传】"], handler.Forms.Select(form => form["msg"]));
        var part = Assert.Single(result.Results);
        Assert.Same(Assert.Single(records), part.Record);
        Assert.Equal(2, part.AttemptCount);
        Assert.Equal("【同\u200B传】", part.Record.Content);
        Assert.Equal(result.Progress.OperationId + ":0:0", part.Record.SendRecordId);
        Assert.Equal(2, result.Progress.GetAcceptedLength(0));
        Assert.Equal("同传", result.Progress.Request.Contents[0].Body);
        Assert.NotNull(part.Diagnostic);
        Assert.Equal(part.Record.Content.Length, part.Diagnostic.TextLength);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(part.Record.Content))),
            part.Diagnostic.TextSha256);
    }

    /// <summary>零宽文本仍被屏蔽时停止，不再叠加或继续发送后续正文。</summary>
    [Fact]
    public async Task RepeatedBlockStopsAfterOneTransformation()
    {
        var transport = new ScriptedDanmuSendTransport();
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.Blocked });
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.Blocked });
        var sender = CreateSender(transport);

        var result = await sender.SendAsync(CreateRequest("甲乙丙丁"), CreateAccount(), new() { SendIntervalMs = 0 });

        Assert.False(result.IsComplete);
        Assert.Equal(["【甲乙丙丁】", "【甲\u200B乙\u200B丙\u200B丁】"], transport.Messages);
        var failure = Assert.Single(result.Results);
        Assert.Equal(DanmuSendFailureKind.Blocked, failure.FailureKind);
        Assert.Equal(2, failure.AttemptCount);
        Assert.Equal(0, result.Progress.GetAcceptedLength(0));
    }

    /// <summary>关闭功能时明确屏蔽仍直接终止。</summary>
    [Fact]
    public async Task DisabledRetryKeepsBlockedResponseTerminal()
    {
        var transport = new ScriptedDanmuSendTransport();
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.Blocked });

        var result = await CreateSender(transport).SendAsync(CreateRequest("正文"), CreateAccount(),
            new() { SendIntervalMs = 0, RetryBlockedWithZeroWidthSpace = false });

        Assert.False(result.IsComplete);
        Assert.Single(transport.Messages);
        Assert.Equal(1, Assert.Single(result.Results).AttemptCount);
    }

    /// <summary>其他拒绝和结果不明的失败不能进入零宽重发。</summary>
    [Theory]
    [InlineData(DanmuSendFailureKind.Rejected)]
    [InlineData(DanmuSendFailureKind.OutcomeUnknown)]
    public async Task OtherFailuresNeverTransformText(DanmuSendFailureKind failure)
    {
        var transport = new ScriptedDanmuSendTransport();
        transport.Results.Enqueue(new() { FailureKind = failure });

        var result = await CreateSender(transport).SendAsync(CreateRequest("正文"), CreateAccount(),
            new() { SendIntervalMs = 0 });

        Assert.False(result.IsComplete);
        Assert.Equal("【正文】", Assert.Single(transport.Messages));
        Assert.Equal(failure, Assert.Single(result.Results).FailureKind);
    }

    /// <summary>没有可改变的文字间隔时不重复发送相同请求。</summary>
    [Theory]
    [InlineData("甲")]
    [InlineData("👩‍💻")]
    [InlineData("甲\u200B乙")]
    public async Task UnchangedBodyDoesNotTriggerRetry(string body)
    {
        var transport = new ScriptedDanmuSendTransport();
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.Blocked });

        var result = await CreateSender(transport).SendAsync(CreateRequest(body), CreateAccount(),
            new() { SendIntervalMs = 0 });

        Assert.False(result.IsComplete);
        Assert.Single(transport.Messages);
    }

    /// <summary>只重分被屏蔽段，先前成功段与后续正常段保持原文及顺序。</summary>
    [Fact]
    public async Task ExpandedRetryStaysInsideRejectedSegmentAndRestoresNormalMode()
    {
        var transport = new ScriptedDanmuSendTransport();
        transport.Results.Enqueue(new());
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.Blocked });
        var sender = CreateSender(transport, 16);
        var records = new List<DanmuFeedItem>();
        sender.RecordCreated += (_, record) => records.Add(record);
        var request = CreateRequest("甲乙丙丁戊己庚辛壬癸子丑寅卯");

        var result = await sender.SendAsync(request, CreateAccount(), new() { SendIntervalMs = 0 });

        Assert.True(result.IsComplete);
        Assert.Equal([
            "【甲乙丙丁戊己】", "【…庚辛壬癸子】",
            "【…庚\u200B辛\u200B壬】", "【…癸\u200B子】", "【…丑寅卯】"
        ], transport.Messages);
        Assert.Equal([1, 2, 1, 1], result.Results.Select(part => part.AttemptCount));
        Assert.Equal(4, records.Count);
        Assert.All(records, record => Assert.True(record.IsSendAccepted));
        Assert.All(transport.Messages, message =>
            Assert.InRange(DanmuMessageSegmenter.GetQuotaLength(message), 1, 16));
        Assert.Equal(request.Contents[0].Body, Reassemble(result));
        Assert.Equal(request.Contents[0].Body.Length, result.Progress.GetAcceptedLength(0));
    }

    /// <summary>零宽子段部分成功后失败，手动续发只补未接受原文并复用失败记录标识。</summary>
    [Fact]
    public async Task PartialRetryPreservesSourceProgressAndContinuationRecordIdentity()
    {
        var transport = new ScriptedDanmuSendTransport();
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.Blocked });
        transport.Results.Enqueue(new());
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.Blocked });
        var sender = CreateSender(transport, 12);
        var account = CreateAccount();
        var request = CreateRequest("甲乙丙丁戊己庚辛");
        var settings = new AppSettings { SendIntervalMs = 0 };

        var first = await sender.SendAsync(request, account, settings);

        Assert.False(first.IsComplete);
        Assert.Equal(2, first.Progress.GetAcceptedLength(0));
        Assert.Equal(["【甲乙丙丁】", "【甲\u200B乙】", "【…丙\u200B丁】"], transport.Messages);
        Assert.True(first.Results[0].IsAccepted);
        Assert.False(first.Results[1].IsAccepted);
        var failedId = first.Results[1].Record.SendRecordId;

        var continued = await sender.SendAsync(request, account, settings, first.Progress);

        Assert.True(continued.IsComplete);
        Assert.Same(first.Progress, continued.Progress);
        Assert.Equal(failedId, continued.Results[1].Record.SendRecordId);
        Assert.Equal(["【…丙丁戊】", "【…己庚辛】"], transport.Messages.Skip(3));
        Assert.Equal(request.Contents[0].Body, Reassemble(continued));
    }

    /// <summary>完成一个被屏蔽范围后，下一个原文分段仍可独立处理一次。</summary>
    [Fact]
    public async Task SeparateRejectedOriginalSegmentsCanEachRetryOnce()
    {
        var transport = new ScriptedDanmuSendTransport();
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.Blocked });
        transport.Results.Enqueue(new());
        transport.Results.Enqueue(new());
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.Blocked });
        var request = CreateRequest("甲乙丙丁戊己");

        var result = await CreateSender(transport, 12).SendAsync(request, CreateAccount(),
            new() { SendIntervalMs = 0 });

        Assert.True(result.IsComplete);
        Assert.Equal(["【甲乙丙丁】", "【甲\u200B乙】", "【…丙\u200B丁】", "【…戊己】", "【…戊\u200B己】"],
            transport.Messages);
        Assert.Equal([2, 1, 2], result.Results.Select(part => part.AttemptCount));
        Assert.Equal(request.Contents[0].Body, Reassemble(result));
    }

    /// <summary>切换为零宽文本后重新计算限频次数，重试仍遵循一秒、两秒退避。</summary>
    [Fact]
    public async Task SwitchingPayloadResetsIndependentRateLimitBudget()
    {
        var transport = new ScriptedDanmuSendTransport();
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.RateLimited });
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.Blocked });
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.RateLimited });
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.RateLimited });
        var clock = new ManualSendTimeProvider();
        var sender = CreateSender(transport, clock: clock);
        var startedAt = clock.GetUtcNow();

        var sending = sender.SendAsync(CreateRequest("正文"), CreateAccount(), new() { SendIntervalMs = 0 });
        await clock.WaitForTimerAsync();
        clock.Advance(TimeSpan.FromSeconds(1));
        await clock.WaitForTimerAsync();
        clock.Advance(TimeSpan.FromSeconds(1));
        await clock.WaitForTimerAsync();
        clock.Advance(TimeSpan.FromSeconds(2));
        var result = await sending;

        Assert.True(result.IsComplete);
        Assert.Equal(["【正文】", "【正文】", "【正\u200B文】", "【正\u200B文】", "【正\u200B文】"], transport.Messages);
        Assert.Equal(5, Assert.Single(result.Results).AttemptCount);
        Assert.Equal(TimeSpan.FromSeconds(4), clock.GetUtcNow() - startedAt);
    }

    /// <summary>零宽文本的限频重试也有上限，不能重新进入屏蔽处理。</summary>
    [Fact]
    public async Task TransformedPayloadRateRetriesRemainBounded()
    {
        var transport = new ScriptedDanmuSendTransport();
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.Blocked });
        for (var index = 0; index < 3; index++)
        {
            transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.RateLimited });
        }
        var clock = new ManualSendTimeProvider();
        var sender = CreateSender(transport, clock: clock);

        var sending = sender.SendAsync(CreateRequest("正文"), CreateAccount(), new() { SendIntervalMs = 0 });
        await clock.WaitForTimerAsync();
        clock.Advance(TimeSpan.FromSeconds(1));
        await clock.WaitForTimerAsync();
        clock.Advance(TimeSpan.FromSeconds(2));
        var result = await sending;

        Assert.False(result.IsComplete);
        Assert.Equal(4, transport.Messages.Count);
        Assert.Equal(DanmuSendFailureKind.RateLimited, Assert.Single(result.Results).FailureKind);
        Assert.Equal(4, result.Results[0].AttemptCount);
    }

    /// <summary>实际长度仍小于配置时严格缩短零宽文本，保留全部原文及处理模式。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaleLengthLimitShrinksTransformedSegmentsUntilAccepted(bool refreshedLimitIsAccurate)
    {
        using var handler = new DanmuSendHttpHandler { ResponseJson = """{"code":0}""", MaximumMessageLength = 16 };
        handler.ResponseBodies.Enqueue("""{"code":0,"msg":"f"}""");
        using var client = new HttpClient(handler);
        var config = new FakeBilibiliDanmuConfigService();
        config.ReadAsync = (_, _, _, refresh) => Task.FromResult(new DanmuRoomConfig
        {
            Length = refresh && refreshedLimitIsAccurate ? 16 : 40
        });
        var sender = new DanmuSendService(client, config);
        var request = CreateRequest("甲乙丙丁戊己");

        var result = await sender.SendAsync(request, CreateAccount(), new() { SendIntervalMs = 0 });

        Assert.True(result.IsComplete);
        var expectedLengths = refreshedLimitIsAccurate ? new[] { 8, 13, 7 } : [8, 13, 9, 7];
        Assert.Equal(expectedLengths, handler.Forms.Take(expectedLengths.Length).Select(form => form["msg"].Length));
        Assert.Equal(refreshedLimitIsAccurate ? 4 : 6, handler.Forms.Count);
        var transformedForms = refreshedLimitIsAccurate ? handler.Forms.Skip(1) : handler.Forms.Skip(1).SkipLast(1);
        Assert.All(transformedForms,
            form => Assert.Contains("\u200B", form["msg"], StringComparison.Ordinal));
        Assert.Equal(refreshedLimitIsAccurate ? "【…丁\u200B戊\u200B己】" : "【…己】", handler.Forms[^1]["msg"]);
        Assert.All(result.Results, part =>
            Assert.InRange(DanmuMessageSegmenter.GetQuotaLength(part.Record.Content), 1, 16));
        Assert.Equal(2, config.GetRequests.Count);
        Assert.Equal(request.Contents[0].Body, Reassemble(result));
        Assert.Equal(refreshedLimitIsAccurate ? 3 : 4, result.Results[0].AttemptCount);
    }

    /// <summary>最小完整文字元素也被长度拒绝时终止，不循环或吞掉后续正文。</summary>
    [Fact]
    public async Task SmallestRetrySegmentLengthRejectionStops()
    {
        var transport = new ScriptedDanmuSendTransport();
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.Blocked });
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.LengthExceeded });

        var result = await CreateSender(transport, 16).SendAsync(CreateRequest("👩‍💻甲"), CreateAccount(),
            new() { SendIntervalMs = 0 });

        Assert.False(result.IsComplete);
        Assert.Equal(["【👩‍💻甲】", "【👩‍💻】"], transport.Messages);
        Assert.Equal(0, result.Progress.GetAcceptedLength(0));
        Assert.Equal(DanmuSendFailureKind.LengthExceeded, Assert.Single(result.Results).FailureKind);
    }

    /// <summary>等待发送间隔时取消，不启动零宽请求或发布未确认的历史。</summary>
    [Fact]
    public async Task CancellationDuringRetryIntervalPreventsAnotherRequest()
    {
        var transport = new ScriptedDanmuSendTransport();
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.Blocked });
        var clock = new ManualSendTimeProvider();
        var sender = CreateSender(transport, clock: clock);
        var records = new List<DanmuFeedItem>();
        sender.RecordCreated += (_, record) => records.Add(record);
        using var cancellation = new CancellationTokenSource();

        var sending = sender.SendAsync(CreateRequest("正文"), CreateAccount(), new() { SendIntervalMs = 750 },
            cancellationToken: cancellation.Token);
        await clock.WaitForTimerAsync();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sending);
        clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Single(transport.Messages);
        Assert.Empty(records);
    }

    /// <summary>原文和重发完成前，后排消息不能插队且每次请求遵守全局间隔。</summary>
    [Fact]
    public async Task RetryKeepsLaterDeliveryQueuedAndUsesSendInterval()
    {
        var transport = new ScriptedDanmuSendTransport();
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.Blocked });
        var clock = new ManualSendTimeProvider();
        var sender = CreateSender(transport, clock: clock);
        var account = CreateAccount();
        var settings = new AppSettings { SendIntervalMs = 1000 };
        var first = sender.SendAsync(CreateRequest("先发"), account, settings);
        await clock.WaitForTimerAsync();
        var second = sender.SendAsync(CreateRequest("后发"), account, settings);

        Assert.Single(transport.Messages);
        clock.Advance(TimeSpan.FromSeconds(1));
        await clock.WaitForTimerAsync();
        Assert.True((await first).IsComplete);
        Assert.False(second.IsCompleted);
        Assert.Equal(["【先发】", "【先\u200B发】"], transport.Messages);
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.True((await second).IsComplete);
        Assert.Equal("【后发】", transport.Messages[^1]);
    }

    /// <summary>零宽子段成功响应期间即使取消，也先保存已确认的原文范围供续发。</summary>
    [Fact]
    public async Task ConfirmedRetrySuccessDuringCancellationKeepsSourceProgress()
    {
        using var cancellation = new CancellationTokenSource();
        var transport = new ScriptedDanmuSendTransport();
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.Blocked });
        transport.BeforeResponse = () =>
        {
            if (transport.Messages.Count == 2)
            {
                cancellation.Cancel();
            }
        };
        var sender = CreateSender(transport, 12);
        var account = CreateAccount();
        var request = CreateRequest("甲乙丙丁戊己");
        var progress = new DanmuDeliveryProgress(request, account);
        var settings = new AppSettings { SendIntervalMs = 0 };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sender.SendAsync(request, account, settings, progress, cancellation.Token));

        Assert.Equal(2, progress.GetAcceptedLength(0));
        Assert.True(Assert.Single(progress.Results).IsAccepted);
        Assert.Equal(["【甲乙丙丁】", "【甲\u200B乙】"], transport.Messages);
        transport.BeforeResponse = null;
        var continued = await sender.SendAsync(request, account, settings, progress);
        Assert.True(continued.IsComplete);
        Assert.Equal(request.Contents[0].Body, Reassemble(continued));
    }

    /// <summary>凭据失效立即撤销等待中的零宽重发，并保留账号不可用结果。</summary>
    [Fact]
    public async Task CredentialRevocationCancelsPendingRetry()
    {
        using var sessions = new BilibiliAccountSessionService(new ControlledAccountValidationService());
        var account = CreateAccount();
        sessions.Register(account);
        var transport = new ScriptedDanmuSendTransport();
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.Blocked });
        var clock = new ManualSendTimeProvider();
        var sender = new DanmuSendService(danmuConfigService: new FakeBilibiliDanmuConfigService(),
            transport: transport, timeProvider: clock, accountSessions: sessions);

        var sending = sender.SendAsync(CreateRequest("正文"), account, new() { SendIntervalMs = 750 });
        await clock.WaitForTimerAsync();
        account.Cookie = "SESSDATA=changed;bili_jct=changed";
        var result = await sending;
        clock.Advance(TimeSpan.FromMinutes(1));

        Assert.False(result.IsComplete);
        Assert.Single(transport.Messages);
        Assert.Equal(DanmuSendFailureKind.LocalFailure, Assert.Single(result.Results).FailureKind);
    }

    /// <summary>零宽子段明确成功时即使凭据同时改变，也保留原文进度并停止剩余正文。</summary>
    [Fact]
    public async Task ConfirmedRetrySuccessDuringCredentialRevocationKeepsSourceProgress()
    {
        using var sessions = new BilibiliAccountSessionService(new ControlledAccountValidationService());
        var account = CreateAccount();
        sessions.Register(account);
        var transport = new ScriptedDanmuSendTransport();
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.Blocked });
        transport.BeforeResponse = () =>
        {
            if (transport.Messages.Count == 2)
            {
                account.Cookie = "SESSDATA=changed;bili_jct=changed";
            }
        };
        var sender = new DanmuSendService(danmuConfigService: new FakeBilibiliDanmuConfigService
        {
            Config = new() { Length = 12 }
        }, transport: transport, accountSessions: sessions);

        var result = await sender.SendAsync(CreateRequest("甲乙丙丁戊己"), account, new() { SendIntervalMs = 0 });

        Assert.False(result.IsComplete);
        Assert.Equal(2, result.Progress.GetAcceptedLength(0));
        Assert.Equal(["【甲乙丙丁】", "【甲\u200B乙】"], transport.Messages);
        Assert.True(result.Results[0].IsAccepted);
        Assert.Equal(DanmuSendFailureKind.LocalFailure, result.Results[1].FailureKind);
    }

    private static DanmuSendService CreateSender(ScriptedDanmuSendTransport transport, int limit = 40,
        TimeProvider? clock = null)
    {
        return new(danmuConfigService: new FakeBilibiliDanmuConfigService { Config = new() { Length = limit } },
            transport: transport, timeProvider: clock);
    }

    private static BilibiliAccount CreateAccount()
    {
        return new() { Id = "account", Cookie = "SESSDATA=test;bili_jct=test;DedeUserID=42" };
    }

    private static DanmuSendRequest CreateRequest(string body)
    {
        return new() { RoomId = "100", Contents = [new() { Body = body, Prefix = "【", Suffix = "】" }] };
    }

    private static string Reassemble(DanmuDeliveryResult result)
    {
        return string.Concat(result.Results.Select(part =>
            part.Record.Content[1..^1].TrimStart('…').Replace("\u200B", "")));
    }
}

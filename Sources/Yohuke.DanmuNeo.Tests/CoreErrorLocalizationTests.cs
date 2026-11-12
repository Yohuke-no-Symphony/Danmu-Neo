using Avalonia.Headless.XUnit;
using Newtonsoft.Json;
using Yohuke.DanmuCore.Errors;
using Yohuke.DanmuCore.Models.Search;
using Yohuke.DanmuNeo.I18n;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证共享错误在产品语言切换后重新呈现，且不改变原始异常。</summary>
[Collection(AvaloniaDispatcherTestCollection.NAME)]
public sealed class CoreErrorLocalizationTests
{
    /// <summary>同一共享异常始终保持真实类型与固定诊断，显示文本按当前语言生成。</summary>
    [AvaloniaFact]
    public void ExistingCoreErrorRendersWithTheCurrentLanguage()
    {
        using var language = new LocalizationTestScope();
        var error = CoreExceptionInfo.Create(CoreErrorCode.ApiEmptyJsonResult,
            message => new JsonSerializationException(message));
        var original = error.Message;
        Localization.SetLanguage("zh-CN");
        var chinese = LocalizedExceptionText.Get(error);

        Localization.SetLanguage("en-US");

        Assert.IsType<JsonSerializationException>(error);
        Assert.Equal(original, error.Message);
        Assert.NotEqual(chinese, LocalizedExceptionText.Get(error));
        Assert.Equal(Localization.Get("Services.Api.EmptyJsonResult"), LocalizedExceptionText.Get(error));
    }

    /// <summary>已显示的搜索问题随语言切换刷新，不重新发起网络查找。</summary>
    [AvaloniaFact]
    public async Task SearchIssueRefreshesWithoutAnotherRequest()
    {
        using var language = new LocalizationTestScope();
        Localization.SetLanguage("zh-CN");
        using var context = new LiveRoomSearchTestContext();
        context.SearchService.RespondAsync = (_, _) => Task.FromResult(new LiveRoomSearchPage
        {
            Issues = [new() { Source = LiveRoomSearchSource.Nickname, Error = new(CoreErrorCode.RoomSearchResultsMissing) }]
        });
        context.ViewModel.OpenAddRoomDialog(null);
        context.ViewModel.LiveRoomSearchText = "主播";
        await context.ViewModel.SearchLiveRoomsAsync();
        var chinese = context.ViewModel.LiveRoomSearchStatus;

        Localization.SetLanguage("en-US");

        Assert.NotEqual(chinese, context.ViewModel.LiveRoomSearchStatus);
        Assert.Contains(Localization.Get("Services.BilibiliRoomSearch.ResultsMissing"), context.ViewModel.LiveRoomSearchStatus);
        Assert.Single(context.SearchService.Requests);
    }
}

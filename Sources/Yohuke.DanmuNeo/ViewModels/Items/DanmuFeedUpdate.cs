using Yohuke.DanmuCore.Apis.Models.Bilibili;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.ViewModels.Items;

internal readonly record struct DanmuFeedUpdate(
    DanmuFeedItem Item,
    BilibiliDanmuMessage? ReceivedMessage = null,
    CancellationToken CancellationToken = default);

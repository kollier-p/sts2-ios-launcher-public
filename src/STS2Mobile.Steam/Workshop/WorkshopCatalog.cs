using SteamKit2.Internal;
using STS2Mobile.Steam.Connection;

namespace STS2Mobile.Steam.Workshop;

public enum WorkshopBrowseMode { Popular, Latest, Search, Subscribed }

public sealed record WorkshopItem(
    ulong Id, string Title, string Description, string PreviewUrl,
    ulong Size, ulong ContentHandle, string DirectUrl, uint UpdatedAt)
{
    public string SteamUrl => "https://steamcommunity.com/sharedfiles/filedetails/?id=" + Id;
}

public sealed record WorkshopPage(IReadOnlyList<WorkshopItem> Items, uint Total, int Page, WorkshopBrowseMode Mode, string Search);

/// <summary>
/// Uses the Steam account already signed in to the launcher for Workshop search,
/// details, and the player's actual Steam subscriptions. No API key or second login.
/// </summary>
public sealed class WorkshopCatalog(SteamConnection connection)
{
    public const int PageSize = 20;
    private const uint RankedByVotes = 0;
    private const uint RankedByPublicationDate = 1;
    private const uint RankedByTextSearch = 12;

    public async Task<WorkshopPage> BrowseAsync(ulong steamId, WorkshopBrowseMode mode, string search, int page,
        CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        search = (search ?? "").Trim();
        if (search.Length > 200) search = search[..200];
        if (mode == WorkshopBrowseMode.Subscribed)
        {
            var request = new CPublishedFile_GetUserFiles_Request
            {
                steamid = steamId,
                appid = SteamApp.Id,
                type = "mysubscriptions",
                sortmethod = "subscriptiondate",
                page = (uint)page,
                numperpage = PageSize,
                language = 0,
                return_short_description = true,
                strip_description_bbcode = true
            };
            var answer = await Task.Run(() => connection.SendPublishedFileAsync<
                CPublishedFile_GetUserFiles_Request, CPublishedFile_GetUserFiles_Response>("GetUserFiles", request),
                cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
            return new WorkshopPage(answer.publishedfiledetails.Select(Convert).OfType<WorkshopItem>().ToArray(),
                answer.total, page, mode, search);
        }

        var query = new CPublishedFile_QueryFiles_Request
        {
            appid = SteamApp.Id,
            query_type = mode switch
            {
                WorkshopBrowseMode.Latest => RankedByPublicationDate,
                WorkshopBrowseMode.Search => RankedByTextSearch,
                _ => RankedByVotes
            },
            search_text = mode == WorkshopBrowseMode.Search ? search : "",
            page = (uint)page,
            numperpage = PageSize,
            language = 0,
            return_details = true,
            return_short_description = true,
            strip_description_bbcode = true
        };
        var results = await Task.Run(() => connection.SendPublishedFileAsync<
            CPublishedFile_QueryFiles_Request, CPublishedFile_QueryFiles_Response>("QueryFiles", query),
            cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
        return new WorkshopPage(results.publishedfiledetails.Select(Convert).OfType<WorkshopItem>().ToArray(),
            results.total, page, mode, search);
    }

    public async Task<WorkshopItem> GetAsync(ulong id, CancellationToken cancellationToken)
    {
        if (id == 0) throw new ArgumentOutOfRangeException(nameof(id));
        var request = new CPublishedFile_GetDetails_Request
        {
            appid = SteamApp.Id,
            short_description = false,
            strip_description_bbcode = true,
            includechildren = true,
            language = 0
        };
        request.publishedfileids.Add(id);
        var response = await Task.Run(() => connection.SendPublishedFileAsync<
            CPublishedFile_GetDetails_Request, CPublishedFile_GetDetails_Response>("GetDetails", request),
            cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
        var item = response.publishedfiledetails.Select(Convert).FirstOrDefault(x => x?.Id == id);
        return item ?? throw new InvalidDataException("Steam did not return this Workshop item.");
    }

    private static WorkshopItem? Convert(PublishedFileDetails detail)
    {
        if (detail.publishedfileid == 0 || detail.consumer_appid != SteamApp.Id || detail.result != 1)
            return null;
        return new WorkshopItem(detail.publishedfileid, detail.title ?? "", 
            string.IsNullOrEmpty(detail.short_description) ? detail.file_description ?? "" : detail.short_description,
            detail.preview_url ?? "", detail.file_size, detail.hcontent_file, detail.file_url ?? "", detail.time_updated);
    }
}

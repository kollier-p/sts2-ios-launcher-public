using System;
using System.Collections.Generic;
using Godot;
using STS2Mobile.Steam.Workshop;

namespace STS2Mobile.FullGame;

public sealed partial class LauncherView
{
    private ScrollContainer _workshopScroll = null!;
    private VBoxContainer _workshopList = null!;
    private LineEdit _workshopSearch = null!;
    private Button _workshopSearchButton = null!;
    private Button _workshopPopular = null!;
    private Button _workshopLatest = null!;
    private Button _workshopSubscribed = null!;
    private Button _workshopPrevious = null!;
    private Button _workshopNext = null!;
    private Button _workshopDownload = null!;
    private readonly List<Button> _workshopItemButtons = [];

    public event Action<WorkshopBrowseMode, string, int>? WorkshopQueryRequested;
    public event Action<ulong>? WorkshopItemRequested;
    public event Action<ulong>? WorkshopDownloadRequested;

    private Control[] CreateWorkshopControls(VBoxContainer page, VBoxContainer actions)
    {
        _workshopSearch = new LineEdit
        {
            PlaceholderText = "Search Slay the Spire 2 Workshop",
            CustomMinimumSize = new Vector2(0, 105),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        _workshopSearch.TextSubmitted += _ => RequestWorkshopSearch();
        _workshopSearchButton = LauncherTheme.MakeButton("Search", "Button", RequestWorkshopSearch);
        _workshopPopular = LauncherTheme.MakeButton("Popular", "Button",
            () => WorkshopQueryRequested?.Invoke(WorkshopBrowseMode.Popular, "", 1));
        _workshopLatest = LauncherTheme.MakeButton("Newest", "Button",
            () => WorkshopQueryRequested?.Invoke(WorkshopBrowseMode.Latest, "", 1));
        _workshopSubscribed = LauncherTheme.MakeButton("My subscriptions", LauncherTheme.PrimaryButton,
            () => WorkshopQueryRequested?.Invoke(WorkshopBrowseMode.Subscribed, "", 1));

        _workshopScroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new Vector2(0, 240)
        };
        _workshopList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _workshopList.AddThemeConstantOverride("separation", 22);
        _workshopScroll.AddChild(_workshopList);
        page.AddChild(_workshopScroll);
        page.MoveChild(_workshopScroll, _status.GetIndex() + 1);

        _workshopPrevious = LauncherTheme.MakeButton("Previous page", "Button", () =>
            WorkshopQueryRequested?.Invoke(_shownWorkshopMode, _shownWorkshopSearch, Math.Max(1, _shownWorkshopPage - 1)));
        _workshopNext = LauncherTheme.MakeButton("Next page", "Button", () =>
            WorkshopQueryRequested?.Invoke(_shownWorkshopMode, _shownWorkshopSearch, _shownWorkshopPage + 1));
        _workshopDownload = LauncherTheme.MakeButton("Download and import", LauncherTheme.PrimaryButton,
            () => { if (_shownWorkshopItem != 0) WorkshopDownloadRequested?.Invoke(_shownWorkshopItem); });

        foreach (var control in new Control[] { _workshopSearch, _workshopSearchButton, _workshopPopular, _workshopLatest,
            _workshopSubscribed, _workshopPrevious, _workshopNext, _workshopDownload })
            actions.AddChild(control);
        return [_workshopSearch, _workshopSearchButton, _workshopPopular, _workshopLatest, _workshopSubscribed,
            _workshopScroll, _workshopPrevious, _workshopNext, _workshopDownload];
    }

    private WorkshopBrowseMode _shownWorkshopMode = WorkshopBrowseMode.Popular;
    private string _shownWorkshopSearch = "";
    private int _shownWorkshopPage = 1;
    private ulong _shownWorkshopItem;

    private void RequestWorkshopSearch()
    {
        string term = _workshopSearch.Text.Trim();
        WorkshopQueryRequested?.Invoke(term.Length > 0 ? WorkshopBrowseMode.Search : WorkshopBrowseMode.Popular, term, 1);
    }

    private void ClearWorkshopList()
    {
        foreach (Node child in _workshopList.GetChildren())
        {
            _workshopList.RemoveChild(child);
            child.QueueFree();
        }
        _workshopItemButtons.Clear();
    }

    public void ShowWorkshopPage(WorkshopPage page, string? statusOverride = null)
    {
        _shownWorkshopItem = 0;
        _shownWorkshopPage = page.Page;
        _shownWorkshopMode = page.Mode;
        _shownWorkshopSearch = page.Search;
        if (page.Mode == WorkshopBrowseMode.Search) _workshopSearch.Text = page.Search;
        ClearWorkshopList();
        foreach (var item in page.Items)
        {
            ulong id = item.Id;
            var title = LauncherTheme.MakeButton(item.Title, LauncherTheme.QuietButton, () => WorkshopItemRequested?.Invoke(id));
            title.Alignment = HorizontalAlignment.Left;
            title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _workshopList.AddChild(title);
            _workshopItemButtons.Add(title);
            var description = LauncherTheme.Text(
                item.Description.Length > 170 ? item.Description[..170] + "..." : item.Description,
                LauncherTheme.Body, LauncherTheme.SmallSize, LauncherTheme.Mist);
            description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _workshopList.AddChild(description);
            _workshopList.AddChild(new HSeparator());
        }
        if (page.Items.Count == 0)
            _workshopList.AddChild(LauncherTheme.Text("No Workshop items on this page.", LauncherTheme.Body,
                LauncherTheme.SmallSize, LauncherTheme.Mist));
        _workshopPrevious.Disabled = page.Page <= 1;
        _workshopNext.Disabled = page.Items.Count == 0 || (long)page.Page * WorkshopCatalog.PageSize >= page.Total;
        string status = statusOverride ?? $"{page.Mode} · page {page.Page} · {page.Total} Workshop items. Tap a mod to see details.";
        Show("Steam Workshop", status, form: true, _workshopSearch, _workshopSearchButton, _workshopPopular,
            _workshopLatest, _workshopSubscribed, _workshopScroll, _workshopPrevious, _workshopNext, _back);
    }

    public void ShowWorkshopDetails(WorkshopItem item)
    {
        _shownWorkshopItem = item.Id;
        ClearWorkshopList();
        var title = LauncherTheme.Text(item.Title, LauncherTheme.BodyStrong, LauncherTheme.BodySize, LauncherTheme.Bone);
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _workshopList.AddChild(title);
        var info = LauncherTheme.Text(
            $"Workshop ID {item.Id} · {item.Size / 1024:N0} KB · updated {DateTimeOffset.FromUnixTimeSeconds(item.UpdatedAt):yyyy-MM-dd}",
            LauncherTheme.Body, LauncherTheme.SmallSize, LauncherTheme.Mist);
        info.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _workshopList.AddChild(info);
        var description = LauncherTheme.Text(item.Description.Length > 3000 ? item.Description[..3000] + "..." : item.Description,
            LauncherTheme.Body, LauncherTheme.SmallSize, LauncherTheme.Bone);
        description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _workshopList.AddChild(description);
        _workshopList.AddChild(LauncherTheme.Text(
            "Code mods run with this app's access, including its saved Steam sign-in. Only install mods from authors you trust.",
            LauncherTheme.Body, LauncherTheme.SmallSize, LauncherTheme.Mist));
        Show("Workshop item", "Download the original Workshop item and import it using the same ZIP checks as Files.",
            form: true, _workshopScroll, _workshopDownload, _back);
    }
}

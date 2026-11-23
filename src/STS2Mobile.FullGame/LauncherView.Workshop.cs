using System;
using System.Collections.Generic;
using Godot;
using STS2Mobile.Steam.Workshop;

namespace STS2Mobile.FullGame;

public sealed partial class LauncherView
{
    // The launcher already has a page-wide ScrollContainer. Workshop results
    // must live in that scroll, not in a second 240-unit-high scroll viewport.
    private VBoxContainer _workshopPanel = null!;
    private VBoxContainer _workshopFilters = null!;
    private VBoxContainer _workshopList = null!;
    private HBoxContainer _workshopPagination = null!;
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
        _workshopPanel = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _workshopPanel.AddThemeConstantOverride("separation", 20);
        _workshopFilters = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _workshopFilters.AddThemeConstantOverride("separation", 16);

        var searchRow = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        searchRow.AddThemeConstantOverride("separation", 14);
        _workshopSearch = new LineEdit
        {
            PlaceholderText = "Search Workshop",
            CustomMinimumSize = new Vector2(0, 90),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        _workshopSearch.AddThemeFontSizeOverride("font_size", 34);
        _workshopSearch.TextSubmitted += _ => RequestWorkshopSearch();
        _workshopSearchButton = CompactWorkshopButton("Search", "Button", RequestWorkshopSearch);
        _workshopSearchButton.CustomMinimumSize = new Vector2(175, 90);
        searchRow.AddChild(_workshopSearch);
        searchRow.AddChild(_workshopSearchButton);
        _workshopFilters.AddChild(searchRow);

        var categoryRow = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        categoryRow.AddThemeConstantOverride("separation", 12);
        _workshopPopular = CompactWorkshopButton("Popular", "Button",
            () => WorkshopQueryRequested?.Invoke(WorkshopBrowseMode.Popular, "", 1));
        _workshopLatest = CompactWorkshopButton("Newest", "Button",
            () => WorkshopQueryRequested?.Invoke(WorkshopBrowseMode.Latest, "", 1));
        _workshopSubscribed = CompactWorkshopButton("Subscribed", "Button",
            () => WorkshopQueryRequested?.Invoke(WorkshopBrowseMode.Subscribed, "", 1));
        foreach (var button in new[] { _workshopPopular, _workshopLatest, _workshopSubscribed })
        {
            button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            categoryRow.AddChild(button);
        }
        _workshopFilters.AddChild(categoryRow);
        _workshopPanel.AddChild(_workshopFilters);

        _workshopList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _workshopList.AddThemeConstantOverride("separation", 12);
        _workshopPanel.AddChild(_workshopList);

        _workshopPagination = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _workshopPagination.AddThemeConstantOverride("separation", 16);
        _workshopPrevious = CompactWorkshopButton("Previous", "Button", () =>
            WorkshopQueryRequested?.Invoke(_shownWorkshopMode, _shownWorkshopSearch, Math.Max(1, _shownWorkshopPage - 1)));
        _workshopNext = CompactWorkshopButton("Next", "Button", () =>
            WorkshopQueryRequested?.Invoke(_shownWorkshopMode, _shownWorkshopSearch, _shownWorkshopPage + 1));
        foreach (var button in new[] { _workshopPrevious, _workshopNext })
        {
            button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _workshopPagination.AddChild(button);
        }
        _workshopPanel.AddChild(_workshopPagination);

        _workshopDownload = CompactWorkshopButton("Download and import", LauncherTheme.PrimaryButton,
            () => { if (_shownWorkshopItem != 0) WorkshopDownloadRequested?.Invoke(_shownWorkshopItem); });
        _workshopDownload.CustomMinimumSize = new Vector2(0, 115);
        _workshopPanel.AddChild(_workshopDownload);

        page.AddChild(_workshopPanel);
        page.MoveChild(_workshopPanel, _status.GetIndex() + 1);
        return [_workshopPanel];
    }

    private static Button CompactWorkshopButton(string title, string variation, Action pressed)
    {
        var button = LauncherTheme.MakeButton(title, variation, pressed);
        button.CustomMinimumSize = new Vector2(0, 88);
        button.AddThemeFontSizeOverride("font_size", 32);
        button.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return button;
    }

    private static Label WorkshopLabel(string text, Font font, int size, Color color)
    {
        var label = LauncherTheme.Text(text, font, size, color);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return label;
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
        _workshopFilters.Visible = true;
        _workshopPagination.Visible = true;
        _workshopDownload.Visible = false;
        _workshopPopular.ThemeTypeVariation = page.Mode == WorkshopBrowseMode.Popular ? LauncherTheme.PrimaryButton : "Button";
        _workshopLatest.ThemeTypeVariation = page.Mode == WorkshopBrowseMode.Latest ? LauncherTheme.PrimaryButton : "Button";
        _workshopSubscribed.ThemeTypeVariation = page.Mode == WorkshopBrowseMode.Subscribed ? LauncherTheme.PrimaryButton : "Button";
        _workshopSearchButton.ThemeTypeVariation = page.Mode == WorkshopBrowseMode.Search ? LauncherTheme.PrimaryButton : "Button";

        ClearWorkshopList();
        foreach (var item in page.Items)
        {
            ulong id = item.Id;
            var title = CompactWorkshopButton(item.Title, LauncherTheme.QuietButton, () => WorkshopItemRequested?.Invoke(id));
            title.Alignment = HorizontalAlignment.Left;
            title.AddThemeFontSizeOverride("font_size", 36);
            _workshopList.AddChild(title);
            _workshopItemButtons.Add(title);
            if (item.Description.Length > 0)
                _workshopList.AddChild(WorkshopLabel(
                    item.Description.Length > 145 ? item.Description[..145] + "…" : item.Description,
                    LauncherTheme.Body, 29, LauncherTheme.Mist));
            _workshopList.AddChild(new HSeparator());
        }

        if (page.Items.Count == 0)
            _workshopList.AddChild(WorkshopLabel("No Workshop items on this page.", LauncherTheme.Body, 32, LauncherTheme.Mist));
        _workshopPrevious.Disabled = page.Page <= 1;
        _workshopNext.Disabled = page.Items.Count == 0 || (long)page.Page * WorkshopCatalog.PageSize >= page.Total;
        string status = statusOverride ?? $"{page.Mode} · page {page.Page} · {page.Total} Workshop items. Select a mod for details.";
        Show("Steam Workshop", status, form: true, _workshopPanel, _back);
    }

    public void ShowWorkshopDetails(WorkshopItem item)
    {
        _shownWorkshopItem = item.Id;
        _workshopFilters.Visible = false;
        _workshopPagination.Visible = false;
        _workshopDownload.Visible = true;
        ClearWorkshopList();
        _workshopList.AddChild(WorkshopLabel(item.Title, LauncherTheme.BodyStrong, 44, LauncherTheme.Bone));
        _workshopList.AddChild(WorkshopLabel(
            $"Workshop ID {item.Id} · {item.Size / 1024:N0} KB · updated {DateTimeOffset.FromUnixTimeSeconds(item.UpdatedAt):yyyy-MM-dd}",
            LauncherTheme.Figures, 29, LauncherTheme.Mist));
        _workshopList.AddChild(WorkshopLabel(item.Description.Length > 3000 ? item.Description[..3000] + "…" : item.Description,
            LauncherTheme.Body, 32, LauncherTheme.Bone));
        _workshopList.AddChild(WorkshopLabel(
            "Code mods can access app files and saved Steam sign-in data. Only install mods you trust.",
            LauncherTheme.Body, 28, LauncherTheme.Mist));
        Show("Workshop item", "Download this item and check it for an installable mod.",
            form: true, _workshopPanel, _back);
    }
}

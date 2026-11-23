using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using STS2Mobile.Launcher;
using STS2Mobile.Platform;
using STS2Mobile.Steam.Workshop;

namespace STS2Mobile.FullGame;

public partial class Launcher
{
    private bool _workshopOpen;
    private bool _workshopDetailsOpen;
    private bool _workshopBusy;
    private WorkshopPage? _workshopPage;
    private WorkshopItem? _workshopItem;
    private CancellationTokenSource? _workshopCancellation;

    private void WireWorkshop()
    {
        _view.WorkshopRequested += OpenWorkshop;
        _view.WorkshopQueryRequested += (mode, term, page) =>
            BrowseWorkshopAsync(mode, term, page).FireAndForget("Browse Steam Workshop");
        _view.WorkshopItemRequested += id => ShowWorkshopItemAsync(id).FireAndForget("Workshop item details");
        _view.WorkshopDownloadRequested += id => DownloadWorkshopAsync(id).FireAndForget("Workshop download");
    }

    private void OpenWorkshop()
    {
        if (!_login.HasStoredCredentials)
        {
            _view.SetNotice(Notice.Failure("Steam sign-in required", "Sign in to Steam to browse Workshop items and view your subscriptions."));
            ShowMods();
            return;
        }
        _workshopOpen = true;
        _workshopDetailsOpen = false;
        _workshopPage = null;
        BrowseWorkshopAsync(WorkshopBrowseMode.Popular, "", 1).FireAndForget("Browse Steam Workshop");
    }

    private async Task BrowseWorkshopAsync(WorkshopBrowseMode mode, string search, int page)
    {
        if (!_workshopOpen || _workshopBusy) return;
        _workshopBusy = true;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        _workshopCancellation = cancel;
        _workshopDetailsOpen = false;
        _view.SetNotice(null);
        _view.ShowWorkshopPage(new WorkshopPage([], 0, page, mode, search), "Loading Steam Workshop items...");
        try
        {
            var catalog = new WorkshopCatalog(_login.Connection);
            var result = await catalog.BrowseAsync(_login.SteamId, mode, search, page, cancel.Token);
            if (!cancel.IsCancellationRequested && _workshopOpen)
            {
                _workshopPage = result;
                _view.ShowWorkshopPage(result);
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        catch (Exception error)
        {
            GD.PushWarning("Workshop browse failed: " + error.GetType().Name + ": " + error.Message);
            if (_workshopOpen && !cancel.IsCancellationRequested)
            {
                _view.SetNotice(Notice.Failure("Workshop unavailable", error.Message));
                _view.ShowWorkshopPage(_workshopPage ?? new WorkshopPage([], 0, page, mode, search),
                    "Steam did not return Workshop items. Try again.");
            }
        }
        finally
        {
            _workshopBusy = false;
            if (ReferenceEquals(_workshopCancellation, cancel)) _workshopCancellation = null;
        }
    }

    private async Task ShowWorkshopItemAsync(ulong id)
    {
        if (!_workshopOpen || _workshopBusy || _workshopPage == null) return;
        var found = _workshopPage.Items.FirstOrDefault(item => item.Id == id);
        if (found == null) return;
        _workshopItem = found;
        _workshopDetailsOpen = true;
        _view.ShowWorkshopDetails(found);
        _workshopBusy = true;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        _workshopCancellation = cancel;
        try
        {
            var details = await new WorkshopCatalog(_login.Connection).GetAsync(id, cancel.Token);
            if (_workshopOpen && _workshopDetailsOpen && !cancel.IsCancellationRequested)
            {
                _workshopItem = details;
                _view.ShowWorkshopDetails(details);
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        catch (Exception error)
        {
            // The results page already has the title and description, so it is still usable.
            GD.PushWarning("Workshop item details unavailable: " + error.Message);
        }
        finally
        {
            _workshopBusy = false;
            if (ReferenceEquals(_workshopCancellation, cancel)) _workshopCancellation = null;
        }
    }

    private async Task DownloadWorkshopAsync(ulong id)
    {
        if (!_workshopOpen || _workshopBusy || _workshopItem?.Id != id) return;
        _workshopBusy = true;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        _workshopCancellation = cancel;
        string temp = Path.Combine(Path.GetTempPath(), "sts2-workshop-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Downloading only happens when the player explicitly requests it.
            _view.SetNotice(null);
            _view.ShowWorking("Connecting to Steam Workshop...");
            var item = await new WorkshopCatalog(_login.Connection).GetAsync(id, cancel.Token);
            int lastPercent = -1;
            void Progress(long done, long total)
            {
                if (cancel.IsCancellationRequested) return;
                int percent = total > 0 ? (int)Math.Clamp(100.0 * done / total, 0, 100) : 0;
                if (Interlocked.Exchange(ref lastPercent, percent) == percent) return;
                Callable.From(() =>
                {
                    if (!cancel.IsCancellationRequested) _view.ShowWorking($"Downloading {item.Title}: {percent}%");
                }).CallDeferred();
            }
            string downloadedZip = await WorkshopDownload.DownloadZipAsync(_login.Connection, item, temp, Progress, cancel.Token);
            cancel.Token.ThrowIfCancellationRequested();
            _view.ShowWorking("Finding the mod manifest and preparing its ZIP...");
            string installableZip = Path.Combine(temp, "installable.zip");
            var selection = await Task.Run(() => WorkshopModPackage.Normalize(downloadedZip, installableZip), cancel.Token);
            cancel.Token.ThrowIfCancellationRequested();
            GD.Print($"Workshop {id}: selected {selection.Id}, synthesized manifest: {selection.GeneratedManifest}");
            _view.ShowWorking("Importing " + selection.Id + "...");
            ModImportResult? result = await PickedModImport.InstallAsync(installableZip, ModInputs, ConfirmModAsync);
            if (result == null) { ShowWorkshopAfterDownload("Import cancelled.", failure: false); return; }
            if (result.Status == ModImportStatus.Installed)
            {
                _workshopOpen = false;
                ShowReopen($"{item.Title} was downloaded and imported. Close the app and open it again to prepare the mod.");
            }
            else ShowWorkshopAfterDownload(result.Message, failure: true);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        catch (Exception error)
        {
            GD.PushWarning("Workshop download failed: " + error.GetType().Name + ": " + error.Message);
            if (_workshopOpen && !cancel.IsCancellationRequested)
                ShowWorkshopAfterDownload(error.Message, failure: true);
        }
        finally
        {
            _workshopBusy = false;
            if (ReferenceEquals(_workshopCancellation, cancel)) _workshopCancellation = null;
            try { if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true); }
            catch (IOException error) { GD.PushWarning("Workshop temporary files not deleted: " + error.Message); }
        }
    }

    private void ShowWorkshopAfterDownload(string message, bool failure)
    {
        if (!_workshopOpen) return;
        if (failure) _view.SetNotice(Notice.Failure("Workshop mod not installed", message));
        if (_workshopItem != null && _workshopDetailsOpen) _view.ShowWorkshopDetails(_workshopItem);
        else if (_workshopPage != null) _view.ShowWorkshopPage(_workshopPage, message);
    }

    private void BackFromWorkshop()
    {
        _workshopCancellation?.Cancel();
        if (_workshopDetailsOpen && _workshopPage != null)
        {
            _workshopDetailsOpen = false;
            _view.ShowWorkshopPage(_workshopPage);
        }
        else
        {
            _workshopOpen = false;
            _workshopDetailsOpen = false;
            ShowMods();
        }
    }
}

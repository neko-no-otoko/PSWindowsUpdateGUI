using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PSWindowsUpdateGui.Models;
using WUApiLib;

namespace PSWindowsUpdateGui.Services;

internal sealed class WuaWindowsUpdateEngine : IWindowsUpdateEngine
{
    internal const string MicrosoftUpdateServiceId = "7971f918-a847-4430-9279-4a52d1efe18d";
    private const string ClientId = "PSWindowsUpdateGUI/3";
    private readonly StaComWorker _worker = new StaComWorker("PSWindowsUpdateGUI WUA COM");
    private readonly SemaphoreSlim _operationGate = new SemaphoreSlim(1, 1);
    private bool _disposed;

    public Task<IReadOnlyList<UpdateRecord>> ScanAsync(
        ScanRequest request,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        WuaCriteria.ValidateRequest(request);
        return SerializedAsync(token => _worker.InvokeAsync(() => ScanCoreAsync(request, progress, token)), request.TimeoutSeconds, cancellationToken);
    }

    public Task<UpdateActionResult> ExecuteAsync(
        UpdateActionRequest request,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        ValidateAction(request);
        return SerializedAsync(token => _worker.InvokeAsync(() => ExecuteCoreAsync(request, progress, token)), request.TimeoutSeconds, cancellationToken);
    }

    public Task<IReadOnlyList<HistoryRecord>> GetHistoryAsync(int limit, CancellationToken cancellationToken)
    {
        if (limit < 1 || limit > 10000) throw new ArgumentOutOfRangeException(nameof(limit), "History limit must be from 1 through 10,000.");
        return SerializedAsync(token => _worker.InvokeAsync(() => ReadHistory(limit, token)), 300, cancellationToken);
    }

    public Task<UpdateSystemStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        SerializedAsync(token => _worker.InvokeAsync(() => ReadStatus(token)), 60, cancellationToken);

    public Task<IReadOnlyList<UpdateServiceRecord>> GetServicesAsync(CancellationToken cancellationToken) =>
        SerializedAsync(token => _worker.InvokeAsync(() => ReadServices(token)), 60, cancellationToken);

    public Task<string> AddMicrosoftUpdateServiceAsync(bool planOnly, CancellationToken cancellationToken) =>
        SerializedAsync(token => _worker.InvokeAsync(() => AddMicrosoftUpdateService(planOnly, token)), 300, cancellationToken);

    public Task RemoveServiceAsync(string serviceId, bool planOnly, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(serviceId, out _)) throw new FormatException("Service ID must be a GUID.");
        return SerializedAsync(async token =>
        {
            await _worker.InvokeAsync(() =>
            {
                RemoveService(serviceId, planOnly, token);
                return true;
            }).ConfigureAwait(false);
        }, 300, cancellationToken);
    }

    public Task ExportPayloadsAsync(IList<UpdateKey> updates, string destination, CancellationToken cancellationToken)
    {
        if (updates == null || updates.Count == 0) throw new FormatException("At least one update identity is required.");
        if (string.IsNullOrWhiteSpace(destination)) throw new FormatException("A destination directory is required.");
        var fullPath = Path.GetFullPath(destination);
        var root = Path.GetPathRoot(fullPath);
        if (string.Equals(fullPath.TrimEnd(Path.DirectorySeparatorChar), root?.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Exporting update payloads directly to a drive root is not allowed.");
        return SerializedAsync(token => _worker.InvokeAsync(() => ExportPayloadsCoreAsync(updates, fullPath, token)), 3600, cancellationToken);
    }

    private async Task<IReadOnlyList<UpdateRecord>> ScanCoreAsync(
        ScanRequest request,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        IUpdateSession? session = null;
        IUpdateSearcher? searcher = null;
        ISearchJob? job = null;
        ISearchResult? result = null;
        IUpdateServiceManager2? serviceManager = null;
        string? temporaryServiceId = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            session = CreateCom<IUpdateSession>("Microsoft.Update.Session");
            session.ClientApplicationID = ClientId;
            searcher = session.CreateUpdateSearcher();
            serviceManager = ConfigureSource(searcher, request);
            if (request.Source == UpdateSourceKind.Offline) temporaryServiceId = searcher.ServiceID;

            progress?.Report(new UpdateProgress("Searching for applicable updates", 0));
            var completion = new SearchCompletedCallback();
            job = searcher.BeginSearch(WuaCriteria.Build(request), completion, null!);
            using (cancellationToken.Register(() => RequestAbort(job)))
            {
                await completion.Completion.ConfigureAwait(true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            result = searcher.EndSearch(job);
            var resultUpdates = result.Updates;
            List<UpdateRecord> mapped;
            try { mapped = MapUpdates(resultUpdates); }
            finally { Release(resultUpdates); }
            IEnumerable<UpdateRecord> filtered = mapped;
            if (!string.IsNullOrWhiteSpace(request.TitlePattern))
            {
                var regex = new Regex(request.TitlePattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
                filtered = filtered.Where(update => regex.IsMatch(update.Title));
            }

            if (request.KbArticleIds.Count > 0)
            {
                var requested = new HashSet<string>(request.KbArticleIds.Select(NormalizeKb), StringComparer.OrdinalIgnoreCase);
                filtered = filtered.Where(update => update.KbArticleIds.Any(kb => requested.Contains(NormalizeKb(kb))));
            }

            progress?.Report(new UpdateProgress("Search complete", 100));
            return filtered.ToList();
        }
        catch (COMException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        finally
        {
            if (job != null) TryCleanup(job);
            Release(result);
            Release(searcher);
            Release(session);
            if (temporaryServiceId != null && serviceManager != null)
            {
                try { serviceManager.RemoveService(temporaryServiceId); } catch { }
            }
            Release(serviceManager);
        }
    }

    private async Task<UpdateActionResult> ExecuteCoreAsync(
        UpdateActionRequest request,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var operationMutex = new Mutex(false, MachineMutationLock.Name);
        var mutexAcquired = false;
        try
        {
            try { mutexAcquired = operationMutex.WaitOne(TimeSpan.Zero); }
            catch (AbandonedMutexException) { mutexAcquired = true; }
            if (!mutexAcquired)
                throw new InvalidOperationException("Another PSWindowsUpdateGUI update modification is already active on this computer.");

            var selected = await FindSelectedUpdatesAsync(request, progress, cancellationToken).ConfigureAwait(true);
            try
            {
                if (selected.Updates.Count != request.Updates.Count)
                    throw new InvalidOperationException("One or more selected updates are no longer applicable at the requested revision. Scan again.");
                if (request.Action == UpdateActionKind.Uninstall && selected.Updates.Any(update => !update.IsUninstallable))
                    throw new InvalidOperationException("One or more selected updates are not uninstallable through Windows Update Agent. Use an explicitly identified package fallback only when appropriate.");

                var response = new UpdateActionResult { Action = request.Action };
                if (request.PlanOnly)
                {
                    response.Result = "Planned";
                    foreach (var update in selected.Updates)
                        response.Updates.Add(ItemResult(update, "Planned", 0));
                    return response;
                }

                if (request.Action == UpdateActionKind.Hide || request.Action == UpdateActionKind.Unhide)
                {
                    var hidden = request.Action == UpdateActionKind.Hide;
                    foreach (var update in selected.Updates)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        update.IsHidden = hidden;
                        response.Updates.Add(ItemResult(update, hidden ? "Hidden" : "Visible", 0));
                    }
                    response.Result = "Succeeded";
                    return response;
                }

                if (request.Action == UpdateActionKind.Download || request.Action == UpdateActionKind.Install)
                {
                    foreach (var update in selected.Updates.Where(update => !update.EulaAccepted))
                    {
                        if (!request.AcceptEulas)
                            throw new InvalidOperationException($"The license terms for '{update.Title}' have not been accepted. Re-run with explicit EULA acceptance.");
                        update.AcceptEula();
                    }

                    var download = await DownloadAsync(selected.Session, selected.Collection, request.Force, progress, cancellationToken).ConfigureAwait(true);
                    if (request.Action == UpdateActionKind.Download)
                    {
                        return MapDownloadResult(request.Action, selected.Updates, download);
                    }
                    if (download.ResultCode == OperationResultCode.orcFailed || download.ResultCode == OperationResultCode.orcAborted)
                        return MapDownloadResult(request.Action, selected.Updates, download);
                    Release(download);
                }

                return await InstallOrUninstallAsync(selected.Session, selected.Collection, selected.Updates, request, progress, cancellationToken).ConfigureAwait(true);
            }
            finally
            {
                selected.Dispose();
            }
        }
        finally
        {
            if (mutexAcquired) operationMutex.ReleaseMutex();
        }
    }

    private async Task<SelectedUpdates> FindSelectedUpdatesAsync(
        UpdateActionRequest request,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        IUpdateSession? session = null;
        IUpdateSearcher? searcher = null;
        ISearchJob? job = null;
        ISearchResult? result = null;
        try
        {
            session = CreateCom<IUpdateSession>("Microsoft.Update.Session");
            session.ClientApplicationID = ClientId;
            searcher = session.CreateUpdateSearcher();
            ConfigureActionSource(searcher, request.Source, request.ServiceId);
            var installed = request.Action == UpdateActionKind.Uninstall ? 1 : 0;
            var criteria = $"IsInstalled={installed}";
            progress?.Report(new UpdateProgress("Revalidating selected update identities", 0));
            var completion = new SearchCompletedCallback();
            job = searcher.BeginSearch(criteria, completion, null!);
            using (cancellationToken.Register(() => RequestAbort(job))) await completion.Completion.ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            result = searcher.EndSearch(job);

            var collection = CreateCom<UpdateCollection>("Microsoft.Update.UpdateColl");
            var updates = new List<IUpdate>();
            var resultUpdates = result.Updates;
            for (var index = 0; index < resultUpdates.Count; index++)
            {
                var update = resultUpdates[index];
                var identity = update.Identity;
                try
                {
                    if (request.Updates.Any(key => key.Revision == identity.RevisionNumber &&
                        string.Equals(key.UpdateId, identity.UpdateID, StringComparison.OrdinalIgnoreCase)))
                    {
                        collection.Add(update);
                        updates.Add(update);
                    }
                    else
                    {
                        Release(update);
                    }
                }
                finally
                {
                    Release(identity);
                }
            }
            Release(resultUpdates);

            Release(result);
            result = null;
            Release(searcher);
            searcher = null;
            if (job != null) { TryCleanup(job); Release(job); job = null; }
            return new SelectedUpdates(session, collection, updates);
        }
        catch
        {
            if (job != null) TryCleanup(job);
            Release(job);
            Release(result);
            Release(searcher);
            Release(session);
            throw;
        }
    }

    private static async Task<IDownloadResult> DownloadAsync(
        IUpdateSession session,
        UpdateCollection collection,
        bool force,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        IUpdateDownloader? downloader = null;
        IDownloadJob? job = null;
        try
        {
            downloader = session.CreateUpdateDownloader();
            downloader.ClientApplicationID = ClientId;
            downloader.IsForced = force;
            downloader.Updates = collection;
            var completed = new DownloadCompletedCallback();
            var changed = new DownloadProgressCallback(progress);
            job = downloader.BeginDownload(changed, completed, null!);
            using (cancellationToken.Register(() => RequestAbort(job))) await completed.Completion.ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            return downloader.EndDownload(job);
        }
        catch (COMException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        finally
        {
            if (job != null) TryCleanup(job);
            Release(job);
            Release(downloader);
        }
    }

    private static async Task<UpdateActionResult> InstallOrUninstallAsync(
        IUpdateSession session,
        UpdateCollection collection,
        IList<IUpdate> updates,
        UpdateActionRequest request,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        IUpdateInstaller? installer = null;
        IInstallationJob? job = null;
        try
        {
            installer = session.CreateUpdateInstaller();
            if (installer.IsBusy) throw new InvalidOperationException("Windows Update Agent reports that another installer is already active.");
            installer.ClientApplicationID = ClientId;
            installer.AllowSourcePrompts = false;
            installer.IsForced = request.Force;
            installer.Updates = collection;
            var completed = new InstallationCompletedCallback();
            var changed = new InstallationProgressCallback(progress);
            job = request.Action == UpdateActionKind.Uninstall
                ? installer.BeginUninstall(changed, completed, null!)
                : installer.BeginInstall(changed, completed, null!);
            using (cancellationToken.Register(() => RequestAbort(job))) await completed.Completion.ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            var result = request.Action == UpdateActionKind.Uninstall ? installer.EndUninstall(job) : installer.EndInstall(job);
            try
            {
                var response = new UpdateActionResult
                {
                    Action = request.Action,
                    Result = result.ResultCode.ToString(),
                    HResult = result.HResult,
                    RebootRequired = result.RebootRequired
                };
                for (var index = 0; index < updates.Count; index++)
                {
                    var item = result.GetUpdateResult(index);
                    try { response.Updates.Add(ItemResult(updates[index], item.ResultCode.ToString(), item.HResult)); }
                    finally { Release(item); }
                }
                return response;
            }
            finally
            {
                Release(result);
            }
        }
        catch (COMException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        finally
        {
            if (job != null) TryCleanup(job);
            Release(job);
            Release(installer);
        }
    }

    private static UpdateActionResult MapDownloadResult(UpdateActionKind action, IList<IUpdate> updates, IDownloadResult result)
    {
        try
        {
            var response = new UpdateActionResult { Action = action, Result = result.ResultCode.ToString(), HResult = result.HResult };
            for (var index = 0; index < updates.Count; index++)
            {
                var item = result.GetUpdateResult(index);
                try { response.Updates.Add(ItemResult(updates[index], item.ResultCode.ToString(), item.HResult)); }
                finally { Release(item); }
            }
            return response;
        }
        finally
        {
            Release(result);
        }
    }

    private static IReadOnlyList<HistoryRecord> ReadHistory(int limit, CancellationToken cancellationToken)
    {
        IUpdateSession? session = null;
        IUpdateSearcher? searcher = null;
        IUpdateHistoryEntryCollection? history = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            session = CreateCom<IUpdateSession>("Microsoft.Update.Session");
            session.ClientApplicationID = ClientId;
            searcher = session.CreateUpdateSearcher();
            var count = Math.Min(limit, searcher.GetTotalHistoryCount());
            history = searcher.QueryHistory(0, count);
            var output = new List<HistoryRecord>(count);
            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = history[index];
                var identity = item.UpdateIdentity;
                try
                {
                    output.Add(new HistoryRecord
                    {
                        DateUtc = item.Date.ToUniversalTime(),
                        Title = item.Title,
                        Operation = item.Operation.ToString(),
                        Result = item.ResultCode.ToString(),
                        HResult = item.HResult,
                        Client = item.ClientApplicationID,
                        Identity = new UpdateKey { UpdateId = identity.UpdateID, Revision = identity.RevisionNumber }
                    });
                }
                finally { Release(identity); Release(item); }
            }
            return output;
        }
        finally { Release(history); Release(searcher); Release(session); }
    }

    private static UpdateSystemStatus ReadStatus(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ISystemInformation? system = null;
        IWindowsUpdateAgentInfo? agent = null;
        IUpdateSession? session = null;
        IUpdateInstaller? installer = null;
        try
        {
            system = CreateCom<ISystemInformation>("Microsoft.Update.SystemInfo");
            agent = CreateCom<IWindowsUpdateAgentInfo>("Microsoft.Update.AgentInfo");
            session = CreateCom<IUpdateSession>("Microsoft.Update.Session");
            installer = session.CreateUpdateInstaller();
            var serviceStatus = "Unknown";
            try
            {
                using var service = new ServiceController("wuauserv");
                serviceStatus = service.Status.ToString();
            }
            catch { }

            return new UpdateSystemStatus
            {
                ComputerName = Environment.MachineName,
                AgentVersion = ReadAgentVersion(agent),
                RebootRequired = system.RebootRequired,
                InstallerBusy = installer.IsBusy,
                UpdateServiceStatus = serviceStatus,
                LocalTime = DateTimeOffset.Now,
                OrchestratorNotice = "WUA operations are independent of the Windows Settings orchestrator and may not appear there."
            };
        }
        finally { Release(installer); Release(session); Release(agent); Release(system); }
    }

    private static string ReadAgentVersion(IWindowsUpdateAgentInfo agent)
    {
        try { return Convert.ToString(agent.GetInfo("ProductVersionString")) ?? string.Empty; }
        catch (ArgumentException)
        {
            var major = Convert.ToString(agent.GetInfo("ApiMajorVersion")) ?? "?";
            var minor = Convert.ToString(agent.GetInfo("ApiMinorVersion")) ?? "?";
            return major + "." + minor;
        }
    }

    private static IReadOnlyList<UpdateServiceRecord> ReadServices(CancellationToken cancellationToken)
    {
        IUpdateServiceManager2? manager = null;
        IUpdateServiceCollection? services = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            manager = CreateCom<IUpdateServiceManager2>("Microsoft.Update.ServiceManager");
            manager.ClientApplicationID = ClientId;
            services = manager.Services;
            var output = new List<UpdateServiceRecord>(services.Count);
            for (var index = 0; index < services.Count; index++)
            {
                var service = services[index];
                try
                {
                    output.Add(new UpdateServiceRecord
                    {
                        ServiceId = service.ServiceID,
                        Name = service.Name,
                        IsManaged = service.IsManaged,
                        IsRegisteredWithAutomaticUpdates = service.IsRegisteredWithAU,
                        IsScanPackage = service.IsScanPackageService
                    });
                }
                finally { Release(service); }
            }
            return output;
        }
        finally { Release(services); Release(manager); }
    }

    private static string AddMicrosoftUpdateService(bool planOnly, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (planOnly) return $"Would register Microsoft Update service {MicrosoftUpdateServiceId}.";
        IUpdateServiceManager2? manager = null;
        IUpdateServiceRegistration? registration = null;
        try
        {
            manager = CreateCom<IUpdateServiceManager2>("Microsoft.Update.ServiceManager");
            manager.ClientApplicationID = ClientId;
            registration = manager.AddService2(MicrosoftUpdateServiceId, 7, string.Empty);
            return registration.Service.ServiceID;
        }
        finally { Release(registration); Release(manager); }
    }

    private static void RemoveService(string serviceId, bool planOnly, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (planOnly) return;
        IUpdateServiceManager2? manager = null;
        try
        {
            manager = CreateCom<IUpdateServiceManager2>("Microsoft.Update.ServiceManager");
            manager.ClientApplicationID = ClientId;
            manager.RemoveService(serviceId);
        }
        finally { Release(manager); }
    }

    private async Task ExportPayloadsCoreAsync(IList<UpdateKey> keys, string destination, CancellationToken cancellationToken)
    {
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            throw new IOException("The payload export destination must be empty so every exported file can be verified unambiguously.");
        var request = new UpdateActionRequest { Action = UpdateActionKind.Download, Updates = keys };
        var selected = await FindSelectedUpdatesAsync(request, null, cancellationToken).ConfigureAwait(true);
        try
        {
            Directory.CreateDirectory(destination);
            foreach (var update in selected.Updates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!update.IsDownloaded) throw new InvalidOperationException($"'{update.Title}' is not present in the WUA download cache.");
                update.CopyFromCache(destination, false);
            }

            var files = Directory.GetFiles(destination, "*", SearchOption.AllDirectories);
            if (files.Length == 0) throw new InvalidDataException("WUA did not export any cached payload files.");
            var manifest = new List<string>();
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var extension = Path.GetExtension(file);
                if (extension.Equals(".cab", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".msu", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".msi", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".dll", StringComparison.OrdinalIgnoreCase))
                    AuthenticodeVerifier.VerifyOrThrow(file);
                using var stream = File.OpenRead(file);
                using var sha = SHA256.Create();
                manifest.Add(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant() + "  " + Path.GetFileName(file));
            }
            File.WriteAllLines(Path.Combine(destination, "payloads.sha256"), manifest);
        }
        finally { selected.Dispose(); }
    }

    private static IUpdateServiceManager2? ConfigureSource(IUpdateSearcher searcher, ScanRequest request)
    {
        if (request.Source == UpdateSourceKind.Default) searcher.ServerSelection = ServerSelection.ssDefault;
        else if (request.Source == UpdateSourceKind.WindowsUpdate) searcher.ServerSelection = ServerSelection.ssWindowsUpdate;
        else if (request.Source == UpdateSourceKind.ManagedServer) searcher.ServerSelection = ServerSelection.ssManagedServer;
        else if (request.Source == UpdateSourceKind.MicrosoftUpdate)
        {
            searcher.ServerSelection = ServerSelection.ssOthers;
            searcher.ServiceID = MicrosoftUpdateServiceId;
        }
        else if (request.Source == UpdateSourceKind.Service)
        {
            searcher.ServerSelection = ServerSelection.ssOthers;
            searcher.ServiceID = request.ServiceId;
        }
        else
        {
            var cab = Path.GetFullPath(request.OfflineCabPath);
            if (!File.Exists(cab)) throw new FileNotFoundException("Offline scan CAB was not found.", cab);
            var manager = CreateCom<IUpdateServiceManager2>("Microsoft.Update.ServiceManager");
            manager.ClientApplicationID = ClientId;
            var service = manager.AddScanPackageService("PSWindowsUpdateGUI Offline Scan", cab, 0);
            try
            {
                searcher.ServerSelection = ServerSelection.ssOthers;
                searcher.ServiceID = service.ServiceID;
            }
            finally { Release(service); }
            return manager;
        }
        return null;
    }

    private static void ConfigureActionSource(IUpdateSearcher searcher, UpdateSourceKind source, string serviceId)
    {
        if (source == UpdateSourceKind.Offline) throw new InvalidOperationException("Offline scan catalogs contain metadata only and cannot download or install updates.");
        if (source == UpdateSourceKind.Default) searcher.ServerSelection = ServerSelection.ssDefault;
        else if (source == UpdateSourceKind.WindowsUpdate) searcher.ServerSelection = ServerSelection.ssWindowsUpdate;
        else if (source == UpdateSourceKind.ManagedServer) searcher.ServerSelection = ServerSelection.ssManagedServer;
        else
        {
            searcher.ServerSelection = ServerSelection.ssOthers;
            searcher.ServiceID = source == UpdateSourceKind.MicrosoftUpdate ? MicrosoftUpdateServiceId : serviceId;
        }
    }

    private static List<UpdateRecord> MapUpdates(UpdateCollection updates)
    {
        var output = new List<UpdateRecord>(updates.Count);
        for (var index = 0; index < updates.Count; index++)
        {
            var update = updates[index];
            try { output.Add(MapUpdate(update)); }
            finally { Release(update); }
        }
        return output;
    }

    private static UpdateRecord MapUpdate(IUpdate update)
    {
        var identity = update.Identity;
        var behavior = update.InstallationBehavior;
        try
        {
            var record = new UpdateRecord
            {
                Identity = new UpdateKey { UpdateId = identity.UpdateID, Revision = identity.RevisionNumber },
                Title = update.Title,
                Description = update.Description,
                Type = update.Type.ToString().Replace("ut", string.Empty),
                KbArticleIds = ReadStrings(update.KBArticleIDs),
                Categories = ReadCategories(update.Categories),
                MinimumDownloadBytes = update.MinDownloadSize,
                MaximumDownloadBytes = update.MaxDownloadSize,
                IsDownloaded = update.IsDownloaded,
                IsInstalled = update.IsInstalled,
                IsHidden = update.IsHidden,
                IsUninstallable = update.IsUninstallable,
                EulaAccepted = update.EulaAccepted,
                RebootMayBeRequired = behavior.RebootBehavior.ToString() != "irbNeverReboots",
                Severity = update.MsrcSeverity ?? string.Empty
            };
            if (update.Type == UpdateType.utDriver && update is IWindowsDriverUpdate driver)
            {
                record.DriverProvider = driver.DriverProvider ?? string.Empty;
                record.DriverManufacturer = driver.DriverManufacturer ?? string.Empty;
                record.DriverClass = driver.DriverClass ?? string.Empty;
                record.DriverModel = driver.DriverModel ?? string.Empty;
                record.DriverDateUtc = driver.DriverVerDate.ToUniversalTime();
                record.DriverVersion = ExtractDriverVersion(update.Title);
            }
            return record;
        }
        finally { Release(behavior); Release(identity); }
    }

    private static IList<string> ReadStrings(StringCollection values)
    {
        try
        {
            var output = new List<string>(values.Count);
            for (var index = 0; index < values.Count; index++) output.Add(values[index]);
            return output;
        }
        finally { Release(values); }
    }

    private static IList<string> ReadCategories(ICategoryCollection categories)
    {
        try
        {
            var output = new List<string>(categories.Count);
            for (var index = 0; index < categories.Count; index++)
            {
                var category = categories[index];
                try { output.Add(category.Name); }
                finally { Release(category); }
            }
            return output;
        }
        finally { Release(categories); }
    }

    private static string ExtractDriverVersion(string title)
    {
        var matches = Regex.Matches(title ?? string.Empty, @"\b\d+(?:\.\d+){2,4}\b");
        return matches.Count == 0 ? string.Empty : matches[matches.Count - 1].Value;
    }

    private static UpdateItemResult ItemResult(IUpdate update, string result, int hresult)
    {
        var identity = update.Identity;
        try
        {
            return new UpdateItemResult
            {
                Identity = new UpdateKey { UpdateId = identity.UpdateID, Revision = identity.RevisionNumber },
                Title = update.Title,
                Result = result,
                HResult = hresult
            };
        }
        finally { Release(identity); }
    }

    private static string NormalizeKb(string value) => value.Trim().Replace("KB", string.Empty).TrimStart('0');

    private static T CreateCom<T>(string progId) where T : class
    {
        var type = Type.GetTypeFromProgID(progId, true)!;
        return (T)(Activator.CreateInstance(type) ?? throw new InvalidOperationException($"Could not create {progId}."));
    }

    private async Task<T> SerializedAsync<T>(Func<CancellationToken, Task<T>> operation, int timeoutSeconds, CancellationToken cancellationToken)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WuaWindowsUpdateEngine));
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try { return await operation(timeout.Token).ConfigureAwait(false); }
        finally { _operationGate.Release(); }
    }

    private async Task SerializedAsync(Func<CancellationToken, Task> operation, int timeoutSeconds, CancellationToken cancellationToken)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WuaWindowsUpdateEngine));
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try { await operation(timeout.Token).ConfigureAwait(false); }
        finally { _operationGate.Release(); }
    }

    private static void ValidateAction(UpdateActionRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (!Enum.IsDefined(typeof(UpdateActionKind), request.Action) || !Enum.IsDefined(typeof(UpdateSourceKind), request.Source))
            throw new FormatException("The update action or source is invalid.");
        if (request.Updates == null || request.Updates.Count == 0) throw new FormatException("At least one update identity is required.");
        if (request.Updates.Count != request.Updates.Distinct().Count()) throw new FormatException("Duplicate update identities are not allowed.");
        if (request.TimeoutSeconds < 5 || request.TimeoutSeconds > 86400) throw new FormatException("Timeout must be between 5 and 86,400 seconds.");
        if (request.Source == UpdateSourceKind.Offline) throw new FormatException("Offline scan catalogs cannot download, install, or modify updates.");
        if (request.Source == UpdateSourceKind.Service && !Guid.TryParse(request.ServiceId, out _)) throw new FormatException("A valid update service GUID is required.");
    }

    private static void RequestAbort(object? job)
    {
        try
        {
            if (job is ISearchJob search) search.RequestAbort();
            else if (job is IDownloadJob download) download.RequestAbort();
            else if (job is IInstallationJob install) install.RequestAbort();
        }
        catch { }
    }

    private static void TryCleanup(object job)
    {
        try
        {
            if (job is ISearchJob search) search.CleanUp();
            else if (job is IDownloadJob download) download.CleanUp();
            else if (job is IInstallationJob install) install.CleanUp();
        }
        catch { }
    }

    private static void Release(object? value)
    {
        if (value != null && Marshal.IsComObject(value))
        {
            try { Marshal.FinalReleaseComObject(value); } catch { }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _worker.Dispose();
        _operationGate.Dispose();
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class SearchCompletedCallback : ISearchCompletedCallback
    {
        private readonly TaskCompletionSource<bool> _completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Completion => _completion.Task;
        public void Invoke(ISearchJob searchJob, ISearchCompletedCallbackArgs callbackArgs) => _completion.TrySetResult(true);
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class DownloadCompletedCallback : IDownloadCompletedCallback
    {
        private readonly TaskCompletionSource<bool> _completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Completion => _completion.Task;
        public void Invoke(IDownloadJob downloadJob, IDownloadCompletedCallbackArgs callbackArgs) => _completion.TrySetResult(true);
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class InstallationCompletedCallback : IInstallationCompletedCallback
    {
        private readonly TaskCompletionSource<bool> _completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Completion => _completion.Task;
        public void Invoke(IInstallationJob installationJob, IInstallationCompletedCallbackArgs callbackArgs) => _completion.TrySetResult(true);
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class DownloadProgressCallback : IDownloadProgressChangedCallback
    {
        private readonly IProgress<UpdateProgress>? _progress;
        public DownloadProgressCallback(IProgress<UpdateProgress>? progress) => _progress = progress;
        public void Invoke(IDownloadJob downloadJob, IDownloadProgressChangedCallbackArgs callbackArgs)
        {
            var value = callbackArgs.Progress;
            try { _progress?.Report(new UpdateProgress("Downloading updates", value.PercentComplete, value.CurrentUpdateIndex)); }
            finally { Release(value); }
        }
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class InstallationProgressCallback : IInstallationProgressChangedCallback
    {
        private readonly IProgress<UpdateProgress>? _progress;
        public InstallationProgressCallback(IProgress<UpdateProgress>? progress) => _progress = progress;
        public void Invoke(IInstallationJob installationJob, IInstallationProgressChangedCallbackArgs callbackArgs)
        {
            var value = callbackArgs.Progress;
            try { _progress?.Report(new UpdateProgress("Applying updates", value.PercentComplete, value.CurrentUpdateIndex)); }
            finally { Release(value); }
        }
    }

    private sealed class SelectedUpdates : IDisposable
    {
        public SelectedUpdates(IUpdateSession session, UpdateCollection collection, IList<IUpdate> updates)
        {
            Session = session;
            Collection = collection;
            Updates = updates;
        }

        public IUpdateSession Session { get; }
        public UpdateCollection Collection { get; }
        public IList<IUpdate> Updates { get; }

        public void Dispose()
        {
            foreach (var update in Updates) Release(update);
            Release(Collection);
            Release(Session);
        }
    }
}

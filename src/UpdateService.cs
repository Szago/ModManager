using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using ModManager.Updates;
using Newtonsoft.Json.Linq;

namespace ModManager
{
    internal sealed class UpdateState
    {
        internal string Guid, Version, Location;
        internal ReleaseSource Source;
        internal string Status = "Not checked";
        internal string Latest, DownloadUrl, Digest;
        internal long Size;
        internal bool Available, Staged, Current;
        internal DateTime LastCheck;
    }

    internal sealed class UpdateService
    {
        private readonly Dictionary<string, UpdateState> _states = new Dictionary<string, UpdateState>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentQueue<Action> _completed = new ConcurrentQueue<Action>();
        private readonly ManualLogSource _log;
        private readonly HttpClient _http;
        private DateTime _rateLimitUntil;
        internal bool Busy { get; private set; }
        internal int Revision { get; private set; }
        internal string Message { get; private set; }
        private const long MaxDllBytes = 64 * 1024 * 1024;
        private string QueueRoot => Path.Combine(Paths.BepInExRootPath, "ModManagerUpdates");

        internal UpdateService(ManualLogSource log, HttpClient http = null, bool installStartup = true)
        {
            _log = log;
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
            _http.MaxResponseContentBufferSize = 2 * 1024 * 1024;
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Eros-ModManager/" + Plugin.PluginVersion);
            _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            if (installStartup) EnsureStartupInstaller();
        }

        // The one-file ModManager release carries its startup component as a resource.
        private void EnsureStartupInstaller()
        {
            try
            {
                string folder = Path.Combine(Paths.BepInExRootPath, "patchers", "ModManager");
                string target = Path.Combine(folder, "ModManager.Updater.dll");
                if (File.Exists(target)) return;
                Directory.CreateDirectory(folder);
                using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("ModManager.Updater.dll"))
                {
                    if (source == null) throw new IOException("Bundled startup installer is missing.");
                    string temp = target + ".tmp";
                    using (var output = File.Create(temp)) source.CopyTo(output);
                    File.Move(temp, target);
                }
            }
            catch (Exception exception)
            {
                _log.LogError("[ModManager] Could not install the startup updater: " + exception);
            }
        }

        internal UpdateState Get(string guid)
        {
            if (_states.TryGetValue(guid, out UpdateState state)) return state;
            if (!Chainloader.PluginInfos.TryGetValue(guid, out PluginInfo info)) return null;
            state = new UpdateState
            {
                Guid = guid, Version = info.Metadata.Version.ToString(), Location = info.Location,
                Source = ModManagerApi.GetReleaseSource(guid)
            };
            if (state.Source == null) state.Status = "Not configured";
            try
            {
                if (Directory.Exists(QueueRoot))
                    foreach (string folder in Directory.GetDirectories(QueueRoot))
                    {
                        string manifest = Path.Combine(folder, "pending.xml");
                        if (!File.Exists(manifest)) continue;
                        PendingUpdate pending = PendingUpdate.Load(manifest);
                        if (!string.Equals(pending.Guid, guid, StringComparison.OrdinalIgnoreCase)) continue;
                        state.Staged = true;
                        state.Status = "Restart to update";
                    }
            }
            catch (Exception exception) { _log.LogWarning("[ModManager] Could not read pending update status: " + exception.Message); }
            _states.Add(guid, state);
            return state;
        }

        internal void Tick()
        {
            while (_completed.TryDequeue(out Action action)) { action(); Revision++; }
        }

        internal void Check(IEnumerable<string> guids, bool install)
        {
            if (Busy) return;
            UpdateState[] targets = guids.Distinct().Select(Get).Where(s => s != null && s.Source != null && !s.Staged).ToArray();
            if (targets.Length == 0) { Message = "No published release sources configured, or updates already downloaded."; Revision++; return; }
            Busy = true;
            Message = install ? "Checking and downloading updates..." : "Checking for updates...";
            Revision++;
            // No Unity calls from worker threads. Changes are delivered by the persistent render hook.
            Task.Run(async () =>
            {
                int failures = 0, staged = 0;
                foreach (UpdateState target in targets)
                {
                    try
                    {
                        _completed.Enqueue(() => target.Status = "Checking...");
                        UpdateState result = await CheckOne(target).ConfigureAwait(false);
                        if (install && result.Available)
                        {
                            _completed.Enqueue(() => target.Status = "Downloading...");
                            await Stage(result).ConfigureAwait(false);
                            result.Staged = true;
                            result.Available = false;
                            result.Status = "Restart to update";
                            staged++;
                        }
                        _completed.Enqueue(() => _states[target.Guid] = result);
                    }
                    catch (Exception exception)
                    {
                        failures++;
                        string status = exception.Message;
                        _log.LogWarning("[ModManager] Update failed for " + target.Guid + ": " + exception);
                        _completed.Enqueue(() =>
                        {
                            target.Current = false;
                            target.Available = false;
                            target.LastCheck = default(DateTime);
                            target.Status = status;
                        });
                    }
                }
                string message = failures > 0
                    ? "Finished with " + failures + " update error(s). See each mod's status."
                    : staged > 0 ? "Downloaded " + staged + " update(s). Restart the game to apply them."
                    : "Update check complete.";
                _completed.Enqueue(() => { Busy = false; Message = message; });
            });
        }

        private async Task<UpdateState> CheckOne(UpdateState target)
        {
            if (DateTime.UtcNow < _rateLimitUntil) throw new IOException("GitHub rate limit; try later");
            // Repeated refresh clicks should not exhaust GitHub's unauthenticated quota.
            if (target.LastCheck > DateTime.UtcNow.AddMinutes(-1))
                return CopyResult(target);
            string endpoint = "https://api.github.com/repos/" + target.Source.Repository + "/releases/latest";
            using (HttpResponseMessage response = await _http.GetAsync(endpoint).ConfigureAwait(false))
            {
                if (response.StatusCode == HttpStatusCode.Forbidden || (int)response.StatusCode == 429)
                {
                    _rateLimitUntil = DateTime.UtcNow.AddMinutes(5);
                    if (response.Headers.TryGetValues("X-RateLimit-Reset", out IEnumerable<string> values) &&
                        long.TryParse(values.FirstOrDefault(), out long reset))
                        _rateLimitUntil = DateTimeOffset.FromUnixTimeSeconds(reset).UtcDateTime;
                    throw new IOException("GitHub rate limit; try later");
                }
                if (response.StatusCode == HttpStatusCode.NotFound) throw new IOException("No public release found");
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (json.Length > 2 * 1024 * 1024) throw new InvalidDataException("Release response too large");
                JObject release = JObject.Parse(json);
                if ((bool?)release["draft"] == true || (bool?)release["prerelease"] == true)
                    throw new InvalidDataException("No stable release found");
                string latest = (string)release["tag_name"];
                bool newer = PendingUpdate.ParseVersion(latest) > PendingUpdate.ParseVersion(target.Version);
                var result = new UpdateState
                {
                    Guid = target.Guid, Version = target.Version, Location = target.Location, Source = target.Source,
                    Latest = latest, Available = newer, Current = !newer,
                    Status = newer ? "Out of date" : "Up to date", LastCheck = DateTime.UtcNow
                };
                if (!newer) return result;
                JToken[] matches = (release["assets"] as JArray ?? new JArray()).Where(a =>
                    string.Equals((string)a["name"], target.Source.AssetName, StringComparison.Ordinal)).ToArray();
                if (matches.Length != 1) throw new InvalidDataException("Release DLL missing or ambiguous");
                result.DownloadUrl = (string)matches[0]["browser_download_url"];
                result.Digest = (string)matches[0]["digest"];
                result.Size = (long?)matches[0]["size"] ?? 0;
                if (result.Size <= 0 || result.Size > MaxDllBytes) throw new InvalidDataException("Release DLL size is invalid");
                if (!Uri.TryCreate(result.DownloadUrl, UriKind.Absolute, out Uri download) ||
                    download.Scheme != "https" || download.Host != "github.com" || !download.IsDefaultPort || download.UserInfo.Length != 0 ||
                    !download.AbsolutePath.StartsWith("/" + target.Source.Repository + "/releases/download/", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Unexpected release download URL");
                return result;
            }
        }

        private static UpdateState CopyResult(UpdateState source) => new UpdateState
        {
            Guid = source.Guid, Version = source.Version, Location = source.Location, Source = source.Source,
            Latest = source.Latest, Available = source.Available, Current = source.Current,
            DownloadUrl = source.DownloadUrl, Digest = source.Digest, Size = source.Size,
            LastCheck = source.LastCheck, Status = source.Available ? "Out of date" : "Up to date"
        };

        private async Task Stage(UpdateState state)
        {
            if (!File.Exists(Path.Combine(Paths.BepInExRootPath, "patchers", "ModManager", "ModManager.Updater.dll")))
                throw new IOException("Startup updater unavailable");
            string pluginRoot = Path.GetFullPath(Paths.PluginPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string target = Path.GetFullPath(state.Location);
            if (!target.StartsWith(pluginRoot, StringComparison.OrdinalIgnoreCase)) throw new IOException("Plugin is outside plugins folder");
            string relative = target.Substring(pluginRoot.Length);
            PendingUpdate.ResolveTarget(Paths.PluginPath, relative);
            var pending = new PendingUpdate
            {
                Guid = state.Guid, RelativeTarget = relative, Version = state.Latest,
                OriginalHash = PendingUpdate.Hash(target)
            };
            string folder = Path.Combine(QueueRoot, System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string payload = Path.Combine(folder, "payload.bin");
            try
            {
            using (HttpResponseMessage response = await _http.GetAsync(state.DownloadUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                using (Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var output = File.Create(payload))
                {
                    byte[] buffer = new byte[65536];
                    long total = 0;
                    using (var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(90)))
                    {
                        int read;
                        while ((read = await input.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false)) > 0)
                        {
                            total += read;
                            if (total > MaxDllBytes || total > state.Size) throw new IOException("Download exceeds release size");
                            await output.WriteAsync(buffer, 0, read, timeout.Token).ConfigureAwait(false);
                        }
                    }
                    if (total != state.Size) throw new IOException("Incomplete DLL download");
                }
            }
            pending.DownloadHash = PendingUpdate.Hash(payload);
            if (!string.IsNullOrEmpty(state.Digest) && !string.Equals(state.Digest, "sha256:" + pending.DownloadHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Release checksum mismatch");
            using (var original = Mono.Cecil.AssemblyDefinition.ReadAssembly(target))
                PendingUpdate.ValidateAssembly(payload, state.Guid, state.Latest, original.Name.Name);
            pending.Save(Path.Combine(folder, "manifest.tmp"));
            File.Move(Path.Combine(folder, "manifest.tmp"), Path.Combine(folder, "pending.xml"));
            }
            finally
            {
                if (!File.Exists(Path.Combine(folder, "pending.xml")))
                {
                    // An incomplete/invalid download must never become installable.
                    File.Delete(payload);
                    File.Delete(Path.Combine(folder, "manifest.tmp"));
                    Directory.Delete(folder, false);
                }
            }
        }
    }
}

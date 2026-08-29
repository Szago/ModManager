using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;

namespace ModManager
{
    internal sealed class ManagedMod
    {
        internal string Guid;
        internal string Name;
        internal string Version;
        internal string EnabledPath;
        internal string DisabledPath;
        internal bool ActiveThisSession;
        internal bool DesiredEnabled;
        internal bool Pending => ActiveThisSession != DesiredEnabled;
    }

    internal sealed class ModRegistry
    {
        private const string RegistryFileName = "ModManager.registry.tsv";
        private const string PendingFileName = "ModManager.pending.tsv";
        private const string HelperFileName = "ModManager.FileApplier.exe";

        private readonly ManualLogSource _logger;
        private readonly Dictionary<string, ManagedMod> _mods =
            new Dictionary<string, ManagedMod>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FileMove> _pending =
            new Dictionary<string, FileMove>(StringComparer.OrdinalIgnoreCase);
        private bool _helperStarted;

        internal ModRegistry(ManualLogSource logger)
        {
            _logger = logger;
        }

        internal IReadOnlyList<ManagedMod> Mods =>
            _mods.Values.OrderBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase).ToList();

        private string RegistryPath => Path.Combine(Paths.ConfigPath, RegistryFileName);
        private string PendingPath => Path.Combine(Paths.ConfigPath, PendingFileName);
        private string HelperPath => Path.Combine(Paths.PluginPath, HelperFileName);

        internal void Discover()
        {
            Dictionary<string, ManagedMod> saved = LoadRegistry();
            _mods.Clear();

            foreach (var pair in Chainloader.PluginInfos)
            {
                var info = pair.Value;
                string guid = info.Metadata.GUID;
                string location = Path.GetFullPath(info.Location);
                if (string.Equals(guid, Plugin.PluginGuid, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(location), "ModManager.dll", StringComparison.OrdinalIgnoreCase))
                    continue;

                ManagedMod mod = saved.TryGetValue(guid, out ManagedMod existing)
                    ? existing
                    : new ManagedMod();
                mod.Guid = guid;
                mod.Name = info.Metadata.Name;
                mod.Version = info.Metadata.Version?.ToString() ?? "";
                mod.EnabledPath = location;
                mod.DisabledPath = location + ".disabled";
                mod.ActiveThisSession = true;
                if (!_pending.ContainsKey(guid))
                    mod.DesiredEnabled = File.Exists(mod.EnabledPath);
                _mods[guid] = mod;
            }

            foreach (ManagedMod savedMod in saved.Values)
            {
                if (_mods.ContainsKey(savedMod.Guid) ||
                    string.Equals(savedMod.Guid, Plugin.PluginGuid, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (File.Exists(savedMod.DisabledPath) || File.Exists(savedMod.EnabledPath))
                {
                    savedMod.ActiveThisSession = false;
                    savedMod.DesiredEnabled = File.Exists(savedMod.EnabledPath);
                    _mods[savedMod.Guid] = savedMod;
                }
            }

            LoadPending();
            SaveRegistry();
        }

        internal string SetDesiredState(ManagedMod mod, bool enabled)
        {
            if (mod == null || !_mods.ContainsKey(mod.Guid))
                return "That mod is no longer available.";

            mod.DesiredEnabled = enabled;
            string source = enabled ? mod.DisabledPath : mod.EnabledPath;
            string destination = enabled ? mod.EnabledPath : mod.DisabledPath;

            if (TryMoveNow(source, destination))
            {
                _pending.Remove(mod.Guid);
                SavePending();
                SaveRegistry();
                _logger.LogInfo($"[ModManager] {mod.Name} will be {(enabled ? "enabled" : "disabled")} after restart.");
                return "Change staged. It will apply after the game restarts.";
            }

            _pending[mod.Guid] = new FileMove(source, destination);
            SavePending();
            SaveRegistry();
            EnsureHelperRunning();
            _logger.LogInfo($"[ModManager] Queued {mod.Name} for {(enabled ? "enable" : "disable")} after shutdown.");
            return "Change queued. It will apply after the game fully closes and restarts.";
        }

        private static bool TryMoveNow(string source, string destination)
        {
            try
            {
                if (!File.Exists(source))
                    return File.Exists(destination);
                if (File.Exists(destination))
                    return false;
                File.Move(source, destination);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private void EnsureHelperRunning()
        {
            if (_helperStarted || _pending.Count == 0)
                return;
            if (!File.Exists(HelperPath))
            {
                _logger.LogError("[ModManager] File helper is missing: " + HelperPath);
                return;
            }

            try
            {
                string encodedPath = Convert.ToBase64String(Encoding.UTF8.GetBytes(PendingPath));
                var startInfo = new ProcessStartInfo
                {
                    FileName = HelperPath,
                    Arguments = Process.GetCurrentProcess().Id + " " + encodedPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(startInfo);
                _helperStarted = true;
            }
            catch (Exception exception)
            {
                _logger.LogError("[ModManager] Could not start the restart helper: " + exception);
            }
        }

        private Dictionary<string, ManagedMod> LoadRegistry()
        {
            var result = new Dictionary<string, ManagedMod>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(RegistryPath))
                return result;

            try
            {
                foreach (string line in File.ReadAllLines(RegistryPath, Encoding.UTF8))
                {
                    string[] fields = line.Split('\t');
                    if (fields.Length != 6)
                        continue;

                    var mod = new ManagedMod
                    {
                        Guid = Decode(fields[0]),
                        Name = Decode(fields[1]),
                        Version = Decode(fields[2]),
                        EnabledPath = Decode(fields[3]),
                        DisabledPath = Decode(fields[4]),
                        DesiredEnabled = fields[5] == "1"
                    };

                    if (!string.IsNullOrEmpty(mod.Guid) &&
                        IsManagedPath(mod.EnabledPath, mod.DisabledPath))
                        result[mod.Guid] = mod;
                }
            }
            catch (Exception exception)
            {
                _logger.LogError("[ModManager] Could not read the mod registry: " + exception);
            }

            return result;
        }

        private void SaveRegistry()
        {
            try
            {
                Directory.CreateDirectory(Paths.ConfigPath);
                string[] lines = _mods.Values
                    .Where(mod => !string.Equals(
                        mod.Guid, Plugin.PluginGuid, StringComparison.OrdinalIgnoreCase))
                    .Select(mod => string.Join("\t",
                        Encode(mod.Guid),
                        Encode(mod.Name),
                        Encode(mod.Version),
                        Encode(mod.EnabledPath),
                        Encode(mod.DisabledPath),
                        mod.DesiredEnabled ? "1" : "0"))
                    .ToArray();
                File.WriteAllLines(RegistryPath, lines, new UTF8Encoding(false));
            }
            catch (Exception exception)
            {
                _logger.LogError("[ModManager] Could not save the mod registry: " + exception);
            }
        }

        private void LoadPending()
        {
            _pending.Clear();
            if (!File.Exists(PendingPath))
                return;

            try
            {
                foreach (string line in File.ReadAllLines(PendingPath, Encoding.UTF8))
                {
                    string[] fields = line.Split('\t');
                    if (fields.Length != 2)
                        continue;

                    string source = Decode(fields[0]);
                    string destination = Decode(fields[1]);
                    ManagedMod mod = _mods.Values.FirstOrDefault(item =>
                        PathsEqual(item.EnabledPath, source) ||
                        PathsEqual(item.DisabledPath, source));
                    if (mod == null || !IsManagedPath(source, destination))
                        continue;

                    _pending[mod.Guid] = new FileMove(source, destination);
                    mod.DesiredEnabled = PathsEqual(destination, mod.EnabledPath);
                }

                if (_pending.Count > 0)
                    EnsureHelperRunning();
                else
                    File.Delete(PendingPath);
            }
            catch (Exception exception)
            {
                _logger.LogError("[ModManager] Could not read pending changes: " + exception);
            }
        }

        private void SavePending()
        {
            try
            {
                if (_pending.Count == 0)
                {
                    if (File.Exists(PendingPath))
                        File.Delete(PendingPath);
                    return;
                }

                Directory.CreateDirectory(Paths.ConfigPath);
                string[] lines = _pending.Values
                    .Where(move => IsManagedPath(move.Source, move.Destination))
                    .Select(move => Encode(move.Source) + "\t" + Encode(move.Destination))
                    .ToArray();
                File.WriteAllLines(PendingPath, lines, new UTF8Encoding(false));
            }
            catch (Exception exception)
            {
                _logger.LogError("[ModManager] Could not save pending changes: " + exception);
            }
        }

        private static bool IsManagedPath(string first, string second)
        {
            if (string.IsNullOrEmpty(first) || string.IsNullOrEmpty(second))
                return false;

            string root = Path.GetFullPath(Paths.PluginPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            string firstFull = Path.GetFullPath(first);
            string secondFull = Path.GetFullPath(second);
            return firstFull.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                   secondFull.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                   (firstFull.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                    firstFull.EndsWith(".dll.disabled", StringComparison.OrdinalIgnoreCase)) &&
                   (secondFull.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                    secondFull.EndsWith(".dll.disabled", StringComparison.OrdinalIgnoreCase));
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string Encode(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? ""));
        }

        private static string Decode(string value)
        {
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(value)); }
            catch { return ""; }
        }

        private sealed class FileMove
        {
            internal FileMove(string source, string destination)
            {
                Source = source;
                Destination = destination;
            }

            internal string Source { get; }
            internal string Destination { get; }
        }
    }
}

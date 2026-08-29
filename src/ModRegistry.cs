using System;
using System.Collections.Generic;
using System.Linq;
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
        internal bool ActiveThisSession;
        internal bool DesiredEnabled;
        internal bool Pending => ActiveThisSession != DesiredEnabled;
    }

    internal sealed class ModRegistry
    {
        private readonly ManualLogSource _logger;
        private readonly Dictionary<string, ManagedMod> _mods =
            new Dictionary<string, ManagedMod>(StringComparer.OrdinalIgnoreCase);

        internal ModRegistry(ManualLogSource logger)
        {
            _logger = logger;
        }

        internal IReadOnlyList<ManagedMod> Mods =>
            _mods.Values
                .OrderBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

        internal void Discover()
        {
            _mods.Clear();
            foreach (var pair in Chainloader.PluginInfos)
            {
                var info = pair.Value;
                string guid = info.Metadata.GUID;
                if (string.Equals(
                    guid,
                    Plugin.PluginGuid,
                    StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!UsesModManagerApi(info))
                    continue;

                _mods[guid] = new ManagedMod
                {
                    Guid = guid,
                    Name = info.Metadata.Name,
                    Version = info.Metadata.Version?.ToString() ?? "",
                    ActiveThisSession = ModManagerApi.IsEnabled(guid),
                    DesiredEnabled = ModManagerApi.GetConfiguredState(guid)
                };
            }

            _logger.LogInfo(
                "[ModManager] Discovered " + _mods.Count +
                " API-integrated mod(s). State file: " +
                ModManagerApi.StateFilePath);
        }

        internal string SetDesiredState(ManagedMod mod, bool enabled)
        {
            if (mod == null || !_mods.ContainsKey(mod.Guid))
                return "That mod is no longer available.";

            ModManagerApi.SetConfiguredState(mod.Guid, enabled);
            mod.DesiredEnabled = enabled;
            _logger.LogInfo(
                "[ModManager] " + mod.Name + " will be " +
                (enabled ? "enabled" : "disabled") +
                " after restart.");
            return "Saved. The new state will apply after the game restarts.";
        }

        private static bool UsesModManagerApi(PluginInfo info)
        {
            return info.Dependencies.Any(dependency =>
                string.Equals(
                    dependency.DependencyGUID,
                    Plugin.PluginGuid,
                    StringComparison.OrdinalIgnoreCase));
        }
    }
}

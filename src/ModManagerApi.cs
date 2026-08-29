using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;

namespace ModManager
{
    public static class ModManagerApi
    {
        public const string ManagerGuid = "com.jaqb.eros.modmanager";
        public const string StateFileName = "ModManager.states.cfg";

        private const string StateSection = "Mods";
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, ConfigEntry<bool>> Entries =
            new Dictionary<string, ConfigEntry<bool>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, bool> SessionStates =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private static ConfigFile _stateFile;

        public static string StateFilePath =>
            Path.Combine(Paths.ConfigPath, StateFileName);

        public static bool IsEnabled(string pluginGuid)
        {
            ValidateGuid(pluginGuid);
            lock (Sync)
            {
                if (!SessionStates.TryGetValue(pluginGuid, out bool enabled))
                {
                    enabled = GetEntry(pluginGuid).Value;
                    SessionStates[pluginGuid] = enabled;
                }
                return enabled;
            }
        }

        public static bool GetConfiguredState(string pluginGuid)
        {
            ValidateGuid(pluginGuid);
            lock (Sync)
                return GetEntry(pluginGuid).Value;
        }

        internal static void SetConfiguredState(string pluginGuid, bool enabled)
        {
            ValidateGuid(pluginGuid);
            lock (Sync)
            {
                GetEntry(pluginGuid).Value = enabled;
                StateFile.Save();
            }
        }

        private static ConfigEntry<bool> GetEntry(string pluginGuid)
        {
            if (Entries.TryGetValue(pluginGuid, out ConfigEntry<bool> entry))
                return entry;

            entry = StateFile.Bind(
                StateSection,
                pluginGuid,
                true,
                "Whether this mod initializes on the next game launch.");
            Entries[pluginGuid] = entry;
            return entry;
        }

        private static ConfigFile StateFile =>
            _stateFile ?? (_stateFile = new ConfigFile(StateFilePath, true));

        private static void ValidateGuid(string pluginGuid)
        {
            if (string.IsNullOrWhiteSpace(pluginGuid))
                throw new ArgumentException(
                    "A non-empty BepInEx plugin GUID is required.",
                    nameof(pluginGuid));
        }
    }
}

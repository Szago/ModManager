using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using ModManager.Updates;
using Mono.Cecil;

namespace ModManager.Updater
{
    public static class Patcher
    {
        public static IEnumerable<string> TargetDLLs => new string[0];
        public static void Patch(AssemblyDefinition assembly) { }

        public static void Initialize()
        {
            var log = Logger.CreateLogSource("ModManager Updates");
            try
            {
                string root = Path.Combine(Paths.BepInExRootPath, "ModManagerUpdates");
                if (!Directory.Exists(root)) return;
                foreach (string folder in Directory.GetDirectories(root))
                {
                    string manifest = Path.Combine(folder, "pending.xml");
                    if (!File.Exists(manifest)) continue;
                    try
                    {
                        PendingUpdate update = PendingUpdate.Load(manifest);
                        update.Apply(Paths.PluginPath, folder);
                        File.Move(manifest, Path.Combine(folder, "applied.xml"));
                    }
                    catch (Exception exception)
                    {
                        log.LogError("[ModManager] Could not apply staged update " + folder + ": " + exception);
                    }
                }
            }
            catch (Exception exception)
            {
                log.LogError("[ModManager] Could not read staged updates: " + exception);
            }
        }
    }
}

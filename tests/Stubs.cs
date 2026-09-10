using System;
using System.Collections.Generic;

namespace BepInEx
{
    public static class Paths
    {
        public static string BepInExRootPath;
        public static string PluginPath => System.IO.Path.Combine(BepInExRootPath, "plugins");
    }
    public class PluginInfo
    {
        public Metadata Metadata;
        public string Location;
    }
    public class Metadata { public Version Version; }
}
namespace BepInEx.Bootstrap
{
    public static class Chainloader
    {
        public static Dictionary<string, BepInEx.PluginInfo> PluginInfos = new Dictionary<string, BepInEx.PluginInfo>();
    }
}
namespace BepInEx.Logging
{
    public class ManualLogSource
    {
        public void LogWarning(object message) { }
        public void LogError(object message) { }
    }
    public static class Logger { public static ManualLogSource CreateLogSource(string name) => new ManualLogSource(); }
}
namespace ModManager
{
    internal static class Plugin { internal const string PluginVersion = "2.3.0"; }
    public static partial class ModManagerApi
    {
        private static void ValidateGuid(string guid) { if (string.IsNullOrWhiteSpace(guid)) throw new ArgumentException(); }
    }
}

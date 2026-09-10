using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using Mono.Cecil;

namespace ModManager.Updates
{
    // Shared by the plugin and the independent preloader; never loads a candidate assembly.
    public sealed class PendingUpdate
    {
        public string Guid;
        public string RelativeTarget;
        public string Version;
        public string OriginalHash;
        public string DownloadHash;

        public static string Hash(string file)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(file))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        public static System.Version ParseVersion(string value)
        {
            string normalized = (value ?? "").Trim();
            if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase)) normalized = normalized.Substring(1);
            if (!Regex.IsMatch(normalized, @"^\d+\.\d+\.\d+(\.\d+)?$") ||
                !System.Version.TryParse(normalized, out System.Version version))
                throw new InvalidDataException("Release tags must be stable versions such as v1.2.3.");
            return new System.Version(version.Major, version.Minor, version.Build, Math.Max(0, version.Revision));
        }

        public static string ResolveTarget(string plugins, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) ||
                relative.IndexOf(':') >= 0 || !relative.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid plugin target.");
            string root = Path.GetFullPath(plugins).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(root, relative));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Update target is outside plugins.");
            // Do not follow junctions/symlinks out of the selected plugin tree.
            for (string current = path; current != null; current = Path.GetDirectoryName(current))
            {
                if ((File.Exists(current) || Directory.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Update paths cannot contain junctions or symbolic links.");
            }
            return path;
        }

        public static void ValidateAssembly(string path, string guid, string version, string expectedAssembly = null)
        {
            using (AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(path))
            {
                var plugins = assembly.MainModule.Types.SelectMany(type => type.CustomAttributes)
                    .Where(attribute => attribute.AttributeType.FullName == "BepInEx.BepInPlugin").ToArray();
                if (plugins.Length != 1 || plugins[0].ConstructorArguments.Count != 3 ||
                    !string.Equals(plugins[0].ConstructorArguments[0].Value as string, guid, StringComparison.OrdinalIgnoreCase) ||
                    ParseVersion(plugins[0].ConstructorArguments[2].Value as string) != ParseVersion(version) ||
                    (expectedAssembly != null && assembly.Name.Name != expectedAssembly))
                    throw new InvalidDataException("Downloaded DLL identity/version does not match this mod and release.");
            }
        }

        public void Save(string path)
        {
            using (var stream = File.Create(path)) new XmlSerializer(typeof(PendingUpdate)).Serialize(stream, this);
        }

        public static PendingUpdate Load(string path)
        {
            var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null };
            using (var reader = System.Xml.XmlReader.Create(path, settings))
                return (PendingUpdate)new XmlSerializer(typeof(PendingUpdate)).Deserialize(reader);
        }

        public void Apply(string plugins, string folder)
        {
            string target = ResolveTarget(plugins, RelativeTarget);
            string payload = Path.Combine(folder, "payload.bin");
            if (!File.Exists(target)) throw new IOException("The installed plugin no longer exists.");
            // A crash after atomic replacement but before marking completion is safe to resume.
            if (Hash(target) == DownloadHash) return;
            if (Hash(target) != OriginalHash) throw new IOException("Installed DLL changed after download; check again before updating.");
            if (Hash(payload) != DownloadHash) throw new InvalidDataException("Staged update checksum mismatch.");
            using (var original = AssemblyDefinition.ReadAssembly(target))
                ValidateAssembly(payload, Guid, Version, original.Name.Name);
            // Staging is under BepInEx (same volume). Replace atomically and retain the old DLL.
            File.Replace(payload, target, Path.Combine(folder, "previous.bin"));
        }
    }
}

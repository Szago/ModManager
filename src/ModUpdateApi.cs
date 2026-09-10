using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ModManager
{
    public static partial class ModManagerApi
    {
        private static readonly Dictionary<string, ReleaseSource> ReleaseSources =
            new Dictionary<string, ReleaseSource>(StringComparer.OrdinalIgnoreCase);

        // Register before IsEnabled, just like descriptions. Empty means not published yet.
        public static void RegisterReleaseSource(string pluginGuid, string releasesUrl, string assetName)
        {
            ValidateGuid(pluginGuid);
            ReleaseSource source = ReleaseSource.Parse(releasesUrl, assetName);
            lock (ReleaseSources)
                ReleaseSources[pluginGuid] = source;
        }

        internal static ReleaseSource GetReleaseSource(string guid)
        {
            lock (ReleaseSources)
                return ReleaseSources.TryGetValue(guid, out ReleaseSource source) ? source : null;
        }
    }

    internal sealed class ReleaseSource
    {
        internal string Repository;
        internal string AssetName;

        internal static ReleaseSource Parse(string url, string asset)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri) ||
                uri.Scheme != "https" || uri.Host != "github.com" || !uri.IsDefaultPort ||
                uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
                !Regex.IsMatch(uri.AbsolutePath, @"^/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/releases/?$"))
                throw new ArgumentException("Expected https://github.com/OWNER/REPO/releases.", nameof(url));
            if (string.IsNullOrEmpty(asset) ||
                !Regex.IsMatch(asset, @"^[A-Za-z0-9_.-]+\.dll$", RegexOptions.IgnoreCase))
                throw new ArgumentException("Supply the exact DLL release asset filename.", nameof(asset));
            string[] segments = uri.AbsolutePath.Split('/');
            return new ReleaseSource { Repository = segments[1] + "/" + segments[2], AssetName = asset };
        }
    }
}

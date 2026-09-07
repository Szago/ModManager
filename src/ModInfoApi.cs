using System;
using System.Collections.Generic;

namespace ModManager
{
    public static partial class ModManagerApi
    {
        private static readonly object DescriptionSync = new object();
        private static readonly Dictionary<string, string> Descriptions =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static void RegisterDescription(string pluginGuid, string description)
        {
            ValidateGuid(pluginGuid);
            if (string.IsNullOrWhiteSpace(description))
                throw new ArgumentException(
                    "A non-empty mod description is required.",
                    nameof(description));

            lock (DescriptionSync)
                Descriptions[pluginGuid] = description.Trim();
        }

        internal static string GetDescription(string pluginGuid)
        {
            ValidateGuid(pluginGuid);
            lock (DescriptionSync)
                return Descriptions.TryGetValue(pluginGuid, out string description)
                    ? description
                    : string.Empty;
        }
    }
}

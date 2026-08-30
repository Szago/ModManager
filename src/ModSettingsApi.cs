using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;

namespace ModManager
{
    public sealed class ModChoiceOption
    {
        public ModChoiceOption(string value, string label)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty option value is required.", nameof(value));
            if (string.IsNullOrWhiteSpace(label))
                throw new ArgumentException("A non-empty option label is required.", nameof(label));

            Value = value;
            Label = label;
        }

        public string Value { get; }
        public string Label { get; }
    }

    internal sealed class RegisteredChoiceSetting
    {
        private readonly ConfigEntry<string> _entry;

        internal RegisteredChoiceSetting(
            string key,
            string displayName,
            ConfigEntry<string> entry,
            IReadOnlyList<ModChoiceOption> options)
        {
            Key = key;
            DisplayName = displayName;
            _entry = entry;
            Options = options;
        }

        internal string Key { get; }
        internal string DisplayName { get; }
        internal IReadOnlyList<ModChoiceOption> Options { get; }
        internal string CurrentValue => _entry.Value;

        internal void Select(string value)
        {
            if (!Options.Any(option =>
                    string.Equals(option.Value, value, StringComparison.Ordinal)))
                throw new ArgumentException(
                    "The value is not registered for this setting.",
                    nameof(value));

            _entry.Value = value;
        }
    }

    public static partial class ModManagerApi
    {
        private static readonly object SettingsSync = new object();
        private static readonly Dictionary<string, Dictionary<string, RegisteredChoiceSetting>>
            ChoiceSettings =
                new Dictionary<string, Dictionary<string, RegisteredChoiceSetting>>(
                    StringComparer.OrdinalIgnoreCase);

        public static void RegisterChoiceSetting(
            string pluginGuid,
            string settingKey,
            string displayName,
            ConfigEntry<string> entry,
            params ModChoiceOption[] options)
        {
            ValidateGuid(pluginGuid);
            if (string.IsNullOrWhiteSpace(settingKey))
                throw new ArgumentException(
                    "A non-empty setting key is required.",
                    nameof(settingKey));
            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException(
                    "A non-empty setting display name is required.",
                    nameof(displayName));
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));
            if (options == null || options.Length < 2)
                throw new ArgumentException(
                    "A choice setting requires at least two options.",
                    nameof(options));

            var copiedOptions = new List<ModChoiceOption>(options.Length);
            var values = new HashSet<string>(StringComparer.Ordinal);
            foreach (ModChoiceOption option in options)
            {
                if (option == null)
                    throw new ArgumentException(
                        "Choice options cannot contain null.",
                        nameof(options));
                if (!values.Add(option.Value))
                    throw new ArgumentException(
                        "Choice option values must be unique.",
                        nameof(options));
                copiedOptions.Add(option);
            }

            lock (SettingsSync)
            {
                if (!ChoiceSettings.TryGetValue(
                        pluginGuid,
                        out Dictionary<string, RegisteredChoiceSetting> settings))
                {
                    settings = new Dictionary<string, RegisteredChoiceSetting>(
                        StringComparer.OrdinalIgnoreCase);
                    ChoiceSettings[pluginGuid] = settings;
                }

                settings[settingKey] = new RegisteredChoiceSetting(
                    settingKey,
                    displayName,
                    entry,
                    copiedOptions);
            }
        }

        internal static IReadOnlyList<RegisteredChoiceSetting> GetChoiceSettings(
            string pluginGuid)
        {
            ValidateGuid(pluginGuid);
            lock (SettingsSync)
            {
                if (!ChoiceSettings.TryGetValue(
                        pluginGuid,
                        out Dictionary<string, RegisteredChoiceSetting> settings))
                    return new RegisteredChoiceSetting[0];

                return settings.Values
                    .OrderBy(setting => setting.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
    }
}

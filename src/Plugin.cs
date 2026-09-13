using System;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ModManager
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.eros.modmanager";
        public const string PluginName = "Mod Manager";
        public const string PluginVersion = "2.3.9";
        public const string PluginReleasesUrl = "https://github.com/Szago/ModManager/releases";

        private const float UiScanInterval = 5f;

        private static readonly AccessTools.FieldRef<GameSettings, Button> BatteryModeOnButton =
            AccessTools.FieldRefAccess<GameSettings, Button>("btn_BatteryModeOn");
        private static readonly AccessTools.FieldRef<GameSettings, Button> BatteryModeOffButton =
            AccessTools.FieldRefAccess<GameSettings, Button>("btn_BatteryModeOff");

        private Button _button30;
        private Button _button60;
        private Button _testButton;
        private float _nextUiScan;
        private ModRegistry _registry;
        private ManagerUi _managerUi;
        private UpdateService _updates;

        internal static Plugin Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
            ModManagerApi.RegisterReleaseSource(PluginGuid, PluginReleasesUrl, "ModManager.dll");
            _updates = new UpdateService(Logger);
            _registry = new ModRegistry();
            _registry.Discover();
            _managerUi = new ManagerUi(Logger, _registry, _updates);
            new Harmony(PluginGuid).PatchAll();

            Application.onBeforeRender -= TickPersistentRuntime;
            Application.onBeforeRender += TickPersistentRuntime;
        }

        private void PersistentUpdate()
        {
            _updates.Tick();
            _managerUi.TickUpdates();
            float now = Time.realtimeSinceStartup;
            try
            {
                if ((_button30 == null || _button60 == null || _testButton == null) &&
                    now >= _nextUiScan)
                {
                    _nextUiScan = now + UiScanInterval;
                    TryAttachToKnownHierarchy();
                }
            }
            catch (Exception exception)
            {
                Logger.LogError("[ModManager] Persistent Update failed: " + exception);
                _nextUiScan = now + UiScanInterval;
            }
        }

        internal static void TickPersistentRuntime()
        {
            Plugin instance = Instance;
            if (!ReferenceEquals(instance, null))
                instance.PersistentUpdate();
        }

        private void OnDestroy()
        {
        }

        internal void AttachToSettings(GameSettings settings)
        {
            if (settings == null)
                return;

            Button button30 = BatteryModeOffButton(settings);
            Button button60 = BatteryModeOnButton(settings);
            AttachButtons(button30, button60);
        }

        private void TryAttachToKnownHierarchy()
        {
            try
            {
                RectTransform button60Transform = null;
                RectTransform[] loadedUiTransforms =
                    Resources.FindObjectsOfTypeAll<RectTransform>();

                foreach (RectTransform candidate in loadedUiTransforms)
                {
                    if (candidate == null || !candidate.gameObject.scene.IsValid())
                        continue;

                    if (candidate.name == "Button 60")
                    {
                        if (candidate.parent != null && candidate.parent.name == "OnOff")
                            button60Transform = candidate;
                    }
                }

                if (button60Transform == null)
                    return;

                Transform buttonGroup = button60Transform.parent;
                Transform button30Transform = buttonGroup.Find("Button 30");
                Button button30 = FindButton(button30Transform);
                Button button60 = FindButton(button60Transform);
                if (button30 == null || button60 == null)
                    return;

                AttachButtons(button30, button60);
            }
            catch (Exception exception)
            {
                Logger.LogError("[ModManager] Failed to attach the TEST settings control: " + exception);
            }
        }

        private static Button FindButton(Transform transform)
        {
            return transform == null
                ? null
                : transform.GetComponent<Button>() ??
                  transform.GetComponentInChildren<Button>(true);
        }

        private void AttachButtons(Button button30, Button button60)
        {
            if (button30 == null || button60 == null)
                return;

            Button claimButton = SettingsButton.FindClaimButton();
            if (claimButton == null)
                return;

            _button30 = button30;
            _button60 = button60;
            _testButton = SettingsButton.Install(
                button30,
                button60,
                claimButton,
                OpenManager);
        }

        private void OpenManager()
        {
            try
            {
                _registry.Discover();
                Transform settingsShell = _testButton != null
                    ? SettingsButton.FindSettingsShell(_testButton.transform)
                    : null;
                _managerUi.Show(settingsShell);
            }
            catch (Exception exception)
            {
                Logger.LogError("[ModManager] Failed to open the manager panel: " + exception);
            }
        }
    }

    [HarmonyPatch(typeof(GameSettings), "Start")]
    internal static class GameSettingsStartPatch
    {
        private static void Postfix(GameSettings __instance)
        {
            Plugin.Instance?.AttachToSettings(__instance);
        }
    }
}

using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.UI;

namespace ModManager
{
    internal sealed class ManagerUi
    {
        private const int SortingOrder = 32760;
        private static readonly Color PanelColor = new Color(0.055f, 0.045f, 0.075f, 0.985f);
        private static readonly Color RowColor = new Color(0.12f, 0.095f, 0.15f, 0.96f);
        private static readonly Color MutedText = new Color(0.72f, 0.68f, 0.76f, 1f);
        private static readonly Color Accent = new Color(0.74f, 0.34f, 0.62f, 1f);

        private readonly ManualLogSource _logger;
        private readonly ModRegistry _registry;
        private readonly List<GameObject> _rows = new List<GameObject>();
        private GameObject _canvasObject;
        private GameObject _panel;
        private RectTransform _content;
        private Text _footer;

        internal ManagerUi(ManualLogSource logger, ModRegistry registry)
        {
            _logger = logger;
            _registry = registry;
        }

        internal void Show(Canvas sourceCanvas)
        {
            EnsureCreated(sourceCanvas);
            RefreshRows();
            _canvasObject.transform.SetAsLastSibling();
            _canvasObject.SetActive(true);
            _panel.SetActive(true);
        }

        internal void Destroy()
        {
            if (_canvasObject != null) Object.Destroy(_canvasObject);
        }

        private void EnsureCreated(Canvas sourceCanvas)
        {
            if (_canvasObject != null)
            {
                ApplyCanvasSettings(sourceCanvas);
                return;
            }

            _canvasObject = new GameObject("ModManager_OverlayCanvas");
            Object.DontDestroyOnLoad(_canvasObject);
            _canvasObject.AddComponent<Canvas>();
            _canvasObject.AddComponent<GraphicRaycaster>();
            ApplyCanvasSettings(sourceCanvas);
            CopyCanvasScaler(sourceCanvas);

            GameObject dimmer = UiFactory.Object("Dimmer", _canvasObject.transform);
            UiFactory.Rect(dimmer, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            UiFactory.Image(dimmer, new Color(0f, 0f, 0f, 0.58f));
            Button dimmerButton = dimmer.AddComponent<Button>();
            dimmerButton.onClick.AddListener(Hide);

            _panel = UiFactory.Object("ModManager_Panel", _canvasObject.transform);
            RectTransform panelRect = UiFactory.Rect(
                _panel, new Vector2(1f, 0f), new Vector2(1f, 1f),
                new Vector2(-760f, 0f), Vector2.zero);
            panelRect.pivot = new Vector2(1f, 0.5f);
            UiFactory.Image(_panel, PanelColor);

            CreateHeader();
            CreateInfo();
            CreateList();
            CreateFooter();
            _canvasObject.SetActive(false);
        }

        private void ApplyCanvasSettings(Canvas sourceCanvas)
        {
            Canvas canvas = _canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = SortingOrder;
            if (sourceCanvas != null)
            {
                canvas.sortingLayerID = sourceCanvas.sortingLayerID;
                canvas.targetDisplay = sourceCanvas.targetDisplay;
                canvas.pixelPerfect = sourceCanvas.pixelPerfect;
            }
        }

        private void CopyCanvasScaler(Canvas sourceCanvas)
        {
            CanvasScaler scaler = _canvasObject.AddComponent<CanvasScaler>();
            CanvasScaler source = sourceCanvas != null ? sourceCanvas.GetComponent<CanvasScaler>() : null;
            if (source == null)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
                return;
            }
            scaler.uiScaleMode = source.uiScaleMode;
            scaler.referencePixelsPerUnit = source.referencePixelsPerUnit;
            scaler.scaleFactor = source.scaleFactor;
            scaler.referenceResolution = source.referenceResolution;
            scaler.screenMatchMode = source.screenMatchMode;
            scaler.matchWidthOrHeight = source.matchWidthOrHeight;
            scaler.physicalUnit = source.physicalUnit;
            scaler.fallbackScreenDPI = source.fallbackScreenDPI;
            scaler.defaultSpriteDPI = source.defaultSpriteDPI;
            scaler.dynamicPixelsPerUnit = source.dynamicPixelsPerUnit;
        }

        private void CreateHeader()
        {
            Text title = UiFactory.Text("Title", _panel.transform, "MOD MANAGER", 32,
                TextAnchor.MiddleLeft, Color.white);
            title.fontStyle = FontStyle.Bold;
            UiFactory.Rect(title.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(32f, -92f), new Vector2(-100f, -20f));

            Button close = UiFactory.Button("Close", _panel.transform, "X", Hide);
            UiFactory.Rect(close.gameObject, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-82f, -76f), new Vector2(-24f, -18f));

            GameObject line = UiFactory.Object("HeaderLine", _panel.transform);
            UiFactory.Rect(line, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(24f, -98f), new Vector2(-24f, -94f));
            UiFactory.Image(line, Accent, false);
        }

        private void CreateInfo()
        {
            Text info = UiFactory.Text("RestartInfo", _panel.transform,
                "Changes are applied after the game restarts. The current session is not modified.",
                18, TextAnchor.UpperLeft, MutedText);
            UiFactory.Rect(info.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(30f, -152f), new Vector2(-30f, -108f));
        }

        private void Hide()
        {
            if (_canvasObject != null) _canvasObject.SetActive(false);
        }

        private void RefreshRows()
        {
            foreach (GameObject row in _rows) Object.Destroy(row);
            _rows.Clear();
            foreach (ManagedMod mod in _registry.Mods) CreateModRow(mod);
            _footer.text = _registry.Mods.Count == 0
                ? "No manageable BepInEx plugins were found."
                : "Mod Manager and BepInEx are protected and never appear in this list.";
        }

        private void CreateList()
        {
            GameObject viewport = UiFactory.Object("Viewport", _panel.transform);
            UiFactory.Rect(viewport, Vector2.zero, Vector2.one,
                new Vector2(24f, 104f), new Vector2(-24f, -166f));
            UiFactory.Image(viewport, new Color(0f, 0f, 0f, 0.16f));
            viewport.AddComponent<RectMask2D>();

            ScrollRect scroll = viewport.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 34f;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            GameObject contentObject = UiFactory.Object("Content", viewport.transform);
            _content = UiFactory.Rect(contentObject,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                Vector2.zero, Vector2.zero);
            _content.pivot = new Vector2(0.5f, 1f);

            VerticalLayoutGroup layout = contentObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.spacing = 10f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            ContentSizeFitter fitter = contentObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content = _content;
        }

        private void CreateFooter()
        {
            _footer = UiFactory.Text("Footer", _panel.transform, "", 16,
                TextAnchor.MiddleLeft, MutedText);
            UiFactory.Rect(_footer.gameObject, Vector2.zero, new Vector2(1f, 0f),
                new Vector2(30f, 24f), new Vector2(-30f, 88f));
        }

        private void CreateModRow(ManagedMod mod)
        {
            GameObject row = UiFactory.Object("Mod_" + mod.Guid, _content);
            _rows.Add(row);
            UiFactory.Image(row, RowColor);
            LayoutElement element = row.AddComponent<LayoutElement>();
            element.preferredHeight = 94f;
            element.minHeight = 94f;

            Text name = UiFactory.Text("Name", row.transform,
                mod.Name + (string.IsNullOrEmpty(mod.Version) ? "" : "  v" + mod.Version),
                22, TextAnchor.MiddleLeft, Color.white);
            name.fontStyle = FontStyle.Bold;
            UiFactory.Rect(name.gameObject, Vector2.zero, Vector2.one,
                new Vector2(18f, 42f), new Vector2(-130f, -8f));

            Text identity = UiFactory.Text("Guid", row.transform, mod.Guid, 14,
                TextAnchor.MiddleLeft, MutedText);
            UiFactory.Rect(identity.gameObject, Vector2.zero, Vector2.one,
                new Vector2(18f, 20f), new Vector2(-130f, -48f));

            Text status = UiFactory.Text("Status", row.transform, StatusText(mod), 13,
                TextAnchor.LowerLeft, StatusColor(mod));
            status.fontStyle = FontStyle.Bold;
            UiFactory.Rect(status.gameObject, Vector2.zero, Vector2.one,
                new Vector2(18f, 7f), new Vector2(-130f, -68f));

            Toggle toggle = CreateToggle(row.transform, mod.DesiredEnabled);
            toggle.onValueChanged.AddListener(enabled =>
            {
                try
                {
                    _footer.text = _registry.SetDesiredState(mod, enabled);
                    status.text = StatusText(mod);
                    status.color = StatusColor(mod);
                }
                catch (System.Exception exception)
                {
                    _logger.LogError("[ModManager] Could not change " + mod.Name + ": " + exception);
                    toggle.SetIsOnWithoutNotify(mod.DesiredEnabled);
                    _footer.text = "The change could not be staged. Check BepInEx\\LogOutput.log.";
                }
            });
        }

        private static Toggle CreateToggle(Transform parent, bool enabled)
        {
            GameObject root = UiFactory.Object("RestartToggle", parent);
            UiFactory.Rect(root, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-108f, -22f), new Vector2(-28f, 22f));
            Image background = UiFactory.Image(root, new Color(0.20f, 0.17f, 0.23f, 1f));

            GameObject checkObject = UiFactory.Object("Enabled", root.transform);
            UiFactory.Rect(checkObject, Vector2.zero, Vector2.one,
                new Vector2(5f, 5f), new Vector2(-5f, -5f));
            Image check = UiFactory.Image(checkObject, new Color(0.40f, 0.82f, 0.48f, 1f), false);

            Toggle toggle = root.AddComponent<Toggle>();
            toggle.targetGraphic = background;
            toggle.graphic = check;
            toggle.transition = Selectable.Transition.ColorTint;
            toggle.SetIsOnWithoutNotify(enabled);
            check.canvasRenderer.SetAlpha(enabled ? 1f : 0f);
            return toggle;
        }

        private static string StatusText(ManagedMod mod)
        {
            if (mod.Pending)
                return mod.ActiveThisSession
                    ? "ACTIVE NOW  -  DISABLED AFTER RESTART"
                    : "INACTIVE NOW  -  ENABLED AFTER RESTART";
            return mod.ActiveThisSession ? "ACTIVE" : "INACTIVE";
        }

        private static Color StatusColor(ManagedMod mod)
        {
            if (mod.Pending)
                return new Color(0.98f, 0.73f, 0.32f, 1f);
            return mod.ActiveThisSession
                ? new Color(0.40f, 0.82f, 0.48f, 1f)
                : MutedText;
        }
    }
}

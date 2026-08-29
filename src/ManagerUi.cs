using System.Collections.Generic;
using BepInEx.Logging;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModManager
{
    internal sealed class ManagerUi
    {
        private static readonly Color RowColor = new Color(0.18f, 0.12f, 0.06f, 0.96f);
        private static readonly Color MutedText = new Color(0.72f, 0.68f, 0.58f, 1f);
        private static readonly Color Accent = new Color(
            239f / 255f,
            170f / 255f,
            23f / 255f,
            1f);
        private static readonly Color HeaderText = new Color(
            16f / 255f,
            11f / 255f,
            10f / 255f,
            1f);

        private readonly ManualLogSource _logger;
        private readonly ModRegistry _registry;
        private readonly UiAssets _assets;
        private readonly List<GameObject> _rows = new List<GameObject>();
        private GameObject _overlayCanvasObject;
        private GameObject _canvasObject;
        private GameObject _nativeCloseObject;
        private GameObject _inputBlockerObject;
        private GameObject _sourceSettingsCanvasObject;
        private GameObject _panel;
        private RectTransform _content;
        private TMP_Text _footer;
        private Sprite _rowBackgroundSprite;

        internal ManagerUi(ManualLogSource logger, ModRegistry registry)
        {
            _logger = logger;
            _registry = registry;
            _assets = new UiAssets(logger);
        }

        internal void Show(Transform sourceShell)
        {
            EnsureCreated(sourceShell);
            RefreshRows();
            _overlayCanvasObject.transform.SetAsLastSibling();
            _overlayCanvasObject.SetActive(true);
            _inputBlockerObject.transform.SetAsLastSibling();
            _inputBlockerObject.SetActive(true);
            _canvasObject.transform.SetAsLastSibling();
            _canvasObject.SetActive(true);
            if (_nativeCloseObject != null)
            {
                _nativeCloseObject.transform.SetAsLastSibling();
                _nativeCloseObject.SetActive(true);
            }
            _panel.SetActive(true);
            _sourceSettingsCanvasObject.SetActive(false);
            _logger.LogInfo(
                "[ModManager] Set Canvas_Settings inactive and opened the independent manager canvas.");
        }

        internal void Destroy()
        {
            if (_overlayCanvasObject != null) Object.Destroy(_overlayCanvasObject);
        }

        private void EnsureCreated(Transform sourceShell)
        {
            if (_canvasObject != null)
                return;

            if (sourceShell == null)
                throw new System.InvalidOperationException(
                    "The Settings_BG/Panel Frame shell was not found.");

            bool sourceWasActive = sourceShell.gameObject.activeSelf;
            GameObject clone = null;
            sourceShell.gameObject.SetActive(false);
            try
            {
                clone = Object.Instantiate(
                    sourceShell.gameObject,
                    sourceShell.parent,
                    false);
            }
            finally
            {
                sourceShell.gameObject.SetActive(sourceWasActive);
            }

            if (clone == null)
                throw new System.InvalidOperationException(
                    "The settings shell could not be cloned.");

            clone.name = "ModManager_ClonedSettingsShell";
            clone.SetActive(false);
            _canvasObject = clone;
            SanitizeClonedShell(clone.transform);
            RestoreBackgroundVisuals(sourceShell, clone.transform);
            CreateNativeCloseButton(sourceShell);

            Transform settingsCanvasRoot = sourceShell;
            while (settingsCanvasRoot != null &&
                   settingsCanvasRoot.name != "Canvas_Settings")
                settingsCanvasRoot = settingsCanvasRoot.parent;
            if (settingsCanvasRoot == null)
                throw new System.InvalidOperationException(
                    "The Canvas_Settings object was not found.");

            Canvas sourceCanvas =
                settingsCanvasRoot.GetComponent<Canvas>() ??
                settingsCanvasRoot.GetComponentInChildren<Canvas>(true);
            if (sourceCanvas == null)
                throw new System.InvalidOperationException(
                    "Canvas_Settings has no Canvas component.");

            _sourceSettingsCanvasObject = settingsCanvasRoot.gameObject;
            CreateOverlayCanvas(sourceCanvas, settingsCanvasRoot.parent);
            Transform overlayParent = _overlayCanvasObject.transform;
            CloneSettingsBackdrop(settingsCanvasRoot, overlayParent);
            _canvasObject.transform.SetParent(overlayParent, true);
            if (_nativeCloseObject != null)
            {
                _nativeCloseObject.transform.SetParent(overlayParent, true);
                RectTransform closeRect =
                    _nativeCloseObject.GetComponent<RectTransform>();
                if (closeRect != null)
                {
                    closeRect.anchoredPosition += new Vector2(-55f, -40f);
                    closeRect.localScale *= 1.2f;
                }
            }

            Transform background = _canvasObject.transform.Find("Settings_BG");
            Transform frame = _canvasObject.transform.Find("Panel Frame");
            if (frame != null)
                frame.SetAsFirstSibling();
            if (background != null)
                background.SetSiblingIndex(Mathf.Min(1, _canvasObject.transform.childCount - 1));

            _inputBlockerObject = UiFactory.Object(
                "ModManager_FullCanvasInputBlocker",
                overlayParent);
            UiFactory.Rect(
                _inputBlockerObject,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero);
            UiFactory.Image(
                _inputBlockerObject,
                new Color(0f, 0f, 0f, 0.001f),
                true);
            _inputBlockerObject.SetActive(false);

            _panel = UiFactory.Object(
                "ModManager_InjectedContent",
                _canvasObject.transform);
            UiFactory.Rect(
                _panel,
                Vector2.zero,
                Vector2.one,
                new Vector2(70f, 55f),
                new Vector2(-70f, -55f));
            UiFactory.Image(_panel, new Color(0f, 0f, 0f, 0.01f));

            string gameFont = UiFactory.CaptureGameFont(settingsCanvasRoot);
            _logger.LogInfo(
                gameFont == null
                    ? "[ModManager] No compatible game TMP font found; using TMP default."
                    : "[ModManager] Using game TMP font: " + gameFont);
            _rowBackgroundSprite =
                _assets.LoadEmbeddedSprite("UI_BG_Paper.png", 24f);
            if (_rowBackgroundSprite != null)
                _logger.LogInfo(
                    "[ModManager] Loaded TeamPresets UI_BG_Paper row texture.");

            CreateHeader();
            CreateInfo();
            CreateList();
            CreateFooter();
            _panel.transform.SetAsLastSibling();
            _overlayCanvasObject.SetActive(false);
            _logger.LogInfo(
                "[ModManager] Created an independent manager canvas beside Canvas_Settings.");
        }

        private void CloneSettingsBackdrop(
            Transform settingsCanvasRoot,
            Transform overlayParent)
        {
            Transform sourceBackdrop = settingsCanvasRoot.Find("Background");
            if (sourceBackdrop == null)
            {
                _logger.LogWarning(
                    "[ModManager] Canvas_Settings/Background was not found.");
                return;
            }

            GameObject backdrop = Object.Instantiate(
                sourceBackdrop.gameObject,
                overlayParent,
                false);
            backdrop.name = "ModManager_Background";
            backdrop.SetActive(true);
            foreach (Graphic graphic in
                     backdrop.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = false;
            backdrop.transform.SetAsFirstSibling();
            _logger.LogInfo(
                "[ModManager] Cloned Canvas_Settings/Background into the manager canvas.");
        }

        private void CreateOverlayCanvas(Canvas sourceCanvas, Transform parent)
        {
            _overlayCanvasObject = new GameObject(
                "ModManager_OverlayCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            _overlayCanvasObject.transform.SetParent(parent, false);

            Canvas canvas = _overlayCanvasObject.GetComponent<Canvas>();
            canvas.renderMode = sourceCanvas.renderMode;
            canvas.worldCamera = sourceCanvas.worldCamera;
            canvas.planeDistance = sourceCanvas.planeDistance;
            canvas.pixelPerfect = sourceCanvas.pixelPerfect;
            canvas.targetDisplay = sourceCanvas.targetDisplay;
            canvas.sortingLayerID = sourceCanvas.sortingLayerID;
            canvas.overrideSorting = true;
            canvas.sortingOrder = sourceCanvas.sortingOrder + 1;

            CanvasScaler sourceScaler = sourceCanvas.GetComponent<CanvasScaler>();
            CanvasScaler scaler = _overlayCanvasObject.GetComponent<CanvasScaler>();
            if (sourceScaler == null)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
            }
            else
            {
                scaler.uiScaleMode = sourceScaler.uiScaleMode;
                scaler.referencePixelsPerUnit = sourceScaler.referencePixelsPerUnit;
                scaler.scaleFactor = sourceScaler.scaleFactor;
                scaler.referenceResolution = sourceScaler.referenceResolution;
                scaler.screenMatchMode = sourceScaler.screenMatchMode;
                scaler.matchWidthOrHeight = sourceScaler.matchWidthOrHeight;
                scaler.physicalUnit = sourceScaler.physicalUnit;
                scaler.fallbackScreenDPI = sourceScaler.fallbackScreenDPI;
                scaler.defaultSpriteDPI = sourceScaler.defaultSpriteDPI;
                scaler.dynamicPixelsPerUnit = sourceScaler.dynamicPixelsPerUnit;
            }
        }

        private static void SanitizeClonedShell(Transform shell)
        {
            var children = new List<Transform>();
            for (int index = 0; index < shell.childCount; index++)
                children.Add(shell.GetChild(index));

            foreach (Transform child in children)
            {
                bool keepVisual =
                    child.name == "Settings_BG" ||
                    child.name == "Panel Frame";
                if (!keepVisual)
                {
                    child.gameObject.SetActive(false);
                    Object.Destroy(child.gameObject);
                    continue;
                }

                foreach (Graphic graphic in
                         child.GetComponentsInChildren<Graphic>(true))
                    graphic.raycastTarget = false;
            }

            foreach (MonoBehaviour component in
                     shell.GetComponents<MonoBehaviour>())
            {
                if (!(component is Graphic))
                    Object.Destroy(component);
            }
        }

        private void RestoreBackgroundVisuals(
            Transform sourceShell,
            Transform clonedShell)
        {
            Transform sourceBackground = sourceShell.Find("Settings_BG");
            Transform clonedBackground = clonedShell.Find("Settings_BG");
            if (sourceBackground == null || clonedBackground == null)
            {
                _logger.LogWarning(
                    "[ModManager] Settings_BG was not found on both source and cloned shells.");
                return;
            }

            int imageCount = 0;
            foreach (Image sourceImage in
                     sourceBackground.GetComponentsInChildren<Image>(true))
            {
                Transform target = FindMatchingTransform(
                    sourceBackground,
                    clonedBackground,
                    sourceImage.transform);
                if (target == null)
                    continue;

                Image targetImage = target.GetComponent<Image>() ??
                                    target.gameObject.AddComponent<Image>();
                CopyImage(sourceImage, targetImage);
                imageCount++;
            }

            int rawImageCount = 0;
            foreach (RawImage sourceImage in
                     sourceBackground.GetComponentsInChildren<RawImage>(true))
            {
                Transform target = FindMatchingTransform(
                    sourceBackground,
                    clonedBackground,
                    sourceImage.transform);
                if (target == null)
                    continue;

                RawImage targetImage = target.GetComponent<RawImage>() ??
                                       target.gameObject.AddComponent<RawImage>();
                targetImage.texture = sourceImage.texture;
                targetImage.uvRect = sourceImage.uvRect;
                targetImage.material = sourceImage.material;
                targetImage.color = sourceImage.color;
                targetImage.enabled = sourceImage.enabled;
                targetImage.raycastTarget = false;
                rawImageCount++;
            }

            clonedBackground.gameObject.SetActive(true);
            _logger.LogInfo(
                "[ModManager] Restored Settings_BG visuals: " +
                imageCount + " Image, " + rawImageCount + " RawImage component(s).");
        }

        private void CreateNativeCloseButton(Transform sourceShell)
        {
            Transform sourceClose = sourceShell.parent != null
                ? sourceShell.parent.Find("Btn_Close")
                : null;
            if (sourceClose == null)
            {
                GameObject exactObject = GameObject.Find(
                    "Canvas_Settings/Panel_Settings/Btn_Close");
                sourceClose = exactObject != null ? exactObject.transform : null;
            }

            if (sourceClose == null)
            {
                _logger.LogWarning(
                    "[ModManager] Native settings Btn_Close was not found.");
                return;
            }

            _nativeCloseObject = Object.Instantiate(
                sourceClose.gameObject,
                sourceClose.parent,
                false);
            _nativeCloseObject.name = "ModManager_Btn_Close";

            Button closeButton =
                _nativeCloseObject.GetComponent<Button>() ??
                _nativeCloseObject.GetComponentInChildren<Button>(true);
            if (closeButton == null)
            {
                _logger.LogWarning(
                    "[ModManager] Cloned Btn_Close has no Button component.");
                Object.Destroy(_nativeCloseObject);
                _nativeCloseObject = null;
                return;
            }

            closeButton.onClick = new Button.ButtonClickedEvent();
            closeButton.onClick.AddListener(Hide);
            closeButton.interactable = true;
            _nativeCloseObject.SetActive(false);
            _logger.LogInfo("[ModManager] Cloned native settings Btn_Close.");
        }

        private static Transform FindMatchingTransform(
            Transform sourceRoot,
            Transform targetRoot,
            Transform source)
        {
            if (source == sourceRoot)
                return targetRoot;

            var names = new List<string>();
            Transform current = source;
            while (current != null && current != sourceRoot)
            {
                names.Add(current.name);
                current = current.parent;
            }

            if (current != sourceRoot)
                return null;

            Transform target = targetRoot;
            for (int index = names.Count - 1; index >= 0; index--)
            {
                target = target.Find(names[index]);
                if (target == null)
                    return null;
            }

            return target;
        }

        private static void CopyImage(Image source, Image target)
        {
            target.sprite = source.sprite;
            target.overrideSprite = source.overrideSprite;
            target.type = source.type;
            target.preserveAspect = source.preserveAspect;
            target.fillCenter = source.fillCenter;
            target.fillMethod = source.fillMethod;
            target.fillAmount = source.fillAmount;
            target.fillClockwise = source.fillClockwise;
            target.fillOrigin = source.fillOrigin;
            target.material = source.material;
            target.color = source.color;
            target.enabled = source.enabled;
            target.maskable = source.maskable;
            target.raycastTarget = false;
        }

        private void CreateHeader()
        {
            TMP_Text title = UiFactory.TmpText(
                "Title",
                _panel.transform,
                "MOD MANAGER",
                64f,
                TextAlignmentOptions.MidlineLeft,
                HeaderText);
            title.fontStyle = FontStyles.Bold;
            UiFactory.Rect(title.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(64f, -184f), new Vector2(-170f, -40f));

            GameObject line = UiFactory.Object("HeaderLine", _panel.transform);
            UiFactory.Rect(line, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(48f, -204f), new Vector2(-48f, -196f));
            UiFactory.Image(line, Accent, false);
        }

        private void CreateInfo()
        {
            TMP_Text info = UiFactory.TmpText("RestartInfo", _panel.transform,
                "Changes are applied after the game restarts. The current session is not modified.",
                36f, TextAlignmentOptions.TopLeft, MutedText);
            UiFactory.Rect(info.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(60f, -310f), new Vector2(-60f, -218f));
        }

        private void Hide()
        {
            if (_overlayCanvasObject != null)
                _overlayCanvasObject.SetActive(false);
            if (_sourceSettingsCanvasObject != null)
                _sourceSettingsCanvasObject.SetActive(true);
            _logger.LogInfo(
                "[ModManager] Closed manager canvas and restored Canvas_Settings.");
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
                new Vector2(48f, 190f), new Vector2(-48f, -330f));
            UiFactory.Image(viewport, new Color(0f, 0f, 0f, 0.16f));
            viewport.AddComponent<RectMask2D>();

            ScrollRect scroll = viewport.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 68f;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            GameObject contentObject = UiFactory.Object("Content", viewport.transform);
            _content = UiFactory.Rect(contentObject,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                Vector2.zero, Vector2.zero);
            _content.pivot = new Vector2(0.5f, 1f);

            VerticalLayoutGroup layout = contentObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 20, 20);
            layout.spacing = 20f;
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
            _footer = UiFactory.TmpText(
                "Footer",
                _panel.transform,
                "",
                48f,
                TextAlignmentOptions.MidlineLeft,
                MutedText);
            UiFactory.Rect(_footer.gameObject, Vector2.zero, new Vector2(1f, 0f),
                new Vector2(60f, 70f), new Vector2(-60f, 190f));
        }

        private void CreateModRow(ManagedMod mod)
        {
            GameObject row = UiFactory.Object("Mod_" + mod.Guid, _content);
            _rows.Add(row);
            Image rowImage = UiFactory.Image(row, RowColor);
            if (_rowBackgroundSprite != null)
            {
                rowImage.sprite = _rowBackgroundSprite;
                rowImage.type = Image.Type.Sliced;
                rowImage.color = Color.white;
            }
            LayoutElement element = row.AddComponent<LayoutElement>();
            element.preferredHeight = 188f;
            element.minHeight = 188f;

            TMP_Text name = UiFactory.TmpText("Name", row.transform,
                mod.Name + (string.IsNullOrEmpty(mod.Version) ? "" : "  v" + mod.Version),
                44f, TextAlignmentOptions.MidlineLeft, Color.white);
            name.fontStyle = FontStyles.Bold;
            UiFactory.Rect(name.gameObject, Vector2.zero, Vector2.one,
                new Vector2(36f, 84f), new Vector2(-260f, -16f));

            TMP_Text identity = UiFactory.TmpText(
                "Guid",
                row.transform,
                mod.Guid,
                28f,
                TextAlignmentOptions.MidlineLeft,
                MutedText);
            UiFactory.Rect(identity.gameObject, Vector2.zero, Vector2.one,
                new Vector2(36f, 40f), new Vector2(-260f, -96f));

            TMP_Text status = UiFactory.TmpText(
                "Status",
                row.transform,
                StatusText(mod),
                26f,
                TextAlignmentOptions.BottomLeft,
                StatusColor(mod));
            status.fontStyle = FontStyles.Bold;
            UiFactory.Rect(status.gameObject, Vector2.zero, Vector2.one,
                new Vector2(36f, 14f), new Vector2(-260f, -136f));

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
                new Vector2(-216f, -44f), new Vector2(-56f, 44f));
            Image background = UiFactory.Image(root, HeaderText);
            Outline outline = root.AddComponent<Outline>();
            outline.effectColor = Accent;
            outline.effectDistance = new Vector2(3f, -3f);

            GameObject checkObject = UiFactory.Object("Enabled", root.transform);
            UiFactory.Rect(checkObject, Vector2.zero, Vector2.one,
                new Vector2(10f, 10f), new Vector2(-10f, -10f));
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

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
        private static readonly Color DividerColor = new Color(
            187f / 255f,
            132f / 255f,
            119f / 255f,
            1f);
        private static readonly Color HeaderText = new Color(
            16f / 255f,
            11f / 255f,
            10f / 255f,
            1f);
        private static readonly Color InactiveOptionBackground = new Color(
            70f / 255f,
            39f / 255f,
            38f / 255f,
            1f);

        private readonly ManualLogSource _logger;
        private readonly ModRegistry _registry;
        private readonly UiAssets _assets;
        private readonly List<GameObject> _rows = new List<GameObject>();
        private GameObject _overlayCanvasObject;
        private GameObject _canvasObject;
        private GameObject _nativeCloseObject;
        private GameObject _sourceSettingsCanvasObject;
        private GameObject _panel;
        private RectTransform _content;
        private RectTransform _listViewport;
        private GridLayoutGroup _gridLayout;
        private TMP_Text _footer;
        private Sprite _rowBackgroundSprite;
        private Sprite _roundedScrollbarSprite;
        private Sprite _settingsIconSprite;
        private GameObject _settingsPopup;
        private bool _loggedMissingSettingsIcon;

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
            _canvasObject.transform.SetAsLastSibling();
            _canvasObject.SetActive(true);
            _panel.transform.SetAsLastSibling();
            _panel.SetActive(true);
            if (_nativeCloseObject != null)
            {
                _nativeCloseObject.transform.SetAsLastSibling();
                _nativeCloseObject.SetActive(true);
            }
            Canvas.ForceUpdateCanvases();
            UpdateGridLayout();
            _sourceSettingsCanvasObject.SetActive(false);
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
            CanvasGroup interactionGroup = _panel.AddComponent<CanvasGroup>();
            interactionGroup.alpha = 1f;
            interactionGroup.interactable = true;
            interactionGroup.blocksRaycasts = true;
            interactionGroup.ignoreParentGroups = true;

            string gameFont = UiFactory.CaptureGameFont(settingsCanvasRoot);
            if (gameFont == null)
                _logger.LogWarning(
                    "[ModManager] No compatible game TMP font found; using TMP default.");
            _rowBackgroundSprite =
                _assets.LoadEmbeddedSprite("UI_BG_Paper.png", 24f);
            CreateHeader();
            CreateInfo();
            CreateList();
            CreateFooter();
            _panel.transform.SetParent(overlayParent, true);
            _panel.transform.SetAsLastSibling();
            _overlayCanvasObject.SetActive(false);
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
                graphic.raycastTarget = true;
            backdrop.transform.SetAsFirstSibling();
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
            }

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
            }

            clonedBackground.gameObject.SetActive(true);
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
            UiFactory.Image(line, DividerColor, false);
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
            DestroySettingsPopup();
            if (_overlayCanvasObject != null)
                _overlayCanvasObject.SetActive(false);
            if (_sourceSettingsCanvasObject != null)
                _sourceSettingsCanvasObject.SetActive(true);
        }

        private void RefreshRows()
        {
            DestroySettingsPopup();
            foreach (GameObject row in _rows)
            {
                if (row != null)
                    row.transform.SetParent(null, false);
                Object.Destroy(row);
            }
            _rows.Clear();
            foreach (ManagedMod mod in _registry.Mods) CreateModRow(mod);
            _footer.text = _registry.Mods.Count == 0
                ? "No mods integrated with the Mod Manager API were found."
                : "Only API-integrated mods appear here. Mod Manager is always enabled.";
        }

        private void CreateList()
        {
            GameObject scrollObject =
                UiFactory.Object("ModListScroll", _panel.transform);
            RectTransform scrollRectTransform = UiFactory.Rect(
                scrollObject,
                Vector2.zero,
                Vector2.one,
                new Vector2(48f, 190f), new Vector2(-48f, -330f));

            ScrollRect scroll = scrollObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.inertia = true;
            scroll.decelerationRate = 0.135f;
            scroll.elasticity = 0.12f;
            scroll.scrollSensitivity = 72f;
            scroll.movementType = ScrollRect.MovementType.Elastic;

            GameObject viewport = UiFactory.Object("Viewport", scrollObject.transform);
            _listViewport = UiFactory.Rect(
                viewport,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                new Vector2(-48f, 0f));
            UiFactory.Image(viewport, new Color(0f, 0f, 0f, 0.16f));
            viewport.AddComponent<RectMask2D>();

            GameObject contentObject = UiFactory.Object("Content", viewport.transform);
            _content = UiFactory.Rect(contentObject,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                Vector2.zero, Vector2.zero);
            _content.pivot = new Vector2(0.5f, 1f);

            _gridLayout = contentObject.AddComponent<GridLayoutGroup>();
            _gridLayout.padding = new RectOffset(20, 20, 20, 20);
            _gridLayout.spacing = new Vector2(20f, 20f);
            _gridLayout.cellSize = new Vector2(720f, 188f);
            _gridLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;
            _gridLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
            _gridLayout.childAlignment = TextAnchor.UpperLeft;
            _gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            _gridLayout.constraintCount = 2;

            ContentSizeFitter fitter = contentObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content = _content;
            scroll.verticalScrollbar = CreateVerticalScrollbar(
                scrollObject.transform);
            scroll.verticalScrollbarVisibility =
                ScrollRect.ScrollbarVisibility.Permanent;
            scroll.verticalScrollbarSpacing = 12f;
        }

        private Scrollbar CreateVerticalScrollbar(Transform parent)
        {
            Sprite roundedSprite = GetRoundedScrollbarSprite();
            GameObject trackObject = UiFactory.Object(
                "VerticalScrollbar",
                parent);
            UiFactory.Rect(
                trackObject,
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(-34f, 12f),
                new Vector2(-8f, -12f));
            Image track = UiFactory.Image(
                trackObject,
                new Color(
                    233f / 255f,
                    199f / 255f,
                    175f / 255f,
                    1f));
            track.sprite = roundedSprite;
            track.type = Image.Type.Sliced;

            GameObject slidingArea = UiFactory.Object(
                "Sliding Area",
                trackObject.transform);
            UiFactory.Rect(
                slidingArea,
                Vector2.zero,
                Vector2.one,
                new Vector2(3f, 3f),
                new Vector2(-3f, -3f));

            GameObject handleObject = UiFactory.Object(
                "Handle",
                slidingArea.transform);
            RectTransform handleRect = UiFactory.Rect(
                handleObject,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero);
            Image handle = UiFactory.Image(
                handleObject,
                new Color(
                    70f / 255f,
                    39f / 255f,
                    38f / 255f,
                    1f));
            handle.sprite = roundedSprite;
            handle.type = Image.Type.Sliced;

            Scrollbar scrollbar = trackObject.AddComponent<Scrollbar>();
            scrollbar.targetGraphic = handle;
            scrollbar.handleRect = handleRect;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.numberOfSteps = 0;
            ColorBlock colors = scrollbar.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.92f, 0.82f, 1f);
            colors.pressedColor = new Color(0.78f, 0.68f, 0.60f, 1f);
            colors.selectedColor = colors.highlightedColor;
            scrollbar.colors = colors;
            return scrollbar;
        }

        private Sprite GetRoundedScrollbarSprite()
        {
            if (_roundedScrollbarSprite != null)
                return _roundedScrollbarSprite;

            const int size = 32;
            const float radius = 12f;
            var texture = new Texture2D(
                size,
                size,
                TextureFormat.RGBA32,
                false);
            texture.name = "ModManager_RoundedScrollbar";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = Mathf.Abs(x + 0.5f - size * 0.5f) -
                               (size * 0.5f - radius);
                    float py = Mathf.Abs(y + 0.5f - size * 0.5f) -
                               (size * 0.5f - radius);
                    float outsideX = Mathf.Max(px, 0f);
                    float outsideY = Mathf.Max(py, 0f);
                    float distance = Mathf.Sqrt(
                        outsideX * outsideX + outsideY * outsideY) +
                        Mathf.Min(Mathf.Max(px, py), 0f) - radius;
                    byte alpha = (byte)Mathf.RoundToInt(
                        Mathf.Clamp01(0.5f - distance) * 255f);
                    pixels[y * size + x] =
                        new Color32(255, 255, 255, alpha);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            _roundedScrollbarSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(radius, radius, radius, radius));
            _roundedScrollbarSprite.name = "ModManager_RoundedScrollbar";
            return _roundedScrollbarSprite;
        }

        private void UpdateGridLayout()
        {
            if (_gridLayout == null || _listViewport == null)
                return;

            float availableWidth =
                _listViewport.rect.width -
                _gridLayout.padding.left -
                _gridLayout.padding.right -
                _gridLayout.spacing.x;
            if (availableWidth <= 0f)
                return;

            float cellWidth = availableWidth * 0.5f;
            _gridLayout.cellSize = new Vector2(cellWidth, 188f);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
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
            IReadOnlyList<RegisteredChoiceSetting> settings =
                ModManagerApi.GetChoiceSettings(mod.Guid);
            bool hasSettings = settings.Count > 0;
            float textRightInset = hasSettings ? -360f : -260f;

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
                new Vector2(36f, 84f), new Vector2(textRightInset, -16f));

            TMP_Text identity = UiFactory.TmpText(
                "Guid",
                row.transform,
                mod.Guid,
                28f,
                TextAlignmentOptions.MidlineLeft,
                MutedText);
            UiFactory.Rect(identity.gameObject, Vector2.zero, Vector2.one,
                new Vector2(36f, 40f), new Vector2(textRightInset, -96f));

            TMP_Text status = UiFactory.TmpText(
                "Status",
                row.transform,
                StatusText(mod),
                26f,
                TextAlignmentOptions.BottomLeft,
                StatusColor(mod));
            status.fontStyle = FontStyles.Bold;
            UiFactory.Rect(status.gameObject, Vector2.zero, Vector2.one,
                new Vector2(36f, 14f), new Vector2(textRightInset, -136f));

            if (hasSettings)
            {
                Button gear = CreateSettingsGear(row.transform);
                RectTransform gearRect = gear.GetComponent<RectTransform>();
                gear.onClick.AddListener(() =>
                    ShowSettingsPopup(mod, settings, gearRect));
            }

            bool desiredState = mod.DesiredEnabled;
            Image enabledVisual;
            Button toggle = CreateToggle(
                row.transform,
                desiredState,
                out enabledVisual);
            toggle.onClick.AddListener(() =>
            {
                bool enabled = !desiredState;
                try
                {
                    _footer.text = _registry.SetDesiredState(mod, enabled);
                    desiredState = enabled;
                    SetToggleVisual(enabledVisual, desiredState);
                    status.text = StatusText(mod);
                    status.color = StatusColor(mod);
                }
                catch (System.Exception exception)
                {
                    _logger.LogError("[ModManager] Could not change " + mod.Name + ": " + exception);
                    desiredState = mod.DesiredEnabled;
                    SetToggleVisual(enabledVisual, desiredState);
                    _footer.text = "The state could not be saved. Check BepInEx\\LogOutput.log.";
                }
            });
        }

        private Button CreateSettingsGear(Transform parent)
        {
            GameObject root = UiFactory.Object("SettingsGear", parent);
            UiFactory.Rect(root, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-336f, -42f), new Vector2(-248f, 42f));
            Image icon = UiFactory.Image(root, Color.white);
            icon.preserveAspect = true;
            icon.sprite = FindSettingsIconSprite();
            icon.color = new Color(
                70f / 255f,
                39f / 255f,
                38f / 255f,
                1f);

            Button button = root.AddComponent<Button>();
            button.targetGraphic = icon;
            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.86f, 0.52f, 1f);
            colors.pressedColor = new Color(0.78f, 0.62f, 0.30f, 1f);
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;

            if (icon.sprite == null)
            {
                icon.color = new Color(0f, 0f, 0f, 0.01f);
                Text fallback = UiFactory.Text(
                    "GearFallback",
                    root.transform,
                    "⚙",
                    48,
                    TextAnchor.MiddleCenter,
                    new Color(
                        70f / 255f,
                        39f / 255f,
                        38f / 255f,
                        1f));
                UiFactory.Rect(
                    fallback.gameObject,
                    Vector2.zero,
                    Vector2.one,
                    Vector2.zero,
                    Vector2.zero);
            }
            return button;
        }

        private Sprite FindSettingsIconSprite()
        {
            if (_settingsIconSprite != null)
                return _settingsIconSprite;

            foreach (Sprite sprite in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (sprite != null &&
                    string.Equals(
                        sprite.name,
                        "UI_Icon_Settings",
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    _settingsIconSprite = sprite;
                    _loggedMissingSettingsIcon = false;
                    return _settingsIconSprite;
                }
            }

            if (!_loggedMissingSettingsIcon)
            {
                _loggedMissingSettingsIcon = true;
                _logger.LogWarning(
                    "[ModManager] UI_Icon_Settings is not loaded; using the text gear fallback.");
            }
            return null;
        }

        private void ShowSettingsPopup(
            ManagedMod mod,
            IReadOnlyList<RegisteredChoiceSetting> settings,
            RectTransform gearRect)
        {
            DestroySettingsPopup();
            if (_overlayCanvasObject == null || gearRect == null)
                return;

            _settingsPopup = UiFactory.Object(
                "ModSettings_" + mod.Guid,
                _overlayCanvasObject.transform);
            RectTransform popupRect = UiFactory.Rect(
                _settingsPopup,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                Vector2.zero);
            popupRect.sizeDelta = new Vector2(840f, 520f);
            PositionPopupAtGear(popupRect, gearRect);

            Image popupBackground = UiFactory.Image(_settingsPopup, RowColor);
            if (_rowBackgroundSprite != null)
            {
                popupBackground.sprite = _rowBackgroundSprite;
                popupBackground.type = Image.Type.Sliced;
                popupBackground.color = Color.white;
            }
            Outline popupOutline = _settingsPopup.AddComponent<Outline>();
            popupOutline.effectColor = Accent;
            popupOutline.effectDistance = new Vector2(4f, -4f);
            CanvasGroup group = _settingsPopup.AddComponent<CanvasGroup>();
            group.alpha = 1f;
            group.interactable = true;
            group.blocksRaycasts = true;
            group.ignoreParentGroups = true;

            TMP_Text title = UiFactory.TmpText(
                "Title",
                _settingsPopup.transform,
                mod.Name.ToUpperInvariant(),
                38f,
                TextAlignmentOptions.MidlineLeft,
                Color.white);
            title.fontStyle = FontStyles.Bold;
            UiFactory.Rect(title.gameObject, Vector2.zero, Vector2.one,
                new Vector2(34f, 418f), new Vector2(-98f, -24f));

            GameObject divider = UiFactory.Object(
                "HeaderDivider",
                _settingsPopup.transform);
            UiFactory.Rect(divider, Vector2.zero, Vector2.one,
                new Vector2(28f, 400f), new Vector2(-28f, -112f));
            UiFactory.Image(divider, DividerColor, false);

            Button close = CreatePopupCloseButton(_settingsPopup.transform);
            close.onClick.AddListener(DestroySettingsPopup);
            CreateSettingsPopupBody(settings);

            TMP_Text note = UiFactory.TmpText(
                "ApplyNote",
                _settingsPopup.transform,
                "Settings apply immediately.",
                24f,
                TextAlignmentOptions.MidlineLeft,
                MutedText);
            UiFactory.Rect(note.gameObject, Vector2.zero, Vector2.one,
                new Vector2(34f, 18f), new Vector2(-34f, -454f));

            _settingsPopup.transform.SetAsLastSibling();
        }

        private static void PositionPopupAtGear(
            RectTransform popupRect,
            RectTransform gearRect)
        {
            RectTransform overlayRect = popupRect.parent as RectTransform;
            if (overlayRect == null)
                return;

            var gearCorners = new Vector3[4];
            gearRect.GetWorldCorners(gearCorners);
            Vector3 bottomLeft3 = overlayRect.InverseTransformPoint(gearCorners[0]);
            Vector3 topLeft3 = overlayRect.InverseTransformPoint(gearCorners[1]);
            Vector3 topRight3 = overlayRect.InverseTransformPoint(gearCorners[2]);
            Vector3 bottomRight3 = overlayRect.InverseTransformPoint(gearCorners[3]);
            Vector2 bottomLeft = new Vector2(bottomLeft3.x, bottomLeft3.y);
            Vector2 topLeft = new Vector2(topLeft3.x, topLeft3.y);
            Vector2 topRight = new Vector2(topRight3.x, topRight3.y);
            Vector2 bottomRight = new Vector2(bottomRight3.x, bottomRight3.y);

            Rect bounds = overlayRect.rect;
            bool extendLeft = bottomLeft.x - bounds.xMin >=
                              bounds.xMax - bottomRight.x;
            bool extendDown = bottomLeft.y - bounds.yMin >=
                              bounds.yMax - topLeft.y;
            popupRect.pivot = new Vector2(
                extendLeft ? 1f : 0f,
                extendDown ? 1f : 0f);
            popupRect.anchoredPosition = new Vector2(
                extendLeft ? bottomLeft.x : bottomRight.x,
                extendDown ? bottomLeft.y : topRight.y);
        }

        private Button CreatePopupCloseButton(Transform parent)
        {
            if (_nativeCloseObject != null)
            {
                GameObject nativeClone = Object.Instantiate(
                    _nativeCloseObject,
                    parent,
                    false);
                nativeClone.name = "ModSettings_Btn_Close";
                nativeClone.SetActive(true);
                RectTransform nativeRect =
                    nativeClone.GetComponent<RectTransform>();
                if (nativeRect != null)
                {
                    nativeRect.anchorMin = Vector2.one;
                    nativeRect.anchorMax = Vector2.one;
                    nativeRect.pivot = new Vector2(0.5f, 0.5f);
                    nativeRect.anchoredPosition = new Vector2(-54f, -54f);
                    nativeRect.sizeDelta = new Vector2(76f, 76f);
                    nativeRect.localScale = Vector3.one;
                }

                Button nativeButton =
                    nativeClone.GetComponent<Button>() ??
                    nativeClone.GetComponentInChildren<Button>(true);
                if (nativeButton != null)
                {
                    nativeButton.onClick = new Button.ButtonClickedEvent();
                    nativeButton.interactable = true;
                    return nativeButton;
                }

                Object.Destroy(nativeClone);
            }

            GameObject root = UiFactory.Object("Close", parent);
            UiFactory.Rect(root, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-82f, -82f), new Vector2(-22f, -22f));
            Image background = UiFactory.Image(root, HeaderText);
            Outline outline = root.AddComponent<Outline>();
            outline.effectColor = Accent;
            outline.effectDistance = new Vector2(2f, -2f);
            Button button = root.AddComponent<Button>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.ColorTint;
            TMP_Text label = UiFactory.TmpText(
                "Label",
                root.transform,
                "X",
                34f,
                TextAlignmentOptions.Center,
                Color.white);
            label.fontStyle = FontStyles.Bold;
            ForceTmpColor(label, Color.white);
            UiFactory.Rect(label.gameObject, Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero);
            return button;
        }

        private void CreateSettingsPopupBody(
            IReadOnlyList<RegisteredChoiceSetting> settings)
        {
            GameObject scrollObject = UiFactory.Object(
                "SettingsScroll",
                _settingsPopup.transform);
            UiFactory.Rect(scrollObject, Vector2.zero, Vector2.one,
                new Vector2(26f, 68f), new Vector2(-26f, -126f));
            ScrollRect scroll = scrollObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.inertia = true;
            scroll.scrollSensitivity = 80f;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            GameObject viewport = UiFactory.Object("Viewport", scrollObject.transform);
            RectTransform viewportRect = UiFactory.Rect(
                viewport,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero);
            UiFactory.Image(viewport, new Color(0f, 0f, 0f, 0.18f));
            viewport.AddComponent<RectMask2D>();

            GameObject contentObject = UiFactory.Object("Content", viewport.transform);
            RectTransform content = UiFactory.Rect(
                contentObject,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                Vector2.zero,
                Vector2.zero);
            content.pivot = new Vector2(0.5f, 1f);
            VerticalLayoutGroup layout = contentObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 14, 14);
            layout.spacing = 18f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            ContentSizeFitter fitter = contentObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            foreach (RegisteredChoiceSetting setting in settings)
                CreateChoiceSetting(setting, content);

            scroll.viewport = viewportRect;
            scroll.content = content;
        }

        private void CreateChoiceSetting(
            RegisteredChoiceSetting setting,
            Transform parent)
        {
            GameObject group = UiFactory.Object("Setting_" + setting.Key, parent);
            LayoutElement layout = group.AddComponent<LayoutElement>();
            layout.preferredHeight = 148f;
            layout.minHeight = 148f;

            TMP_Text label = UiFactory.TmpText(
                "Label",
                group.transform,
                setting.DisplayName,
                28f,
                TextAlignmentOptions.MidlineLeft,
                MutedText);
            label.fontStyle = FontStyles.Bold;
            UiFactory.Rect(label.gameObject, Vector2.zero, Vector2.one,
                new Vector2(8f, 98f), new Vector2(-8f, -4f));

            var visuals = new List<ChoiceVisual>();
            int count = setting.Options.Count;
            for (int index = 0; index < count; index++)
            {
                ModChoiceOption option = setting.Options[index];
                float min = index / (float)count;
                float max = (index + 1) / (float)count;
                GameObject optionObject = UiFactory.Object(
                    "Option_" + option.Value,
                    group.transform);
                UiFactory.Rect(
                    optionObject,
                    new Vector2(min, 0f),
                    new Vector2(max, 0f),
                    new Vector2(8f, 8f),
                    new Vector2(-8f, 88f));
                Image background = UiFactory.Image(optionObject, HeaderText);
                Outline outline = optionObject.AddComponent<Outline>();
                outline.effectDistance = new Vector2(2f, -2f);
                Button button = optionObject.AddComponent<Button>();
                button.targetGraphic = background;
                button.transition = Selectable.Transition.ColorTint;
                TMP_Text optionLabel = UiFactory.TmpText(
                    "Label",
                    optionObject.transform,
                    option.Label,
                    28f,
                    TextAlignmentOptions.Center,
                    Color.white);
                optionLabel.fontStyle = FontStyles.Bold;
                ForceTmpColor(optionLabel, Color.white);
                UiFactory.Rect(optionLabel.gameObject, Vector2.zero, Vector2.one,
                    new Vector2(8f, 4f), new Vector2(-8f, -4f));

                var visual = new ChoiceVisual(
                    option.Value,
                    background,
                    outline,
                    optionLabel);
                visuals.Add(visual);
                ModChoiceOption selectedOption = option;
                button.onClick.AddListener(() =>
                {
                    try
                    {
                        setting.Select(selectedOption.Value);
                        RefreshChoiceVisuals(visuals, setting.CurrentValue);
                    }
                    catch (System.Exception exception)
                    {
                        _logger.LogError(
                            "[ModManager] Could not save setting " +
                            setting.Key + ": " + exception);
                        _footer.text =
                            "The setting could not be saved. Check BepInEx\\LogOutput.log.";
                    }
                });
            }

            RefreshChoiceVisuals(visuals, setting.CurrentValue);
        }

        private static void RefreshChoiceVisuals(
            IEnumerable<ChoiceVisual> visuals,
            string currentValue)
        {
            foreach (ChoiceVisual visual in visuals)
            {
                bool selected = string.Equals(
                    visual.Value,
                    currentValue,
                    System.StringComparison.Ordinal);
                visual.Background.color = selected
                    ? Accent
                    : InactiveOptionBackground;
                visual.Outline.effectColor = selected ? HeaderText : Accent;
                ForceTmpColor(
                    visual.Label,
                    selected ? HeaderText : Color.white);
            }
        }

        private static void ForceTmpColor(TMP_Text text, Color color)
        {
            if (text == null)
                return;

            color.a = 1f;
            text.alpha = 1f;
            text.enableVertexGradient = false;
            text.color = color;
            text.faceColor = color;
            text.colorGradient = new VertexGradient(color);
            if (text.fontMaterial != null)
                text.fontMaterial.SetColor(
                    ShaderUtilities.ID_FaceColor,
                    Color.white);
            text.ForceMeshUpdate(true, true);
            text.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
            text.SetVerticesDirty();
        }

        private void DestroySettingsPopup()
        {
            if (_settingsPopup == null)
                return;

            _settingsPopup.SetActive(false);
            Object.Destroy(_settingsPopup);
            _settingsPopup = null;
        }

        private sealed class ChoiceVisual
        {
            internal ChoiceVisual(
                string value,
                Image background,
                Outline outline,
                TMP_Text label)
            {
                Value = value;
                Background = background;
                Outline = outline;
                Label = label;
            }

            internal string Value { get; }
            internal Image Background { get; }
            internal Outline Outline { get; }
            internal TMP_Text Label { get; }
        }

        private static Button CreateToggle(
            Transform parent,
            bool enabled,
            out Image enabledVisual)
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
            Image check = UiFactory.Image(
                checkObject,
                new Color(233f / 255f, 155f / 255f, 22f / 255f, 1f),
                false);

            Button button = root.AddComponent<Button>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.88f, 0.55f, 1f);
            colors.pressedColor = new Color(0.82f, 0.58f, 0.16f, 1f);
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;
            button.interactable = true;
            enabledVisual = check;
            SetToggleVisual(enabledVisual, enabled);
            return button;
        }

        private static void SetToggleVisual(Image visual, bool enabled)
        {
            if (visual != null)
                visual.gameObject.SetActive(enabled);
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

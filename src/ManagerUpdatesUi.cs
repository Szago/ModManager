using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ModManager
{
    internal sealed partial class ManagerUi
    {
        private readonly UpdateService _updates;
        private readonly List<UpdateControls> _updateControls = new List<UpdateControls>();
        private readonly List<Button> _bulkUpdateButtons = new List<Button>();
        private int _updateRevision = -1;
        private TMP_Text _headerTitle;
        private static readonly Color UpdateGreen = new Color(0.10f, 0.30f, 0.12f, 1f);
        private static readonly Color UpdateRed = new Color(0.48f, 0.07f, 0.06f, 1f);
        private static readonly Color BulkButtonText = new Color(1f, 249f / 255f, 179f / 255f, 1f);

        private sealed class UpdateControls
        {
            internal string Guid;
            internal bool IsHeader;
            internal TMP_Text Version, Status;
            internal Button Refresh, Update;
        }

        internal void TickUpdates()
        {
            if (_panel == null || _updateRevision == _updates.Revision) return;
            _updateRevision = _updates.Revision;
            RefreshUpdateControls();
            if (_footer != null && !string.IsNullOrEmpty(_updates.Message)) _footer.text = _updates.Message;
        }

        private void RefreshUpdateControls()
        {
            foreach (UpdateControls controls in _updateControls)
            {
                if (controls.Status == null) continue;
                UpdateState state = _updates.Get(controls.Guid);
                controls.Status.text = state?.Status ?? "Not configured";
                Color color = state != null && state.Current ? UpdateGreen :
                    state != null && state.Available ? UpdateRed : InactiveOptionBackground;
                ForceTmpColor(controls.Status, color);
                controls.Refresh.interactable = !_updates.Busy && state?.Source != null && !state.Staged;
                controls.Update.gameObject.SetActive(state != null && state.Available);
                controls.Update.interactable = !_updates.Busy;
            }
            foreach (Button button in _bulkUpdateButtons) if (button != null) button.interactable = !_updates.Busy;
            FitVisibleUpdateLabels();
        }

        private void FitVisibleUpdateLabels()
        {
            if (_panel == null || !_panel.activeInHierarchy) return;
            Canvas.ForceUpdateCanvases();
            FitVisibleLabel(_headerTitle);
            foreach (UpdateControls controls in _updateControls)
            {
                FitVisibleLabel(controls.Version);
                FitVisibleLabel(controls.Status);
                if (controls.Version != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)controls.Version.transform.parent);
            }
        }

        private static void FitVisibleLabel(TMP_Text text)
        {
            if (text == null || !text.isActiveAndEnabled) return;
            // Measure only after activation, using unconstrained preferred size
            // so an ellipsized initial slot does not become permanently narrow.
            text.ForceMeshUpdate();
            float width = text.GetPreferredValues(text.text, Mathf.Infinity, Mathf.Infinity).x;
            if (float.IsNaN(width) || float.IsInfinity(width) || width < 8f) return;
            LayoutElement slot = text.GetComponent<LayoutElement>();
            slot.minWidth = slot.preferredWidth = Mathf.Ceil(width) + 4f;
            slot.flexibleWidth = 0f;
            text.SetVerticesDirty();
        }

        private void CreateUpdateControls(Transform parent, string guid, bool header)
        {
            UpdateState state = _updates.Get(guid);
            HorizontalLayoutGroup layout = parent.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 32f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;
            TMP_Text version = UiFactory.TmpText("Version", parent,
                "v" + (header ? Plugin.PluginVersion : state?.Version ?? ""),
                header ? 36f : 30f, TextAlignmentOptions.MidlineLeft,
                header ? HeaderText : InactiveOptionBackground);
            ForceTmpColor(version, header ? HeaderText : InactiveOptionBackground);
            // These labels are built while the shell is inactive; measuring TMP
            // now can collapse their width to zero and hide the whole string.
            LayoutSlot(version.gameObject, header ? 150f : 116f, header ? 80f : 54f);

            Button refresh = CreateInfoButton(parent, out RectTransform refreshRect);
            refreshRect.name = "CheckForUpdate";
            LayoutSlot(refreshRect.gameObject, 50f, 50f);
            refresh.onClick.AddListener(() => _updates.Check(new[] { guid }, false));

            TMP_Text status = UiFactory.TmpText("UpdateStatus", parent, state?.Status ?? "Not configured",
                header ? 34f : 30f, TextAlignmentOptions.MidlineLeft, InactiveOptionBackground);
            LayoutElement statusLayout = LayoutSlot(status.gameObject, 260f, header ? 80f : 54f);
            statusLayout.minWidth = 60f;
            statusLayout.flexibleWidth = 1f;

            Button update = CreateNativeActionButton(parent, "Update", () => _updates.Check(new[] { guid }, true));
            LayoutSlot(update.gameObject, 132f, 52f);
            _updateControls.Add(new UpdateControls
            {
                Guid = guid, IsHeader = header, Version = version, Status = status, Refresh = refresh, Update = update
            });
        }

        private static LayoutElement LayoutSlot(GameObject target, float width, float height)
        {
            LayoutElement slot = target.GetComponent<LayoutElement>() ?? target.AddComponent<LayoutElement>();
            slot.ignoreLayout = false;
            slot.minWidth = width;
            slot.preferredWidth = width;
            slot.minHeight = height;
            slot.preferredHeight = height;
            slot.flexibleWidth = 0f;
            slot.flexibleHeight = 0f;
            return slot;
        }

        private void CreateBulkControls()
        {
            GameObject bar = UiFactory.Object("BulkActions", _panel.transform);
            UiFactory.Rect(bar, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-860f, -302f), new Vector2(-60f, -230f));
            HorizontalLayoutGroup layout = bar.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 16f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = true;
            layout.childForceExpandWidth = true;
            Button updateAll = CreateNativeActionButton(bar.transform, "Update all", () =>
                _updates.Check(AllUpdateGuids(), true), BulkButtonText);
            LayoutSlot(updateAll.gameObject, 220f, 70f);
            Button checkAll = CreateNativeActionButton(bar.transform, "Check for updates", () =>
                _updates.Check(AllUpdateGuids(), false), BulkButtonText);
            LayoutSlot(checkAll.gameObject, 300f, 70f);
            Button toggleAll = CreateNativeActionButton(bar.transform, "Toggle all", ToggleAll, BulkButtonText);
            LayoutSlot(toggleAll.gameObject, 220f, 70f);
            _bulkUpdateButtons.Add(updateAll);
            _bulkUpdateButtons.Add(checkAll);
        }

        private IEnumerable<string> AllUpdateGuids() =>
            new[] { Plugin.PluginGuid }.Concat(_registry.Mods.Select(mod => mod.Guid));

        private void ToggleAll()
        {
            IReadOnlyList<ManagedMod> mods = _registry.Mods;
            if (mods.Count == 0) return;
            // Mixed/off -> all enabled; all enabled -> all disabled. Manager is never in this list.
            bool enabled = !mods.All(mod => mod.DesiredEnabled);
            try
            {
                _registry.SetAllDesiredStates(enabled);
                RefreshRows();
                _footer.text = (enabled ? "All mods enabled." : "All mods disabled.") + " Restart the game to apply.";
            }
            catch (Exception exception)
            {
                _logger.LogError("[ModManager] Could not save all mod states: " + exception);
                _registry.Discover();
                RefreshRows();
                _footer.text = "Could not save all states. Check BepInEx/LogOutput.log.";
            }
        }

        private Button CreateNativeActionButton(Transform parent, string label, UnityEngine.Events.UnityAction action,
            Color? labelColor = null)
        {
            Button source = SettingsButton.FindClaimButton();
            Button button;
            if (source == null)
            {
                button = UiFactory.Button(label, parent, "", action);
                foreach (Graphic graphic in button.GetComponentsInChildren<Graphic>(true))
                    if (graphic is Text) graphic.gameObject.SetActive(false);
            }
            else
            {
                bool active = source.gameObject.activeSelf;
                source.gameObject.SetActive(false);
                try { button = Object.Instantiate(source, parent, false); }
                finally { source.gameObject.SetActive(active); }
                button.name = label.Replace(" ", "");
                foreach (MonoBehaviour component in button.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (!(component is Graphic) && !(component is Button) && !(component is BaseMeshEffect))
                        Object.DestroyImmediate(component);
                }
                foreach (Animator animator in button.GetComponentsInChildren<Animator>(true))
                    Object.DestroyImmediate(animator);
                foreach (Graphic graphic in button.GetComponentsInChildren<Graphic>(true))
                {
                    graphic.raycastTarget = graphic == button.targetGraphic;
                    if (graphic is TMP_Text || graphic is Text) graphic.gameObject.SetActive(false);
                }
                button.onClick = new Button.ButtonClickedEvent();
                button.onClick.AddListener(action);
                button.navigation = new Navigation { mode = Navigation.Mode.None };
                button.transition = Selectable.Transition.ColorTint;
                button.colors = ColorBlock.defaultColorBlock;
                button.interactable = true;
                button.transform.localScale = Vector3.one;
                button.transform.localRotation = Quaternion.identity;
                button.gameObject.SetActive(true);
            }
            TMP_Text text = UiFactory.TmpText("ActionLabel", button.transform, label, 30f,
                TextAlignmentOptions.Center, labelColor ?? Color.white);
            ForceTmpColor(text, labelColor ?? Color.white);
            UiFactory.Rect(text.gameObject, Vector2.zero, Vector2.one, new Vector2(6f, 0f), new Vector2(-6f, 0f));
            return button;
        }
    }
}

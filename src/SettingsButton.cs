using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModManager
{
    internal static class SettingsButton
    {
        internal static Button Install(
            Button button30,
            Button button60,
            Button visualSource,
            Action onClick)
        {
            Transform parent = button60.transform.parent;
            SettingsButtonMarker marker =
                parent.GetComponentInChildren<SettingsButtonMarker>(true);
            if (marker != null)
                return marker.GetComponent<Button>();

            Button button = UnityEngine.Object.Instantiate(visualSource, parent, false);
            button.name = "ModManager_OpenButton";
            button.gameObject.AddComponent<SettingsButtonMarker>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => onClick());
            RemoveLocalizationComponents(button.gameObject);
            SetButtonLabel(button, "MOD MANAGER");

            LayoutElement layoutElement = button.GetComponent<LayoutElement>();
            if (layoutElement == null)
                layoutElement = button.gameObject.AddComponent<LayoutElement>();

            layoutElement.ignoreLayout = true;
            button.transform.SetAsLastSibling();

            RectTransform sourceRect = button60.transform as RectTransform;
            RectTransform buttonRect = button.transform as RectTransform;
            if (sourceRect != null && buttonRect != null)
            {
                buttonRect.anchorMin = sourceRect.anchorMin;
                buttonRect.anchorMax = sourceRect.anchorMax;
                buttonRect.pivot = sourceRect.pivot;
                buttonRect.sizeDelta = sourceRect.sizeDelta;
                buttonRect.SetSizeWithCurrentAnchors(
                    RectTransform.Axis.Horizontal,
                    360f);
                buttonRect.localScale = sourceRect.localScale;
                buttonRect.localRotation = sourceRect.localRotation;
                buttonRect.anchoredPosition =
                    sourceRect.anchoredPosition + new Vector2(-440f, -155f);
            }

            button.interactable = true;
            button.gameObject.SetActive(true);
            return button;
        }

        internal static Button FindClaimButton()
        {
            const string claimPath =
                "Canvas_Settings/Panel_Settings/Panel_Settings/Panel_Settings/" +
                "RighSide/Panel Promo Code/Button Claim";

            GameObject exactObject = GameObject.Find(claimPath);
            if (exactObject != null)
            {
                Button exactButton = exactObject.GetComponent<Button>() ??
                                     exactObject.GetComponentInChildren<Button>(true);
                if (exactButton != null)
                    return exactButton;
            }

            foreach (Button candidate in Resources.FindObjectsOfTypeAll<Button>())
            {
                if (candidate == null ||
                    !candidate.gameObject.scene.IsValid() ||
                    candidate.name != "Button Claim")
                    continue;

                bool insidePromoPanel = false;
                bool insideRightSide = false;
                Transform current = candidate.transform.parent;
                while (current != null)
                {
                    if (current.name == "Panel Promo Code")
                        insidePromoPanel = true;
                    if (current.name == "RighSide")
                        insideRightSide = true;
                    current = current.parent;
                }

                if (insidePromoPanel && insideRightSide)
                    return candidate;
            }

            return null;
        }

        internal static Transform FindSettingsShell(Transform origin)
        {
            Transform current = origin;
            while (current != null)
            {
                if (current.Find("Settings_BG") != null &&
                    current.Find("Panel Frame") != null)
                    return current;
                current = current.parent;
            }

            const string shellPath =
                "Canvas_Settings/Panel_Settings/Panel_Settings";
            GameObject exactObject = GameObject.Find(shellPath);
            return exactObject != null ? exactObject.transform : null;
        }

        internal static void SetButtonLabel(Button button, string label)
        {
            foreach (TextMeshProUGUI text in
                     button.GetComponentsInChildren<TextMeshProUGUI>(true))
                text.text = label;

            foreach (Text text in button.GetComponentsInChildren<Text>(true))
                text.text = label;
        }

        private static void RemoveLocalizationComponents(GameObject root)
        {
            foreach (MonoBehaviour component in
                     root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                Type type = component.GetType();
                if (type.Namespace == "I2.Loc" &&
                    type.Name.IndexOf(
                        "Localize",
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    UnityEngine.Object.Destroy(component);
                }
            }
        }
    }

    internal sealed class SettingsButtonMarker : MonoBehaviour
    {
    }
}

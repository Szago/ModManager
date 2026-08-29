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
            Action onClick)
        {
            Transform parent = button60.transform.parent;
            SettingsButtonMarker marker =
                parent.GetComponentInChildren<SettingsButtonMarker>(true);
            if (marker != null)
                return marker.GetComponent<Button>();

            Button button = UnityEngine.Object.Instantiate(button60, parent, false);
            button.name = "ModManager_TestButton";
            button.gameObject.AddComponent<SettingsButtonMarker>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => onClick());
            RemoveLocalizationComponents(button.gameObject);
            SetButtonLabel(button, "TEST");

            LayoutElement layoutElement = button.GetComponent<LayoutElement>();
            if (layoutElement == null)
                layoutElement = button.gameObject.AddComponent<LayoutElement>();

            layoutElement.ignoreLayout = true;
            button.transform.SetAsLastSibling();

            RectTransform sourceRect = button60.transform as RectTransform;
            RectTransform buttonRect = button.transform as RectTransform;
            if (sourceRect != null && buttonRect != null)
            {
                buttonRect.anchoredPosition =
                    sourceRect.anchoredPosition + new Vector2(-440f, -105f);
            }

            button.interactable = true;
            button.gameObject.SetActive(true);
            return button;
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

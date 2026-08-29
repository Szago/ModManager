using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ModManager
{
    internal static class UiFactory
    {
        private static Font _font;
        internal static Font Font => _font ?? (_font = Resources.GetBuiltinResource<Font>("Arial.ttf"));

        internal static GameObject Object(string name, Transform parent)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            gameObject.AddComponent<RectTransform>();
            return gameObject;
        }

        internal static RectTransform Rect(
            GameObject gameObject,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            RectTransform rect = gameObject.GetComponent<RectTransform>() ??
                                 gameObject.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return rect;
        }

        internal static Image Image(GameObject gameObject, Color color, bool raycast = true)
        {
            Image image = gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        internal static Text Text(
            string name,
            Transform parent,
            string value,
            int size,
            TextAnchor alignment,
            Color color)
        {
            GameObject gameObject = Object(name, parent);
            Text text = gameObject.AddComponent<Text>();
            text.font = Font;
            text.text = value;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            return text;
        }

        internal static Button Button(
            string name,
            Transform parent,
            string label,
            UnityAction onClick)
        {
            GameObject gameObject = Object(name, parent);
            Image image = Image(gameObject, new Color(0.22f, 0.19f, 0.27f, 1f));
            Button button = gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
            colors.pressedColor = new Color(0.72f, 0.72f, 0.72f, 1f);
            button.colors = colors;
            button.onClick.AddListener(onClick);

            Text text = Text("Label", gameObject.transform, label, 24, TextAnchor.MiddleCenter, Color.white);
            Rect(text.gameObject, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            return button;
        }
    }
}

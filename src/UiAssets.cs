using System;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace ModManager
{
    internal sealed class UiAssets
    {
        private readonly ManualLogSource _logger;

        internal UiAssets(ManualLogSource logger)
        {
            _logger = logger;
        }

        internal Sprite LoadEmbeddedSprite(string fileName, float borderPixels)
        {
            byte[] imageBytes = LoadEmbeddedBytes(fileName);
            if (imageBytes == null || imageBytes.Length == 0)
            {
                _logger.LogWarning(
                    "[ModManager] Embedded UI image was not found: " + fileName);
                return null;
            }

            try
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                texture.name = "ModManager_" + fileName;
                if (!TryLoadImage(texture, imageBytes))
                {
                    UnityEngine.Object.Destroy(texture);
                    _logger.LogWarning(
                        "[ModManager] Could not decode embedded UI image: " + fileName);
                    return null;
                }

                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                float border = Mathf.Max(
                    0f,
                    Mathf.Min(
                        borderPixels,
                        texture.width * 0.5f - 1f,
                        texture.height * 0.5f - 1f));
                Sprite sprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    100f,
                    0,
                    SpriteMeshType.FullRect,
                    new Vector4(border, border, border, border));
                sprite.name = "ModManager_" + Path.GetFileNameWithoutExtension(fileName);
                return sprite;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    "[ModManager] Failed to load UI image '" +
                    fileName + "': " + exception);
                return null;
            }
        }

        private static byte[] LoadEmbeddedBytes(string fileName)
        {
            Assembly assembly = typeof(UiAssets).Assembly;
            foreach (string resourceName in assembly.GetManifestResourceNames())
            {
                if (!resourceName.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(resourceName, fileName, StringComparison.OrdinalIgnoreCase))
                    continue;

                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null || stream.Length > int.MaxValue)
                        return null;

                    var bytes = new byte[(int)stream.Length];
                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        int read = stream.Read(bytes, offset, bytes.Length - offset);
                        if (read <= 0)
                            break;
                        offset += read;
                    }
                    return bytes;
                }
            }

            return null;
        }

        private static bool TryLoadImage(Texture2D texture, byte[] imageBytes)
        {
            Type conversionType = FindLoadedType("UnityEngine.ImageConversion");
            if (conversionType == null)
            {
                try
                {
                    Assembly assembly =
                        Assembly.Load("UnityEngine.ImageConversionModule");
                    conversionType = assembly?.GetType("UnityEngine.ImageConversion");
                }
                catch
                {
                }
            }

            if (conversionType == null)
                return false;

            MethodInfo method = conversionType.GetMethod(
                "LoadImage",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) },
                null);
            if (method != null)
                return (bool)method.Invoke(
                    null,
                    new object[] { texture, imageBytes, false });

            method = conversionType.GetMethod(
                "LoadImage",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(Texture2D), typeof(byte[]) },
                null);
            return method != null &&
                   (bool)method.Invoke(null, new object[] { texture, imageBytes });
        }

        private static Type FindLoadedType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName);
                if (type != null)
                    return type;
            }
            return null;
        }
    }
}

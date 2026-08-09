using UnityEngine;

/// <summary>
/// Provides a build-embedded Cyrillic font for runtime IMGUI.
/// </summary>
internal static class RuntimeGuiPresentation
{
    private const string FontResourcePath = "Fonts/RobotoMono-Regular";
    private static Font _font;
    private static bool _fontLoadAttempted;

    public static Font Font
    {
        get
        {
            if (_fontLoadAttempted == false)
            {
                _fontLoadAttempted = true;
                _font = Resources.Load<Font>(FontResourcePath);
                if (_font == null)
                {
                    Debug.LogError($"Runtime GUI font was not found at Resources/{FontResourcePath}.");
                }
            }

            return _font;
        }
    }

    public static void ApplyFontToCurrentSkin()
    {
        Font font = Font;
        if (font == null)
        {
            return;
        }

        GUI.skin.font = font;
        GUI.skin.label.font = font;
        GUI.skin.box.font = font;
        GUI.skin.button.font = font;
        GUI.skin.textField.font = font;
        GUI.skin.textArea.font = font;
        GUI.skin.toggle.font = font;
    }

    public static void ApplyFont(GUIStyle style)
    {
        if (style != null && Font != null)
        {
            style.font = Font;
        }
    }
}

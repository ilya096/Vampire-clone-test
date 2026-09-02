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

/// <summary>
/// Shared runtime presentation for state transitions and objective guidance.
/// It uses unscaled time so state changes remain readable without changing
/// gameplay time or dimming the scene.
/// </summary>
internal sealed class GameStateTransitionBanner : MonoBehaviour
{
    private const float TransitionSeconds = 1f;
    private static GameStateTransitionBanner _instance;

    private string _text = string.Empty;
    private float _startedAt = float.NegativeInfinity;
    private GUIStyle _style;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _instance = null;
    }

    public static void Show(string text)
    {
        if (Application.isPlaying == false || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (_instance == null)
        {
            GameObject root = new("GameStateTransitionBanner");
            _instance = root.AddComponent<GameStateTransitionBanner>();
        }

        _instance._text = text;
        _instance._startedAt = Time.unscaledTime;
    }

    private void OnGUI()
    {
        float elapsed = Time.unscaledTime - _startedAt;
        if (elapsed < 0f || elapsed >= TransitionSeconds || string.IsNullOrEmpty(_text))
        {
            return;
        }

        RuntimeGuiPresentation.ApplyFontToCurrentSkin();
        _style ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            wordWrap = true
        };
        RuntimeGuiPresentation.ApplyFont(_style);

        float collapse = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, elapsed / TransitionSeconds));
        _style.fontSize = Mathf.RoundToInt(Mathf.Lerp(42f, 15f, collapse));

        Rect center = new(Screen.width * 0.12f, Screen.height * 0.42f, Screen.width * 0.76f, 92f);
        Rect status = new(Screen.width * 0.5f - 180f, 14f, 360f, 32f);
        Rect rect = new(
            Mathf.Lerp(center.x, status.x, collapse),
            Mathf.Lerp(center.y, status.y, collapse),
            Mathf.Lerp(center.width, status.width, collapse),
            Mathf.Lerp(center.height, status.height, collapse));

        Color previousColor = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, Mathf.Lerp(0.78f, 0.38f, collapse));
        GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), _text, _style);
        GUI.color = Color.white;
        GUI.Label(rect, _text, _style);
        GUI.color = previousColor;
    }
}

internal static class ObjectiveGuidanceGui
{
    private const float EdgeMargin = 34f;
    private static GUIStyle _worldStyle;
    private static GUIStyle _indicatorStyle;
    private static GUIStyle _indicatorLabelStyle;

    public static bool DrawWorldProgress(Camera camera, Vector3 worldPosition, string text, Color color)
    {
        if (TryGetGuiPoint(camera, worldPosition, out Vector2 guiPoint, out bool onScreen) == false || onScreen == false)
        {
            return false;
        }

        EnsureStyles();
        float width = Mathf.Clamp(text.Length * 9f + 22f, 76f, 190f);
        Rect rect = new(guiPoint.x - width * 0.5f, guiPoint.y - 18f, width, 28f);
        Color previousColor = GUI.color;
        GUI.color = new Color(0.025f, 0.035f, 0.045f, 0.88f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = color;
        GUI.Label(rect, text, _worldStyle);
        GUI.color = previousColor;
        return true;
    }

    public static bool DrawOffscreenIndicator(
        Camera camera,
        Vector3 worldPosition,
        string label,
        Color color,
        float tangentOffset = 0f)
    {
        if (TryGetGuiPoint(camera, worldPosition, out Vector2 guiPoint, out bool onScreen) == false || onScreen)
        {
            return false;
        }

        EnsureStyles();
        Vector2 center = new(Screen.width * 0.5f, Screen.height * 0.5f);
        Vector2 direction = guiPoint - center;
        if (direction.sqrMagnitude < 0.001f)
        {
            direction = Vector2.up;
        }
        direction.Normalize();

        float horizontalScale = (Screen.width * 0.5f - EdgeMargin) / Mathf.Max(Mathf.Abs(direction.x), 0.0001f);
        float verticalScale = (Screen.height * 0.5f - EdgeMargin) / Mathf.Max(Mathf.Abs(direction.y), 0.0001f);
        Vector2 marker = center + direction * Mathf.Min(horizontalScale, verticalScale);
        marker += new Vector2(-direction.y, direction.x) * tangentOffset;
        marker.x = Mathf.Clamp(marker.x, EdgeMargin, Screen.width - EdgeMargin);
        marker.y = Mathf.Clamp(marker.y, EdgeMargin, Screen.height - EdgeMargin);

        Color previousColor = GUI.color;
        GUI.color = color;
        GUI.Label(new Rect(marker.x - 14f, marker.y - 14f, 28f, 28f), "●", _indicatorStyle);
        GUI.Label(new Rect(marker.x - 72f, marker.y + 13f, 144f, 20f), label, _indicatorLabelStyle);
        GUI.color = previousColor;
        return true;
    }

    private static bool TryGetGuiPoint(Camera camera, Vector3 worldPosition, out Vector2 guiPoint, out bool onScreen)
    {
        guiPoint = default;
        onScreen = false;
        if (camera == null)
        {
            return false;
        }

        Vector3 viewport = camera.WorldToViewportPoint(worldPosition);
        if (viewport.z < 0f)
        {
            viewport.x = 1f - viewport.x;
            viewport.y = 1f - viewport.y;
        }

        guiPoint = new Vector2(viewport.x * Screen.width, (1f - viewport.y) * Screen.height);
        onScreen = viewport.z > 0f
            && viewport.x >= 0.04f
            && viewport.x <= 0.96f
            && viewport.y >= 0.06f
            && viewport.y <= 0.94f;
        return true;
    }

    private static void EnsureStyles()
    {
        RuntimeGuiPresentation.ApplyFontToCurrentSkin();
        _worldStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 15,
            fontStyle = FontStyle.Bold
        };
        _indicatorStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 25,
            fontStyle = FontStyle.Bold
        };
        _indicatorLabelStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 12,
            fontStyle = FontStyle.Bold
        };
        RuntimeGuiPresentation.ApplyFont(_worldStyle);
        RuntimeGuiPresentation.ApplyFont(_indicatorStyle);
        RuntimeGuiPresentation.ApplyFont(_indicatorLabelStyle);
    }
}

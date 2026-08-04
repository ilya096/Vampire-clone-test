using UnityEngine;
using UnityEngine.UI;

public class CombatHudView : MonoBehaviour
{
    [SerializeField] private Text _healthText;
    [SerializeField] private Text _experienceText;
    [SerializeField] private Text[] _weaponSlots;
    [SerializeField] private RectTransform _aimReticle;
    [SerializeField] private GameObject _defeatPanel;
    [SerializeField] private Text _defeatText;

    private int _health;
    private int _maxHealth;
    private int _experience;
    private int _selectedWeapon;
    private int _pistolUpgradeCount;
    private int _machineGunUpgradeCount;
    private bool _hasRuntimeData;
    private GUIStyle _centerLabelStyle;

    private void Awake()
    {
        SetLegacyTextVisible(_healthText, false);
        SetLegacyTextVisible(_experienceText, false);
        foreach (Text weaponSlot in _weaponSlots)
        {
            SetLegacyTextVisible(weaponSlot, false);
        }
    }

    public void Refresh(int health, int maxHealth, int experience, int selectedWeapon, int pistolUpgradeCount, int machineGunUpgradeCount)
    {
        _health = health;
        _maxHealth = Mathf.Max(1, maxHealth);
        _experience = experience;
        _selectedWeapon = selectedWeapon;
        _pistolUpgradeCount = pistolUpgradeCount;
        _machineGunUpgradeCount = machineGunUpgradeCount;
        _hasRuntimeData = true;
    }

    public void SetAimReticle(Vector3 screenPosition)
    {
        if (_aimReticle != null)
        {
            _aimReticle.position = screenPosition;
        }
    }

    public void ShowDefeat(bool visible, string reason = null, int experience = 0)
    {
        if (_defeatPanel != null)
        {
            _defeatPanel.SetActive(visible);
        }

        if (visible && _defeatText == null && _defeatPanel != null)
        {
            _defeatText = _defeatPanel.GetComponentInChildren<Text>(true);
        }

        if (visible && _defeatText != null)
        {
            _defeatText.gameObject.SetActive(true);
            _defeatText.enabled = true;
            _defeatText.color = Color.white;
            _defeatText.fontSize = 36;
            _defeatText.verticalOverflow = VerticalWrapMode.Overflow;
            _defeatText.rectTransform.sizeDelta = new Vector2(620f, 180f);
            _defeatText.text = $"ПОРАЖЕНИЕ\nПричина: {reason}\nXP {experience}";
            _defeatText.SetAllDirty();
        }
    }

    private void OnGUI()
    {
        if (_hasRuntimeData == false)
        {
            return;
        }

        DrawBottomHud();
    }

    private void DrawBottomHud()
    {
        float healthFill = Mathf.Clamp01(_health / (float)_maxHealth);
        float hudWidth = Mathf.Min(760f, Screen.width - 20f);
        float healthWidth = Mathf.Min(420f, hudWidth);
        float healthHeight = 22f;
        float healthY = Screen.height - 148f;
        Rect healthFrame = new(Screen.width * 0.5f - healthWidth * 0.5f, healthY, healthWidth, healthHeight);
        DrawBar(healthFrame, healthFill, new Color(0.88f, 0.08f, 0.08f, 1f));
        DrawCenteredLabel(healthFrame, $"HP {_health} / {_maxHealth}", 13);

        const float weaponGap = 6f;
        const float weaponHeight = 48f;
        float weaponWidth = (hudWidth - weaponGap * 3f) / 4f;
        float weaponY = Screen.height - 119f;
        float weaponX = Screen.width * 0.5f - hudWidth * 0.5f;
        DrawWeaponCard(new Rect(weaponX, weaponY, weaponWidth, weaponHeight), 1, "ПИСТОЛЕТ", _pistolUpgradeCount, _selectedWeapon == 1, false);
        DrawWeaponCard(new Rect(weaponX + (weaponWidth + weaponGap), weaponY, weaponWidth, weaponHeight), 2, "ПУЛЕМЁТ", _machineGunUpgradeCount, _selectedWeapon == 2, false);
        DrawWeaponCard(new Rect(weaponX + (weaponWidth + weaponGap) * 2f, weaponY, weaponWidth, weaponHeight), 3, "LOCKED", 0, false, true);
        DrawWeaponCard(new Rect(weaponX + (weaponWidth + weaponGap) * 3f, weaponY, weaponWidth, weaponHeight), 4, "LOCKED", 0, false, true);
    }

    private void DrawWeaponCard(Rect rect, int slot, string weaponName, int upgradeCount, bool selected, bool locked)
    {
        Color previousColor = GUI.color;
        GUI.color = locked ? new Color(0.05f, 0.05f, 0.06f, 0.88f) : (selected ? new Color(0.32f, 0.22f, 0.03f, 0.96f) : new Color(0.05f, 0.08f, 0.12f, 0.94f));
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previousColor;

        DrawCenteredLabel(new Rect(rect.x, rect.y + 2f, rect.width, 18f), $"{slot}  {weaponName}", 11, locked ? Color.gray : (selected ? Color.yellow : Color.white));
        if (locked)
        {
            return;
        }

        Rect progressRect = new(rect.x + 6f, rect.yMax - 15f, rect.width - 12f, 8f);
        DrawSegmentedProgress(progressRect, upgradeCount, 9, selected ? new Color(1f, 0.73f, 0.12f) : new Color(0.05f, 0.76f, 0.92f));
        DrawCenteredLabel(new Rect(rect.x, rect.y + 18f, rect.width, 16f), $"{upgradeCount} / 9", 11);
    }

    private void DrawSegmentedProgress(Rect rect, int value, int maxValue, Color fillColor)
    {
        const float gap = 2f;
        float segmentWidth = (rect.width - gap * (maxValue - 1)) / maxValue;
        for (int index = 0; index < maxValue; index++)
        {
            Color previousColor = GUI.color;
            GUI.color = index < value ? fillColor : new Color(0.14f, 0.2f, 0.24f, 1f);
            GUI.DrawTexture(new Rect(rect.x + index * (segmentWidth + gap), rect.y, segmentWidth, rect.height), Texture2D.whiteTexture);
            GUI.color = previousColor;
        }
    }

    private void DrawBar(Rect frame, float fill, Color fillColor)
    {
        Color previousColor = GUI.color;
        GUI.color = new Color(0.08f, 0.01f, 0.01f, 0.95f);
        GUI.DrawTexture(frame, Texture2D.whiteTexture);
        Rect inner = new(frame.x + 3f, frame.y + 3f, frame.width - 6f, frame.height - 6f);
        GUI.color = fillColor;
        GUI.DrawTexture(new Rect(inner.x, inner.y, inner.width * fill, inner.height), Texture2D.whiteTexture);
        GUI.color = previousColor;
    }

    private void DrawCenteredLabel(Rect rect, string text, int fontSize, Color? color = null)
    {
        if (_centerLabelStyle == null)
        {
            _centerLabelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        }

        _centerLabelStyle.fontSize = fontSize;
        _centerLabelStyle.normal.textColor = color ?? Color.white;
        GUI.Label(rect, text, _centerLabelStyle);
    }

    private static void SetLegacyTextVisible(Text text, bool visible)
    {
        if (text != null)
        {
            text.enabled = visible;
        }
    }
}

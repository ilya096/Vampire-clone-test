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

    public void Refresh(int health, int maxHealth, int experience, int selectedWeapon)
    {
        _healthText.text = $"HP {health}/{maxHealth}";
        _experienceText.text = $"XP {experience}";

        for (int index = 0; index < _weaponSlots.Length; index++)
        {
            int slot = index + 1;
            bool active = slot == selectedWeapon;
            bool locked = slot > 2;
            _weaponSlots[index].color = locked ? Color.gray : (active ? Color.yellow : Color.white);
        }
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
}

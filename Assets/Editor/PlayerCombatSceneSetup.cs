#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class PlayerCombatSceneSetup
{
    private const string GameScenePath = "Assets/Scenes/Game.unity";
    private const string PresentationRootName = "LogoSurvivorCombatPresentation";

    [MenuItem("Tools/Logo Survivor/Setup Player Combat In Game")]
    public static void SetupPlayerCombatInGame()
    {
        Scene scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
        GameInstaller installer = Object.FindAnyObjectByType<GameInstaller>();
        if (installer == null)
        {
            Debug.LogError("GameInstaller was not found in the Game scene.");
            return;
        }

        if (installer.GetComponent<CombatRuntimeController>() == null)
        {
            Undo.AddComponent<CombatRuntimeController>(installer.gameObject);
        }

        GameObject existingRoot = scene.GetRootGameObjects().FirstOrDefault(gameObject => gameObject.name == PresentationRootName);
        if (existingRoot != null)
        {
            RefreshExistingPresentation(scene, existingRoot);
            return;
        }

        GameObject root = new(PresentationRootName);
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        root.AddComponent<CanvasScaler>();
        root.AddComponent<GraphicRaycaster>();
        CombatHudView hud = root.AddComponent<CombatHudView>();

        Text health = CreateText("Health", root.transform, "HP 100/100", new Vector2(16f, -16f), TextAnchor.UpperLeft, 24);
        Text experience = CreateText("Experience", root.transform, "XP 0", new Vector2(16f, -50f), TextAnchor.UpperLeft, 24);
        Text slot1 = CreateText("WeaponSlot1", root.transform, "1  PISTOL", new Vector2(-16f, -16f), TextAnchor.UpperRight, 20);
        Text slot2 = CreateText("WeaponSlot2", root.transform, "2  MACHINE GUN", new Vector2(-16f, -44f), TextAnchor.UpperRight, 20);
        Text slot3 = CreateText("WeaponSlot3", root.transform, "3  LOCKED", new Vector2(-16f, -72f), TextAnchor.UpperRight, 20);
        Text slot4 = CreateText("WeaponSlot4", root.transform, "4  LOCKED", new Vector2(-16f, -100f), TextAnchor.UpperRight, 20);
        Text reticle = CreateText("AimReticle", root.transform, "+", Vector2.zero, TextAnchor.MiddleCenter, 30);
        reticle.rectTransform.sizeDelta = new Vector2(30f, 30f);

        GameObject defeatPanel = CreateDefeatPanel(root.transform);
        BindHud(hud, health, experience, new[] { slot1, slot2, slot3, slot4 }, reticle.rectTransform, defeatPanel, defeatPanel.GetComponentInChildren<Text>());
        defeatPanel.SetActive(false);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Saved player combat HUD and runtime controller in Assets/Scenes/Game.unity.");
    }

    private static Text CreateText(string name, Transform parent, string value, Vector2 offset, TextAnchor alignment, int fontSize)
    {
        GameObject gameObject = new(name, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        Text text = gameObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value;
        text.alignment = alignment;
        text.fontSize = fontSize;
        text.color = Color.white;

        RectTransform transform = text.rectTransform;
        bool right = alignment == TextAnchor.UpperRight;
        transform.anchorMin = right ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
        transform.anchorMax = transform.anchorMin;
        transform.pivot = right ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
        transform.anchoredPosition = offset;
        transform.sizeDelta = new Vector2(300f, 30f);
        return text;
    }

    private static GameObject CreateDefeatPanel(Transform parent)
    {
        GameObject panel = new("DefeatPanel", typeof(RectTransform));
        panel.transform.SetParent(parent, false);
        Image image = panel.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.7f);
        RectTransform panelTransform = image.rectTransform;
        panelTransform.anchorMin = Vector2.zero;
        panelTransform.anchorMax = Vector2.one;
        panelTransform.offsetMin = Vector2.zero;
        panelTransform.offsetMax = Vector2.zero;

        Text text = CreateText("DefeatText", panel.transform, "ПОРАЖЕНИЕ", Vector2.zero, TextAnchor.MiddleCenter, 36);
        text.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        text.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        text.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        text.rectTransform.anchoredPosition = Vector2.zero;
        ConfigureDefeatText(text);
        return panel;
    }

    private static void RefreshExistingPresentation(Scene scene, GameObject root)
    {
        CombatHudView hud = root.GetComponent<CombatHudView>();
        Text health = root.transform.Find("Health").GetComponent<Text>();
        Text experience = root.transform.Find("Experience").GetComponent<Text>();
        Text[] slots =
        {
            root.transform.Find("WeaponSlot1").GetComponent<Text>(),
            root.transform.Find("WeaponSlot2").GetComponent<Text>(),
            root.transform.Find("WeaponSlot3").GetComponent<Text>(),
            root.transform.Find("WeaponSlot4").GetComponent<Text>()
        };
        Text reticle = root.transform.Find("AimReticle").GetComponent<Text>();
        GameObject defeatPanel = root.transform.Find("DefeatPanel").gameObject;
        Text defeatText = defeatPanel.transform.Find("DefeatText").GetComponent<Text>();
        ConfigureDefeatText(defeatText);
        BindHud(hud, health, experience, slots, reticle.rectTransform, defeatPanel, defeatText);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Refreshed player combat presentation in Assets/Scenes/Game.unity.");
    }

    private static void BindHud(CombatHudView hud, Text health, Text experience, Text[] slots, RectTransform reticle, GameObject defeatPanel, Text defeatText)
    {
        SerializedObject serializedHud = new(hud);
        serializedHud.FindProperty("_healthText").objectReferenceValue = health;
        serializedHud.FindProperty("_experienceText").objectReferenceValue = experience;
        SerializedProperty slotProperty = serializedHud.FindProperty("_weaponSlots");
        slotProperty.arraySize = slots.Length;
        for (int index = 0; index < slots.Length; index++)
        {
            slotProperty.GetArrayElementAtIndex(index).objectReferenceValue = slots[index];
        }

        serializedHud.FindProperty("_aimReticle").objectReferenceValue = reticle;
        serializedHud.FindProperty("_defeatPanel").objectReferenceValue = defeatPanel;
        serializedHud.FindProperty("_defeatText").objectReferenceValue = defeatText;
        serializedHud.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ConfigureDefeatText(Text text)
    {
        text.fontSize = 36;
        text.color = Color.white;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.rectTransform.sizeDelta = new Vector2(620f, 180f);
    }
}
#endif

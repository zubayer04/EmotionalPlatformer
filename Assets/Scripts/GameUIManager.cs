using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class GameUIManager : MonoBehaviour
{
    private const float EasyDifficulty = 3.5f;
    private const float MediumDifficulty = 5f;
    private const float HardDifficulty = 7f;

    private LevelManager levelManager;
    private Canvas canvas;
    private Font uiFont;
    private GameObject mainMenuPanel;
    private GameObject howToPlayPanel;
    private GameObject optionsPanel;
    private Toggle advancedToggle;
    private Text difficultyValueText;

    private bool advancedMode = false;
    private float selectedDifficulty = MediumDifficulty;

    public void Initialize(LevelManager manager)
    {
        levelManager = manager;
        uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (uiFont == null)
            uiFont = Resources.GetBuiltinResource<Font>("Arial.ttf");

        EnsureEventSystem();
        BuildCanvas();
        BuildMainMenu();
        BuildHowToPlay();
        BuildOptions();
        ShowMainMenu();
    }

    private void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null)
            return;

        GameObject eventSystem = new GameObject("EventSystem");
        eventSystem.AddComponent<EventSystem>();
        eventSystem.AddComponent<StandaloneInputModule>();
    }

    private void BuildCanvas()
    {
        GameObject canvasObject = new GameObject("Game Menu Canvas");
        canvasObject.transform.SetParent(transform, false);

        canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObject.AddComponent<GraphicRaycaster>();
    }

    private void BuildMainMenu()
    {
        mainMenuPanel = CreatePanel("Main Menu");
        RectTransform box = CreateMenuBox(mainMenuPanel.transform, 460f, 390f);

        CreateText(box, "Adaptive Platformer", 34, FontStyle.Bold, new Vector2(0f, 130f), new Vector2(400f, 50f));
        CreateButton(box, "Start", new Vector2(0f, 45f), () =>
        {
            HideAllPanels();
            levelManager.StartGameFromMenu(selectedDifficulty, advancedMode);
        });
        CreateButton(box, "How To Play", new Vector2(0f, -25f), ShowHowToPlay);
        CreateButton(box, "Options", new Vector2(0f, -95f), ShowOptions);
    }

    private void BuildHowToPlay()
    {
        howToPlayPanel = CreatePanel("How To Play");
        RectTransform box = CreateMenuBox(howToPlayPanel.transform, 640f, 430f);

        CreateText(box, "How To Play", 30, FontStyle.Bold, new Vector2(0f, 160f), new Vector2(560f, 45f));
        CreateText(
            box,
            "Move: A / D or Arrow Keys\n" +
            "Jump: Space\n" +
            "Dash: Left Shift\n\n" +
            "Reach the endpoint to complete each level.\n" +
            "Between levels, the system adapts the target difficulty using performance and behaviour signals.",
            21,
            FontStyle.Normal,
            new Vector2(0f, 25f),
            new Vector2(540f, 220f),
            TextAnchor.MiddleLeft);
        CreateButton(box, "Back", new Vector2(0f, -155f), ShowMainMenu);
    }

    private void BuildOptions()
    {
        optionsPanel = CreatePanel("Options");
        RectTransform box = CreateMenuBox(optionsPanel.transform, 580f, 430f);

        CreateText(box, "Options", 30, FontStyle.Bold, new Vector2(0f, 160f), new Vector2(500f, 45f));

        advancedToggle = CreateToggle(box, "Advanced Stats", new Vector2(0f, 85f));
        advancedToggle.isOn = advancedMode;
        advancedToggle.onValueChanged.AddListener(value => advancedMode = value);

        CreateText(box, "Starting Difficulty", 22, FontStyle.Bold, new Vector2(0f, 20f), new Vector2(460f, 35f));
        difficultyValueText = CreateText(box, DifficultyLabel(), 22, FontStyle.Normal, new Vector2(0f, -22f), new Vector2(460f, 35f));

        CreateButton(box, "Easy", new Vector2(-150f, -85f), () => SetDifficulty(EasyDifficulty), new Vector2(120f, 48f));
        CreateButton(box, "Medium", new Vector2(0f, -85f), () => SetDifficulty(MediumDifficulty), new Vector2(120f, 48f));
        CreateButton(box, "Hard", new Vector2(150f, -85f), () => SetDifficulty(HardDifficulty), new Vector2(120f, 48f));

        CreateButton(box, "Back", new Vector2(0f, -160f), ShowMainMenu);
    }

    private GameObject CreatePanel(string panelName)
    {
        GameObject panel = new GameObject(panelName);
        panel.transform.SetParent(canvas.transform, false);

        RectTransform rect = panel.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image background = panel.AddComponent<Image>();
        background.color = new Color(0.07f, 0.12f, 0.17f, 0.92f);

        return panel;
    }

    private RectTransform CreateMenuBox(Transform parent, float width, float height)
    {
        GameObject boxObject = new GameObject("Menu Box");
        boxObject.transform.SetParent(parent, false);

        RectTransform rect = boxObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(width, height);

        Image image = boxObject.AddComponent<Image>();
        image.color = new Color(0.11f, 0.18f, 0.25f, 0.96f);

        return rect;
    }

    private Text CreateText(
        Transform parent,
        string text,
        int size,
        FontStyle style,
        Vector2 position,
        Vector2 dimensions,
        TextAnchor alignment = TextAnchor.MiddleCenter)
    {
        GameObject textObject = new GameObject("Text");
        textObject.transform.SetParent(parent, false);

        RectTransform rect = textObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;

        Text label = textObject.AddComponent<Text>();
        label.text = text;
        label.font = uiFont;
        label.fontSize = size;
        label.fontStyle = style;
        label.alignment = alignment;
        label.color = Color.white;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Overflow;

        return label;
    }

    private Button CreateButton(
        Transform parent,
        string label,
        Vector2 position,
        UnityEngine.Events.UnityAction onClick,
        Vector2? size = null)
    {
        Vector2 buttonSize = size ?? new Vector2(240f, 48f);
        GameObject buttonObject = new GameObject(label + " Button");
        buttonObject.transform.SetParent(parent, false);

        RectTransform rect = buttonObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = buttonSize;

        Image image = buttonObject.AddComponent<Image>();
        image.color = new Color(0.22f, 0.42f, 0.58f, 1f);

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);

        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(0.28f, 0.52f, 0.70f, 1f);
        colors.pressedColor = new Color(0.16f, 0.32f, 0.46f, 1f);
        button.colors = colors;

        CreateText(buttonObject.transform, label, 22, FontStyle.Bold, Vector2.zero, buttonSize);
        return button;
    }

    private Toggle CreateToggle(Transform parent, string label, Vector2 position)
    {
        GameObject toggleObject = new GameObject(label + " Toggle");
        toggleObject.transform.SetParent(parent, false);

        RectTransform rect = toggleObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(340f, 42f);

        Toggle toggle = toggleObject.AddComponent<Toggle>();

        GameObject backgroundObject = new GameObject("Background");
        backgroundObject.transform.SetParent(toggleObject.transform, false);
        RectTransform bgRect = backgroundObject.AddComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0f, 0.5f);
        bgRect.anchorMax = new Vector2(0f, 0.5f);
        bgRect.pivot = new Vector2(0f, 0.5f);
        bgRect.anchoredPosition = new Vector2(0f, 0f);
        bgRect.sizeDelta = new Vector2(42f, 42f);
        Image bgImage = backgroundObject.AddComponent<Image>();
        bgImage.color = new Color(0.18f, 0.28f, 0.36f, 1f);

        GameObject checkObject = new GameObject("Checkmark");
        checkObject.transform.SetParent(backgroundObject.transform, false);
        RectTransform checkRect = checkObject.AddComponent<RectTransform>();
        checkRect.anchorMin = new Vector2(0.5f, 0.5f);
        checkRect.anchorMax = new Vector2(0.5f, 0.5f);
        checkRect.pivot = new Vector2(0.5f, 0.5f);
        checkRect.anchoredPosition = Vector2.zero;
        checkRect.sizeDelta = new Vector2(24f, 24f);
        Image checkImage = checkObject.AddComponent<Image>();
        checkImage.color = new Color(0.36f, 0.78f, 0.52f, 1f);

        toggle.targetGraphic = bgImage;
        toggle.graphic = checkImage;

        CreateText(toggleObject.transform, label, 22, FontStyle.Bold, new Vector2(92f, 0f), new Vector2(230f, 42f), TextAnchor.MiddleLeft);
        return toggle;
    }

    private void SetDifficulty(float difficulty)
    {
        selectedDifficulty = difficulty;
        if (difficultyValueText != null)
            difficultyValueText.text = DifficultyLabel();
    }

    private string DifficultyLabel()
    {
        if (Mathf.Approximately(selectedDifficulty, EasyDifficulty))
            return "Easy (3.5)";

        if (Mathf.Approximately(selectedDifficulty, HardDifficulty))
            return "Hard (7.0)";

        return "Medium (5.0)";
    }

    private void ShowMainMenu()
    {
        Time.timeScale = 0f;
        HideAllPanels();
        mainMenuPanel.SetActive(true);
    }

    private void ShowHowToPlay()
    {
        HideAllPanels();
        howToPlayPanel.SetActive(true);
    }

    private void ShowOptions()
    {
        HideAllPanels();
        optionsPanel.SetActive(true);
    }

    private void HideAllPanels()
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (howToPlayPanel != null) howToPlayPanel.SetActive(false);
        if (optionsPanel != null) optionsPanel.SetActive(false);
    }
}

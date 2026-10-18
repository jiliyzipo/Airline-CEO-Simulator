using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>右下角悬浮 AI 对话气泡外观。</summary>
    public class ChatBubble : MonoBehaviour
    {
        [SerializeField] private Image background;
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI bodyText;
        [SerializeField] private TMP_InputField inputField;

        public TextMeshProUGUI BodyText => bodyText;
        public TMP_InputField InputField => inputField;
        public TextMeshProUGUI TitleText => titleText;

        public static ChatBubble BuildRuntime(Transform parent, UIFactory.Anchor anchor)
        {
            var root = UIFactory.Panel(parent, "ChatBubble", anchor, UITheme.Glass);
            UIFactory.AddBorder(root, UITheme.Accent, new Vector2(1.5f, -1.5f));

            UIFactory.Panel(root.transform, "HeaderBar",
                new UIFactory.Anchor(new Vector2(0, 0.88f), new Vector2(1, 1), Vector2.zero, Vector2.zero),
                UITheme.Accent, rounded: false);

            var bubble = root.AddComponent<ChatBubble>();
            bubble.background = root.GetComponent<Image>();
            bubble.titleText = UIFactory.Label(root.transform, "Title", "Neural Command · 战略顾问", 15,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.88f), new Vector2(0.72f, 1f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            bubble.bodyText = UIFactory.Label(root.transform, "Body",
                "战略顾问：输入「分析航线」后，可点「执行建议」真正开通。", 14,
                TextAlignmentOptions.TopLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.36f), new Vector2(0.96f, 0.86f), Vector2.zero, Vector2.zero),
                new Color(0.82f, 0.9f, 0.86f));

            return bubble;
        }
    }
}

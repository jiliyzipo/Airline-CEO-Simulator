using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI.Setup
{
    /// <summary>主菜单纵向选项（Football Manager 式：整行按钮，一条一件事）。</summary>
    public class MenuEntry
    {
        /// <summary>节点名（测试与排查按 <c>Entry_ + Name</c> 定位，例：Entry_New）。</summary>
        public string Name;
        /// <summary>按钮主文案。</summary>
        public string Label;
        /// <summary>按钮底色。</summary>
        public Color Color = UITheme.PanelDeep;
        /// <summary>false = 置灰不可点（例：无存档时的「继续 / 读档」）。</summary>
        public bool Interactable = true;
        /// <summary>点击回调。</summary>
        public Action OnClick;
    }

    /// <summary>「最近保存」卡片内容。本类**不读存档**，全部由调用方给出（便于测试与解耦）。</summary>
    public struct SaveCardInfo
    {
        /// <summary>是否存在存档：决定卡片配色与图标字。</summary>
        public bool HasSave;
        /// <summary>图标方块里的字（存 / 新）。</summary>
        public string Icon;
        /// <summary>卡片主行（例：北辰快线）。</summary>
        public string Title;
        /// <summary>卡片次行（例：财年 2 · Q3 · 现金 1,204 万 · 董事会 62）。</summary>
        public string Sub;
    }

    /// <summary>主菜单建好后的句柄。提示行要能被后续点击回写（例：读档组件未就绪），故不是纯 Build。</summary>
    /// <remarks>
    /// 提示行**故意只暴露字符串**（<see cref="HintText"/> / <see cref="SetHint"/>），不外泄 <c>TextMeshProUGUI</c>：
    /// 测试程序集（<c>AirlineCEO.PlayModeTests</c>）不引用 Unity.TextMeshPro，一旦从外部碰到 TMPro 类型
    /// 就会 CS0012（编译期报「必须添加对程序集 Unity.TextMeshPro 的引用」）。版面句柄没必要把内部控件类型
    /// 交出去 —— 调用方要读要写都只需那段文字。
    /// </remarks>
    public class MainMenuView
    {
        public GameObject Root;

        TextMeshProUGUI _hint;

        /// <summary>当前提示文字（无提示行时为空串，不会返回 null）。</summary>
        public string HintText => _hint != null ? _hint.text : "";

        public void SetHint(string text)
        {
            if (_hint != null) _hint.text = ChineseFontProvider.Sanitize(text ?? "");
        }

        internal void BindHint(TextMeshProUGUI label) => _hint = label;
    }

    /// <summary>
    /// 主菜单版面（Football Manager 式两栏）：左侧**主视觉槽**（留白，后期图像化）+ 右侧**纵向菜单列**。
    ///
    /// 为什么换掉旧的「标题 + 说明 + 一排横按钮」：横排按钮把 6 件事挤在一行、每格宽度只有屏宽的
    /// 15%，文案稍长就被截断；而入口这件事本身有主次之分（开局 / 读档 / 回放 / 设置 / 退出），
    /// FM 的解法是**纵向整行**并按主次分层：
    /// <list type="number">
    /// <item>品牌块（游戏名 + 强调横条 + 一句说明）；</item>
    /// <item>「最近保存」卡片（有档时给出公司名 / 财年 / 现金 / 董事会满意度）；</item>
    /// <item>4 条主按钮（新游戏·接管 / 从零创建 / 继续·读档 / 历史回放）——真正的玩法入口；</item>
    /// <item>次级一行（设置 / 存档 + 快捷键提示）——utility 不配跟主入口抢同一层视觉；</item>
    /// <item>页脚（版本号 + 小号「退出」），钉在列的底部，不随上方内容增删而漂移。</item>
    /// </list>
    ///
    /// 简易版约定：左槽只放一道淡水印 + 一行占位说明，**没有任何交互**；
    /// 后期把 <see cref="HeroName"/> 里的两个 Label 换掉、挂上航司涂装 / 驾驶舱插画即可，
    /// 不必动右侧菜单列的任何几何。
    /// </summary>
    public static class MainMenuUI
    {
        public const string RootName = "MainMenuLayout";
        /// <summary>左侧主视觉槽（后期图像化的替换点）。</summary>
        public const string HeroName = "HeroSlot";
        public const string BrandName = "Brand";
        public const string BrandRuleName = "BrandRule";
        public const string BrandTagName = "BrandTag";
        public const string SaveCardName = "SaveCard";
        public const string SaveTitleName = "SaveTitle";
        public const string SaveSubName = "SaveSub";
        public const string LinkBarName = "LinkBar";
        public const string SettingsName = "Settings";
        public const string KeyHintName = "KeyHint";
        public const string HintName = "Hint";
        public const string QuitName = "Quit";
        public const string VersionName = "VersionTag";
        /// <summary>主按钮节点名前缀：<c>Entry_</c> + <see cref="MenuEntry.Name"/>。</summary>
        public const string EntryPrefix = "Entry_";

        // 版面几何：左槽 3%~55%，菜单列 58%~97%（留出右侧安全边）。
        const float HeroX0 = 0.03f, HeroX1 = 0.55f, HeroY0 = 0.10f, HeroY1 = 0.90f;
        const float ColX0 = 0.58f, ColX1 = 0.97f, ColY0 = 0.08f, ColY1 = 0.92f;
        const float RowH = 44f;          // 主按钮高
        const float RowGap = 7f;         // 主按钮间距
        const float SaveCardH = 62f;     // 「最近保存」卡片高

        /// <summary>
        /// 建版面。返回值里的 <see cref="MainMenuView.SetHint(string)"/> / <see cref="MainMenuView.HintText"/>
        /// 供调用方读写状态提示（只走字符串，不外泄 TMPro 类型，见 <see cref="MainMenuView"/> 注释）。
        /// 调用方负责在切页时销毁 <see cref="MainMenuView.Root"/>。
        /// </summary>
        public static MainMenuView Build(Transform overlay, SaveCardInfo save,
            IList<MenuEntry> primary, Action onSettings, Action onQuit, string hint)
        {
            var root = UIFactory.Panel(overlay, RootName, UIFactory.Fill, new Color(0f, 0f, 0f, 0f), rounded: false);
            // 盖在通用表单卡之上（主菜单不用那张卡，见 SetupFlowController.ShowMainMenu）
            root.transform.SetAsLastSibling();
            Decorative(root);

            BuildHeroSlot(root.transform);

            var column = UIFactory.Panel(root.transform, "Column",
                new UIFactory.Anchor(new Vector2(ColX0, ColY0), new Vector2(ColX1, ColY1), Vector2.zero, Vector2.zero),
                new Color(0f, 0f, 0f, 0f), rounded: false);
            Decorative(column);

            float y = 0f;
            y = BuildBrand(column.transform, y);
            y = BuildSaveCard(column.transform, save, y);
            y = BuildPrimaryEntries(column.transform, primary, y);
            y = BuildLinkBar(column.transform, onSettings, y);

            // 主按钮栈的可用高度是按「菜单列 0.84 屏高」估的：1080p 富余很大，
            // 1024×600 的编辑器预览下约 504px，当前栈 ≈ 438px + 页脚 32px，仍留有余量。
            var hintLabel = UIFactory.Label(column.transform, HintName, hint ?? "", 13,
                TextAlignmentOptions.TopLeft, UIFactory.Fill, UITheme.TextMuted);
            UIFactory.PinFromTop(hintLabel.gameObject, y, 46f);

            BuildFooter(column.transform, onQuit);

            var view = new MainMenuView { Root = root };
            view.BindHint(hintLabel);
            return view;
        }

        /// <summary>左侧主视觉槽：本次只放淡水印 + 占位说明，后期换成插画（不挂任何交互）。</summary>
        static void BuildHeroSlot(Transform parent)
        {
            var hero = UIFactory.Panel(parent, HeroName,
                new UIFactory.Anchor(new Vector2(HeroX0, HeroY0), new Vector2(HeroX1, HeroY1), Vector2.zero, Vector2.zero),
                new Color(0.04f, 0.09f, 0.17f, 0.55f));
            UIFactory.AddBorder(hero, UITheme.Border, new Vector2(1f, -1f));
            Decorative(hero);

            // 极淡水印：让空槽看起来是「刻意留白」，而不是「渲染坏了」
            UIFactory.Label(hero.transform, "Watermark", "AIRLINE\nCEO SIMULATOR", 44,
                TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.06f, 0.34f), new Vector2(0.94f, 0.88f), Vector2.zero, Vector2.zero),
                new Color(1f, 1f, 1f, 0.07f), FontStyles.Bold);

            UIFactory.Label(hero.transform, "SlotNote",
                "主视觉占位 · 后期图像化\n此处将替换为航司涂装 / 驾驶舱插画（简易版留白，不影响交互）",
                13, TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.06f, 0.06f), new Vector2(0.94f, 0.24f), Vector2.zero, Vector2.zero),
                new Color(UITheme.TextMuted.r, UITheme.TextMuted.g, UITheme.TextMuted.b, 0.85f));
        }

        /// <summary>品牌块：游戏名 + 强调横条 + 一句说明（对齐 FM 面板顶部的 logo 处理）。</summary>
        static float BuildBrand(Transform col, float y)
        {
            var brand = UIFactory.Label(col, BrandName, "Airline CEO Simulator", 22,
                TextAlignmentOptions.MidlineLeft, UIFactory.Fill, UITheme.TextPrimary, FontStyles.Bold);
            UIFactory.PinFromTop(brand.gameObject, y, 34f);

            var rule = UIFactory.Panel(col, BrandRuleName, UIFactory.Fill, UITheme.Accent);
            UIFactory.PinFromTop(rule, y + 36f, 3f, 0f, 0.30f);
            Decorative(rule);

            var tag = UIFactory.Label(col, BrandTagName, "接管一家航司 · 当十年 CEO", 13,
                TextAlignmentOptions.MidlineLeft, UIFactory.Fill, UITheme.TextMuted);
            UIFactory.PinFromTop(tag.gameObject, y + 42f, 20f);

            return y + 64f;
        }

        /// <summary>「最近保存」卡片：有档给公司 / 财年 / 现金 / 董事会；无档说明去哪儿开一局。</summary>
        static float BuildSaveCard(Transform col, SaveCardInfo save, float y)
        {
            Color cardColor = save.HasSave ? UITheme.PanelDeep : new Color(0.10f, 0.16f, 0.26f, 0.55f);
            var card = UIFactory.Panel(col, SaveCardName, UIFactory.Fill, cardColor);
            UIFactory.AddBorder(card, save.HasSave ? UITheme.Border : new Color(0.16f, 0.24f, 0.36f, 1f),
                new Vector2(1f, -1f));
            UIFactory.PinFromTop(card, y, SaveCardH, 0f, 1f);
            Decorative(card);

            var icon = UIFactory.Panel(card.transform, "Icon", UIFactory.Fill,
                save.HasSave ? UITheme.Accent : UITheme.NavIdle);
            UIFactory.PinFromTop(icon, 13f, 40f, 0.028f, 0.136f);
            Decorative(icon);
            UIFactory.Label(icon.transform, "Glyph", save.Icon ?? "", 16,
                TextAlignmentOptions.Center, UIFactory.Fill, UITheme.TextPrimary, FontStyles.Bold);

            var title = UIFactory.Label(card.transform, SaveTitleName, save.Title ?? "", 15,
                TextAlignmentOptions.MidlineLeft, UIFactory.Fill, UITheme.TextPrimary, FontStyles.Bold);
            UIFactory.PinFromTop(title.gameObject, 11f, 24f, 0.17f, 0.97f);

            var sub = UIFactory.Label(card.transform, SaveSubName, save.Sub ?? "", 12,
                TextAlignmentOptions.MidlineLeft, UIFactory.Fill, UITheme.TextMuted);
            UIFactory.PinFromTop(sub.gameObject, 34f, 22f, 0.17f, 0.97f);

            return y + SaveCardH + 16f;
        }

        /// <summary>玩法入口：纵向整行按钮。<c>Interactable = false</c> 的条目置灰（Unity 的 disabledColor 生效）。</summary>
        static float BuildPrimaryEntries(Transform col, IList<MenuEntry> entries, float y)
        {
            if (entries == null) return y;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e == null) continue;
                var btn = UIFactory.Button(col, EntryPrefix + e.Name, e.Label ?? "", UIFactory.Fill,
                    e.Color, () => e.OnClick?.Invoke(), 17);
                UIFactory.PinFromTop(btn.gameObject, y, RowH);
                if (!e.Interactable) btn.interactable = false;
                y += RowH + RowGap;
            }
            return y + 6f;
        }

        /// <summary>次级一行：设置入口 + 快捷键提示。utility 不跟主入口抢同一层视觉。</summary>
        static float BuildLinkBar(Transform col, Action onSettings, float y)
        {
            var bar = UIFactory.Panel(col, LinkBarName, UIFactory.Fill, new Color(0f, 0f, 0f, 0f), rounded: false);
            UIFactory.PinFromTop(bar, y, 30f, 0f, 1f);
            Decorative(bar);

            var setBtn = UIFactory.Button(bar.transform, SettingsName, "设置 / 存档", UIFactory.Fill,
                UITheme.NavIdle, () => onSettings?.Invoke(), 13);
            UIFactory.PinFromTop(setBtn.gameObject, 0f, 30f, 0f, 0.46f);

            var keys = UIFactory.Label(bar.transform, KeyHintName, "F5 存档 · F9 读档", 12,
                TextAlignmentOptions.MidlineRight, UIFactory.Fill, UITheme.TextMuted);
            UIFactory.PinFromTop(keys.gameObject, 0f, 30f, 0.5f, 1f);

            return y + 40f;
        }

        /// <summary>页脚：版本号（左）+ 小号「退出」（右），钉在列的底部。</summary>
        static void BuildFooter(Transform col, Action onQuit)
        {
            var version = UIFactory.Label(col, VersionName, "MVP v" + Application.version, 12,
                TextAlignmentOptions.MidlineLeft, UIFactory.Fill, UITheme.TextMuted);
            PinToBottom(version.gameObject, 28f, 2f, 0f, 0.5f);

            var quit = UIFactory.Button(col, QuitName, "退出", UIFactory.Fill, UITheme.Danger,
                () => onQuit?.Invoke(), 13);
            PinToBottom(quit.gameObject, 32f, 0f, 0.62f, 1f);
        }

        /// <summary>钉在父级底部（页脚用；与 <see cref="UIFactory.PinFromTop"/> 对称）。</summary>
        static void PinToBottom(GameObject go, float height, float bottomPad, float xMin, float xMax)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(xMin, 0f);
            rt.anchorMax = new Vector2(xMax, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(0f, height);
            rt.anchoredPosition = new Vector2(0f, bottomPad);
        }

        /// <summary>纯装饰节点不吃射线：点空白处不该被这些底板拦下（子节点按钮不受影响）。</summary>
        static void Decorative(GameObject go)
        {
            var img = go != null ? go.GetComponent<Image>() : null;
            if (img != null) img.raycastTarget = false;
        }
    }
}

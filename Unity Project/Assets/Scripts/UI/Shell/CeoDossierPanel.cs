using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI.Shell
{
    /// <summary>主页子页：CEO 资料 / 合同（历史、成就来自局内标志）。</summary>
    public class CeoDossierPanel : BasePanel
    {
        public const string PageProfile = "n110_CEO资料";
        public const string PageContract = "n111_CEO合同";

        bool _contract;

        public static CeoDossierPanel BuildProfile() => Build(false);
        public static CeoDossierPanel BuildContract() => Build(true);

        static CeoDossierPanel Build(bool contract)
        {
            string id = contract ? PageContract : PageProfile;
            string title = contract ? "合同" : "个人资料";
            var root = UIFactory.Panel(null, id, UIFactory.Fill, UITheme.Panel);
            var cg = root.AddComponent<CanvasGroup>();
            var titleLabel = UIFactory.Label(root.transform, "Title", title, 22,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.04f, 0.90f), new Vector2(0.7f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);
            var close = UIFactory.Button(root.transform, "Close", "×",
                new UIFactory.Anchor(new Vector2(0.90f, 0.90f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 22);
            UIFactory.Button(root.transform, "Back", "← 返回",
                new UIFactory.Anchor(new Vector2(0.04f, 0.02f), new Vector2(0.20f, 0.10f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, () => { }, 14);

            var body = UIFactory.Panel(root.transform, "Body",
                new UIFactory.Anchor(new Vector2(0.04f, 0.14f), new Vector2(0.96f, 0.88f), Vector2.zero, Vector2.zero),
                UITheme.PanelDeep);
            UIFactory.AddBorder(body, UITheme.Border, new Vector2(1f, -1f));

            var panel = root.AddComponent<CeoDossierPanel>();
            panel._contract = contract;
            panel.AssignRefs(root.GetComponent<RectTransform>(), titleLabel, close, cg);
            panel.BindPage(id, title);
            Fill(body.transform, contract);
            return panel;
        }

        public static void Fill(Transform host, bool contract)
        {
            for (int i = host.childCount - 1; i >= 0; i--)
            {
                var c = host.GetChild(i);
                if (c.name == "Border") continue;
                Object.Destroy(c.gameObject);
            }

            var gs = GameState.Instance;
            gs?.EnsureCeoContractDefaults();

            void Row(string name, string label, string value, float y)
            {
                UIFactory.Label(host, name + "K", label, 14, TextAlignmentOptions.MidlineLeft,
                    new UIFactory.Anchor(new Vector2(0.04f, y), new Vector2(0.36f, y + 0.08f), Vector2.zero, Vector2.zero),
                    UITheme.TextMuted);
                UIFactory.Label(host, name + "V", string.IsNullOrEmpty(value) ? "—" : value, 16,
                    TextAlignmentOptions.MidlineLeft,
                    new UIFactory.Anchor(new Vector2(0.38f, y), new Vector2(0.96f, y + 0.08f), Vector2.zero, Vector2.zero),
                    UITheme.TextPrimary, FontStyles.Bold);
            }

            if (gs == null)
            {
                Row("E", "状态", "游戏未就绪", 0.84f);
                return;
            }

            if (!contract)
            {
                Row("Name", "姓名", gs.ceoDisplayName, 0.84f);
                Row("Age", "年龄", gs.CeoAge.ToString(), 0.74f);
                Row("Nation", "国籍", gs.ceoNationality, 0.64f);
                Row("Gender", "性别", gs.ceoGender, 0.54f);
                Row("Term", "合同期限", $"第 {gs.ceoContractStartYear}–{gs.CeoContractEndYear} 财年（{gs.ceoContractYears} 年）", 0.44f);
                Row("Left", "剩余任期", $"还剩 {gs.CeoContractYearsRemaining} 个财年（含本年）", 0.34f);
                Row("Pay", "税后周薪", $"{gs.ceoWeeklyPayWan:F2} 万美元/周", 0.24f);
                int routes = gs.activeRoutes != null ? gs.activeRoutes.Count : 0;
                Row("Hist", "历史", CeoAchievementLogic.FormatHistory(gs.decisionLog, gs.year, routes), 0.14f);
                Row("Ach", "成就", CeoAchievementLogic.FormatAchievements(
                    gs.greatRouteUnlocked, gs.unlockedLegendMode, gs.unlockedNewGamePlus, gs.unlockedFastMode,
                    gs.gameOverVictory, gs.lastLegacyGrade, routes), 0.04f);
            }
            else
            {
                Row("Title", "职位", "首席执行官（CEO）", 0.84f);
                Row("Co", "任职公司", $"{gs.companyNameZh} / {gs.companyNameEn}", 0.74f);
                Row("Term", "期限", $"第 {gs.ceoContractStartYear} 财年 起，共 {gs.ceoContractYears} 个财年（至第 {gs.CeoContractEndYear} 财年）", 0.64f);
                Row("Pay", "税后周薪", $"{gs.ceoWeeklyPayWan:F2} 万美元", 0.54f);
                Row("Bonus", "奖金", $"年终 {gs.ceoBonusWan:F2} 万美元（续聘达标后发放，计入公司现金流）", 0.44f);
                Row("Left", "剩余任期", $"还剩 {gs.CeoContractYearsRemaining} 个财年（含本年）", 0.34f);
                Row("Note", "备注", "合同到期：达标则续约，未达标则解雇（本局结束）。见习期内未到期可再给一年。", 0.18f);
            }
        }

        protected override void Awake()
        {
            base.Awake();
            HideCloseXButtons(transform);
        }

        public override void Show()
        {
            base.Show();
            var body = transform.Find("Body");
            if (body != null) Fill(body, _contract);
        }
    }
}

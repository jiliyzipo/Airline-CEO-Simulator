#if UNITY_EDITOR
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using AirlineCEO.UI;
using AirlineCEO.UI.Shell;
using TMPro;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// 收件箱列表行的排版（2026-10-28 报障）隔离回归。
    ///
    /// 报障原文：「邮件的主题，两行就挤在一起，发件人也被挤占」。
    /// 截图里「新闻官 王志强」被来文主题的第一行盖掉一半。
    ///
    /// 两个原因叠在一起：
    /// ① 列表行固定 52px，发件人与主题各占一条窄带，而 `UIFactory.Label` 默认
    ///    Wrap + Overflow —— 主题一长就折成两行，从窄带里上下溢出去压住发件人；
    /// ② 来文的 `subject` 自带「2026年10月28日 · 」前缀，而行的上方就是日期分组头、
    ///    右侧就是具体时刻 —— 这段重复文字把「哪个台风、多强」挤出了可视宽度。
    ///
    /// 钉住：单行收敛的三个属性、主题前缀只在「确实是日期」时才去掉、行高随字号等比。
    /// 排版是否真的不重叠仍属界面手测（本回归不宣称已目视确认）。
    /// </summary>
    public static class InboxRowLayoutRegression
    {
        public static bool Run()
        {
            var go = new GameObject("__inbox_row_layout__");
            try
            {
                bool all = true;

                // ① 单行收敛：Wrap + Overflow 就是报障的成因，必须三个属性一起收
                var labelGo = new GameObject("__row_label__", typeof(RectTransform), typeof(CanvasRenderer));
                labelGo.transform.SetParent(go.transform, false);
                var label = labelGo.AddComponent<TextMeshProUGUI>();
                UIFactory.ClampSingleLine(label);
                bool nowrap = label.textWrappingMode == TextWrappingModes.NoWrap;
                bool ellipsis = label.overflowMode == TextOverflowModes.Ellipsis;
                bool oneLine = label.maxVisibleLines == 1;
                all &= nowrap && ellipsis && oneLine;
                if (!nowrap || !ellipsis || !oneLine)
                {
                    Debug.LogError(
                        $"列表行-① 单行收敛不完整：wrap={label.textWrappingMode} "
                        + $"overflow={label.overflowMode} maxLines={label.maxVisibleLines}；"
                        + "期望 NoWrap / Ellipsis / 1（否则长主题会折行压住发件人）");
                }

                // ② 来文主题：日期前缀去掉，风暴名与强度留下
                var mail = new InboxMail
                {
                    kind = "official",
                    subject = "2026年10月28日 · 台风“烟花”（超强台风）西移动，中心气压 920 百帕"
                };
                string row = InboxManager.RowSubject(mail);
                const string want = "[来文] 台风“烟花”（超强台风）西移动，中心气压 920 百帕";
                bool stripped = row == want;
                all &= stripped;
                if (!stripped) Debug.LogError($"列表行-② 来文主题不符：得到「{row}」期望「{want}」");

                // ②b 详情页仍用原始主题（前缀只影响列表这一条显示路径）
                bool detailKept = mail.subject.StartsWith("2026年10月28日 · ");
                all &= detailKept;
                if (!detailKept) Debug.LogError("列表行-②b 原始 subject 被改写：详情页标题会跟着丢日期");

                // ③ 不是日期的前缀一律保留，别把正常主题砍了
                var internalMail = new InboxMail { kind = "report", subject = "内部 · 季度考核口径" };
                string kept = InboxManager.RowSubject(internalMail);
                bool keptOk = kept == "[汇报] 内部 · 季度考核口径";
                all &= keptOk;
                if (!keptOk) Debug.LogError($"列表行-③ 误砍非日期前缀：得到「{kept}」");

                // ③b 「年」「月」缺一不可：只带「月」的短语不是日期
                bool noYear = InboxManager.StripDatePrefix("10月28日 · 台风") == "10月28日 · 台风";
                all &= noYear;
                if (!noYear) Debug.LogError("列表行-③b 只含「月/日」的短语被当成日期前缀砍掉了");

                // ④ 空值不炸（列表重建时可能遇到半初始化的邮件）
                bool nullSafe = InboxManager.RowSubject(null) == ""
                    && InboxManager.StripDatePrefix(null) == ""
                    && InboxManager.StripDatePrefix("") == "";
                all &= nullSafe;
                if (!nullSafe) Debug.LogError("列表行-④ 空邮件 / 空主题传进去会炸或返回非空");

                // ⑤ 行高随无障碍字号等比：字放大而行高不动，等于把挤压缩小后原地复发。
                //    「特大」1.30 下 52px 的行装不下 21px + 18px 两行字。
                all &= CheckRowHeights();

                if (all) Debug.Log("收件箱列表行排版：单行收敛 + 日期前缀去除 + 行高随字号缩放，5 组口径全部通过");
                return all;
            }
            finally
            {
                Accessibility.SetFontScale(FontScale.Standard);
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// 判定：给定行高与字号倍率，两条文字带是否各装得下自己那行字，且行高确实随倍率长。
        ///
        /// 抽成纯函数是为了能被**反向自证**（见 <see cref="CheckRowHeights"/> 的 canary）：
        /// 一个只会在正确实现下返回 true 的判定，无法证明它抓得住错误实现。
        /// </summary>
        static bool RowHeightSuffices(float rowH, float mult, out string why)
        {
            why = null;
            if (rowH < 52f * mult)
            {
                why = $"rowH={rowH} 未随倍率 {mult} 增长（至少 {52f * mult}）";
                return false;
            }
            int senderFont = Mathf.RoundToInt((14 + 2) * mult);
            int subjectFont = Mathf.RoundToInt((12 + 2) * mult);
            float senderBand = rowH * (0.95f - 0.55f);
            float subjectBand = rowH * (0.55f - 0.08f);
            if (senderBand < senderFont || subjectBand < subjectFont)
            {
                why = $"发件人带 {senderBand:F1} vs 字号 {senderFont}；主题带 {subjectBand:F1} vs 字号 {subjectFont}（rowH={rowH}）";
                return false;
            }
            return true;
        }

        /// <summary>
        /// 三档字号下，两条文字带都要装得下各自那行字。
        ///
        /// 倍率与字号换算都取**真实实现**（`Accessibility.FontMultiplier` /
        /// `UIFactory.Label` 的 `RoundToInt((size + 2) * 倍率)`），边界取 `CreateRow` 的
        /// 发件人 [0.55, 0.95] 与主题 [0.08, 0.55]。
        ///
        /// canary：先拿"行高写死 52"这个已知的错误实现喂给判定，它**必须**拒绝。
        /// 否则这条断言只是把实现重复了一遍，绿的没有意义。
        /// 这也是 2026-10-28 加的工具链要求：守卫型断言必须有一条反向自证，
        /// 首跑即绿的检查在本项目里默认视为"什么都没测"。
        /// </summary>
        static bool CheckRowHeights()
        {
            bool all = true;

            // canary：52px 固定行高在「特大」下装不下 21 + 18 两行字，判定必须拒绝
            if (RowHeightSuffices(52f, 1.30f, out _))
            {
                all = false;
                Debug.LogError(
                    "列表行-⑤ canary 失效：判定放过了「行高写死 52 / 字号 1.30」这个已知错误实现，"
                    + "说明这条检查抓不住回归，必须修判定本身");
            }

            var levels = new[] { FontScale.Standard, FontScale.Large, FontScale.XLarge };
            foreach (var level in levels)
            {
                Accessibility.SetFontScale(level);
                float mult = Accessibility.FontMultiplier();
                float rowH = InboxPanelUI.RowHeight();
                string why = rowH > 0f ? null : $"rowH={rowH} 非法";
                bool ok = rowH > 0f && RowHeightSuffices(rowH, mult, out why);
                if (!ok)
                {
                    all = false;
                    Debug.LogError($"列表行-⑤「{Accessibility.FontScaleLabel(level)}」不通过：{why}");
                }

                int dayFont = Mathf.RoundToInt((12 + 2) * mult);
                float dayH = InboxPanelUI.DayHeaderHeight();
                if (dayH < dayFont)
                {
                    all = false;
                    Debug.LogError($"列表行-⑤「{Accessibility.FontScaleLabel(level)}」日期表头装不下：dayH={dayH} < 字号 {dayFont}");
                }
            }
            return all;
        }
    }
}
#endif

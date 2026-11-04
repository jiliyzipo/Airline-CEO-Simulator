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

                // ①b 主题两行（2026-11-04 用户裁定）：单行时「请决策：开通伦敦希思罗 LHR → …」
                //    被截在目的地之前，两条同前缀的决策件在列表里长得一模一样。
                var subjectGo = new GameObject("__row_subject__", typeof(RectTransform), typeof(CanvasRenderer));
                subjectGo.transform.SetParent(go.transform, false);
                var subjectProbe = subjectGo.AddComponent<TextMeshProUGUI>();
                UIFactory.ClampLines(subjectProbe, InboxPanelUI.SubjectLines);
                bool wrapsTwo = subjectProbe.textWrappingMode == TextWrappingModes.Normal
                                && subjectProbe.overflowMode == TextOverflowModes.Ellipsis
                                && subjectProbe.maxVisibleLines == 2;
                all &= wrapsTwo;
                if (!wrapsTwo)
                {
                    Debug.LogError(
                        $"列表行-①b 主题不是「两行 + 末行省略」：wrap={subjectProbe.textWrappingMode} "
                        + $"overflow={subjectProbe.overflowMode} maxLines={subjectProbe.maxVisibleLines}；"
                        + "期望 Normal / Ellipsis / 2");
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

                // ⑥ 主题与时间同带（2026-11-03 报障：「主题和时间一高一低」）
                all &= CheckSubjectTimeAligned();

                // ⑦ 单行主题落在行心附近、两行主题块居中（2026-11-04 两轮报障：先贴底、后偏下）
                all &= CheckVerticalPlacement();

                if (all) Debug.Log("收件箱列表行排版：单行收敛 + 主题两行 + 日期前缀去除 + 行高随字号缩放 + 主题时间同带 + 单行居中/两行块居中，8 组口径全部通过");
                return all;
            }
            finally
            {
                Accessibility.SetFontScale(FontScale.Standard);
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// ⑦ 单行主题落在行心附近、时间**永远垂直居中** —— 贴底与「中间偏下」都不许。
        ///
        /// 2026-11-04 报了三轮：先「所有的邮件标题和时间都在栏的底部」（全 Bottom），
        /// 改全 Middle 后又「单行中间偏下」（单行被放进两行满带，带心只有 0.395），
        /// 再「两行主题的时间对着下面一行」（时间贴末行 BottomRight）。
        /// 最终口径：两行 = 满带块居中 + 时间同带 Middle（两行正中间）；
        /// 单行 = 换到居中带（带心 0.46）+ Middle。行内不再出现任何 Bottom。
        ///
        /// 判定四条：
        /// ① 单行带中心在 0.5±0.08（行心附近），且**高于**两行满带中心（否则单行仍偏下）；
        /// ② 两行主题与两行时间的垂直对齐都是 Middle（时间居中，不贴末行）；
        /// ③ 单行主题与单行时间同一种垂直对齐（Middle），不会一高一低；
        /// ④ canary：「全 Bottom」与「两行时间贴末行」这两个已知错误口径必须被拒。
        /// </summary>
        static bool CheckVerticalPlacement()
        {
            bool all = true;
            const int VerticalMask = 0xF00;   // TMP：VerticalAlignmentOptions = 0x100/0x200/0x400...
            int middle = (int)VerticalAlignmentOptions.Middle;
            int bottom = (int)VerticalAlignmentOptions.Bottom;

            float singleCenter = (InboxPanelUI.SubjectSingleBandMin + InboxPanelUI.SubjectSingleBandMax) * 0.5f;
            float twoLineCenter = (InboxPanelUI.SubjectBandMin + InboxPanelUI.SubjectBandMax) * 0.5f;
            if (Mathf.Abs(singleCenter - 0.5f) > 0.08f)
            {
                all = false;
                Debug.LogError($"列表行-⑦ 单行带中心偏离行心：{singleCenter:F3}，期望 0.5±0.08");
            }
            if (singleCenter <= twoLineCenter)
            {
                all = false;
                Debug.LogError($"列表行-⑦ 单行带没有比两行满带更靠上：single={singleCenter:F3} ≤ two={twoLineCenter:F3}"
                               + "（单行仍会「中间偏下」）");
            }

            int subjectV = (int)InboxPanelUI.SubjectAlign & VerticalMask;
            int timeV = (int)InboxPanelUI.TimeAlign & VerticalMask;
            int singleSubjectV = (int)InboxPanelUI.SubjectSingleAlign & VerticalMask;
            int singleTimeV = (int)InboxPanelUI.TimeSingleAlign & VerticalMask;
            if (subjectV != middle)
            {
                all = false;
                Debug.LogError($"列表行-⑦ 两行主题不是块居中：0x{subjectV:X}，期望 Middle=0x{middle:X}");
            }
            if (timeV != middle)
            {
                all = false;
                Debug.LogError($"列表行-⑦ 两行主题的时间不居中：0x{timeV:X}，期望 Middle=0x{middle:X}"
                               + "（贴末行就是 2026-11-04 第三轮报障的「时间对着下面一行」）");
            }
            if (singleSubjectV != middle || singleTimeV != middle)
            {
                all = false;
                Debug.LogError($"列表行-⑦ 单行主题/时间不是垂直居中：0x{singleSubjectV:X}/0x{singleTimeV:X}，"
                               + $"期望 Middle=0x{middle:X}");
            }

            // canary：两个已知错误口径（全 Bottom / 两行时间贴末行）必须被拒
            int wrongAllBottomV = (int)TextAlignmentOptions.BottomLeft & VerticalMask;
            int wrongTimeBottomV = (int)TextAlignmentOptions.BottomRight & VerticalMask;
            if (wrongAllBottomV == middle || wrongTimeBottomV == middle)
            {
                all = false;
                Debug.LogError("列表行-⑦ canary 失效：判定分不出 Bottom 与 Middle，"
                               + "抓不住「都在栏的底部」/「时间对着下面一行」");
            }
            return all;
        }

        /// <summary>
        /// ⑥ 时间**永远**在居中带，且与主题不互相压字。
        ///
        /// 2026-11-03 报障：「左侧邮件主题和时间的位置没有对齐，一高一低」——旧版把时间钉在
        /// 发件人那条带（[0.52,0.95]），列表看上去只剩「主题」和悬在它上方的「时间」两截。
        /// 2026-11-04 第三轮报障后口径改为：时间常驻居中带（与单行主题同带），
        /// 两行主题占满带、时间落在两行之间偏上的行心位置。判定四条：
        /// ① 时间的上下沿 == 居中带（<see cref="InboxPanelUI.SubjectSingleBandMin"/>..Max）；
        /// ② 单行主题与时间同带（不会一高一低）；
        /// ③ 左右不重叠（主题右界 ≤ 时间左界）；
        /// ④ 两行主题的带**包含**时间的带（时间不会探出主题块上下沿）。
        /// canary：把「时间钉在发件人带上」这个已知错误口径喂进同一判定，它必须拒绝。
        /// </summary>
        static bool CheckSubjectTimeAligned()
        {
            bool all = true;

            var subject = InboxPanelUI.SubjectAnchor();
            var subjectSingle = InboxPanelUI.SubjectSingleAnchor();
            var time = InboxPanelUI.TimeAnchor();

            bool timeCentered = Mathf.Approximately(time.min.y, InboxPanelUI.SubjectSingleBandMin)
                                && Mathf.Approximately(time.max.y, InboxPanelUI.SubjectSingleBandMax);
            bool sameBandAsSingle = Mathf.Approximately(subjectSingle.min.y, time.min.y)
                                    && Mathf.Approximately(subjectSingle.max.y, time.max.y);
            bool noOverlap = subject.max.x <= time.min.x + 0.0001f;
            bool insideTwoLine = time.min.y >= subject.min.y - 0.0001f
                                 && time.max.y <= subject.max.y + 0.0001f;
            if (!timeCentered || !sameBandAsSingle || !noOverlap || !insideTwoLine)
            {
                all = false;
                Debug.LogError($"列表行-⑥ 时间没有常驻居中带：时间 y[{time.min.y},{time.max.y}]，"
                               + $"居中带 y[{InboxPanelUI.SubjectSingleBandMin},{InboxPanelUI.SubjectSingleBandMax}]；"
                               + $"单行主题同带={sameBandAsSingle} 不压字={noOverlap} 两行带包含={insideTwoLine}");
            }

            // canary：旧口径（时间钉在发件人带上）必须被判为「不在居中带」
            var wrong = new UIFactory.Anchor(new Vector2(0.68f, 0.52f), new Vector2(0.97f, 0.95f),
                Vector2.zero, Vector2.zero);
            bool wrongRejected = !(Mathf.Approximately(wrong.min.y, InboxPanelUI.SubjectSingleBandMin)
                                   && Mathf.Approximately(wrong.max.y, InboxPanelUI.SubjectSingleBandMax));
            if (!wrongRejected)
            {
                all = false;
                Debug.LogError("列表行-⑥ canary 失效：判定放过了「时间钉在发件人带上」这个已知错误口径");
            }

            // 「特大」字号下下带仍装得下主题那两行字
            Accessibility.SetFontScale(FontScale.XLarge);
            float mult = Accessibility.FontMultiplier();
            float rowH = InboxPanelUI.RowHeight();
            int subjectFont = Mathf.RoundToInt((12 + 2) * mult);
            float band = rowH * (InboxPanelUI.SubjectBandMax - InboxPanelUI.SubjectBandMin);
            Accessibility.SetFontScale(FontScale.Standard);
            float need = subjectFont * InboxPanelUI.SubjectLines;
            if (band < need)
            {
                all = false;
                Debug.LogError($"列表行-⑥「特大」下主题带装不下 {InboxPanelUI.SubjectLines} 行：band={band:F1} < {need}");
            }
            return all;
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
            int senderFont = Mathf.RoundToInt((14 + 2) * mult);
            int subjectFont = Mathf.RoundToInt((12 + 2) * mult);
            float senderBand = rowH * (InboxPanelUI.SenderBandMax - InboxPanelUI.SenderBandMin);
            float subjectBand = rowH * (InboxPanelUI.SubjectBandMax - InboxPanelUI.SubjectBandMin);
            float subjectNeed = subjectFont * InboxPanelUI.SubjectLines;
            if (senderBand < senderFont || subjectBand < subjectNeed)
            {
                why = $"发件人带 {senderBand:F1} vs 字号 {senderFont}；"
                      + $"主题带 {subjectBand:F1} vs {InboxPanelUI.SubjectLines} 行 {subjectNeed}（rowH={rowH}）";
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

            // canary：52px 固定行高在「特大」下连发件人那行都装不下，判定必须拒绝
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

                // 行高必须跟着字号倍率长：字变大而行高不动，等于把挤压缩小后原地复发。
                float expected = Mathf.Ceil(InboxPanelUI.BaseRowHeight * mult);
                if (Mathf.Abs(rowH - expected) > 0.01f)
                {
                    all = false;
                    Debug.LogError(
                        $"列表行-⑤「{Accessibility.FontScaleLabel(level)}」行高没随倍率 {mult} 长："
                        + $"rowH={rowH}，应为 {expected}（{InboxPanelUI.BaseRowHeight}×倍率）");
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

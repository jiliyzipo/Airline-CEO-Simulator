#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// 新闻官转呈（2026-10-24）的隔离回归。
    ///
    /// 改这层的起因：收件箱原先拿**发布单位**当发件人 —— 列表上排着「中央气象台 / 发改委 / 路透社」，
    /// 像公司自己开了一排机构的邮箱。真实公司只有一个对外入口：新闻官把各家来文原文整理后转呈 CEO。
    ///
    /// 钉住五件事：
    /// ① 发件人是新闻官（具名），发布单位退到正文；原文正文一字不改，机关名与文日改由转呈抬头点明；
    /// ② 姓名按存档派生：同一存档恒定（读档、跨年都不改名），不同开局不同名；
    /// ③ 姓名不进人事名单 —— 不占编制、不领薪、不可任命，故与人事去重无关；
    /// ④ 信笺识别：新闻官的信不会被拼成「新闻官 · 负责人 张三」这种双重职务；
    /// ⑤ 正文末尾只有**转呈人落款**（新闻官 姓名 / 日期），不再出现右对齐的机关落款块 ——
    ///    一封信两个落款，CEO 不知道该找谁。
    /// </summary>
    public static class PressDeskRegression
    {
        public static bool Run()
        {
            var go = new GameObject("__press_desk__");
            try
            {
                var gs = go.AddComponent<GameState>();
                gs.inbox = new List<InboxMail>();
                gs.dynamicFeed = new List<DynamicFeedItem>();
                gs.externalEvents = new List<ExternalEvent>();
                gs.staffPool = new List<PersonnelData>();
                gs.year = 1;
                gs.quarter = 1;
                gs.simDay = 1;
                gs.ceoNationality = "中国";
                gs.companyNameZh = "天际航空";
                gs.companyNameEn = "Skyline Air";
                gs.companyIata = "NV";
                gs.companyIcao = "SKY";
                gs.hubIata = "PEK";
                gs.ceoDisplayName = "阿斯芬";
                gs.ceoBirthYear = 1985;
                gs.SetSimUtc(new DateTime(2030, 5, 10, 15, 0, 0, DateTimeKind.Utc));

                bool all = true;

                // ① 具名、可重复：同一存档连取两次必须同名
                string n1 = PressDesk.Name(gs);
                string n2 = PressDesk.Name(gs);
                bool named = !string.IsNullOrEmpty(n1) && n1.Length >= 2;
                bool stable = named && n1 == n2;
                all &= stable;
                if (!stable) Debug.LogError($"新闻官-① 同存档姓名不稳：「{n1}」/「{n2}」");

                // ② 时间推进不改名（year / quarter / simDay 绝不参与派生）
                gs.year = 9; gs.quarter = 4; gs.simDay = 213;
                bool timeProof = PressDesk.Name(gs) == n1;
                all &= timeProof;
                if (!timeProof) Debug.LogError($"新闻官-② 跨年后改名：「{n1}」→「{PressDesk.Name(gs)}」（时间字段不该参与）");
                gs.year = 1; gs.quarter = 1; gs.simDay = 1;

                // ③ 换存档就换人：身份字段（公司名）变了，姓名必须跟着变
                bool differs = false;
                for (int i = 0; i < 6 && !differs; i++)
                {
                    gs.companyNameZh = "测试航空" + i;
                    differs = PressDesk.Name(gs) != n1;
                }
                gs.companyNameZh = "天际航空";
                all &= differs;
                if (!differs) Debug.LogError("新闻官-③ 换公司名后仍是同一人（存档标识没吃到身份字段）");
                bool restored = PressDesk.Name(gs) == n1;
                all &= restored;
                if (!restored) Debug.LogError("新闻官-③b 身份复原后姓名没复原");

                // ④ 信笺识别：新闻官的信不等于「部门 + 负责人」
                InboxLetter.Identify(gs, n1, "新闻官 · 预警通报", out string dept, out string post, out string name);
                bool idOk = dept == PressDesk.Title && post.Length == 0 && name == n1;
                all &= idOk;
                if (!idOk) Debug.LogError($"新闻官-④ 识别不符：dept=「{dept}」post=「{post}」name=「{name}」");

                string sig = InboxLetter.Sign(gs, n1, "新闻官 · 预警通报", "2030年5月10日");
                string sigLine = sig.Split('\n')[0];
                bool signOk = sigLine == PressDesk.Title + " " + n1;
                all &= signOk;
                if (!signOk) Debug.LogError($"新闻官-④b 落款不符：「{sigLine}」期望「{PressDesk.Title} {n1}」");

                // ⑤ 真邮件：外源来文投递后，发件人是新闻官，原文与来源仍在
                var payload = ExternalDataFetcher.DemoPayloads()[1];   // 台风预警（政府公文）
                var ev = ExternalEventManager.Receive(gs, payload);
                var mail = gs.inbox != null && gs.inbox.Count > 0 ? gs.inbox[gs.inbox.Count - 1] : null;
                bool sent = ev != null && mail != null;
                bool senderOk = sent && mail.sender == n1
                                && PressDesk.IsPressOfficer(gs, mail.sender);
                bool roleOk = sent && mail.role == "新闻官 · 预警通报";
                bool glyphOk = sent && mail.glyph == PressDesk.Glyph;
                bool bodyOk = sent
                              && mail.body.Contains("整理转呈")
                              && mail.body.Contains(payload.source)      // 发布单位在正文里
                              && mail.body.Contains("特此通报");           // 原文体例没被改写
                bool noDeptLeak = sent && mail.sender != payload.source;   // 不再拿发布单位当发件人
                // ⑥ 转呈人落款：正文最后两行必须是「新闻官 姓名」+ 日期。
                //    原先正文只以原文机关落款收尾（中央气象台），读起来像局方写给 CEO 的，找不到递件人。
                string signLabel = PressDesk.SignLabel(n1);
                string[] bodyLines = sent
                    ? mail.body.Replace("\r\n", "\n").Split('\n')
                    : new string[0];
                var tail = new List<string>();
                for (int i = bodyLines.Length - 1; i >= 0 && tail.Count < 2; i--)
                {
                    string l = bodyLines[i].Trim();
                    if (l.Length > 0) tail.Insert(0, l);
                }
                bool signOk2 = tail.Count == 2 && tail[0] == signLabel
                               && tail[1].Contains("年") && tail[1].Contains("日");
                bool noAgencySign = sent && !HasExactLine(bodyLines, payload.source);
                all &= senderOk && roleOk && glyphOk && bodyOk && noDeptLeak && signOk2 && noAgencySign;
                if (!all) Debug.LogError($"新闻官-⑤ 转呈件不符：发件人=「{mail?.sender}」角色=「{mail?.role}」"
                    + $" 头像=「{mail?.glyph}」 含转呈={mail != null && mail.body.Contains("整理转呈")}"
                    + $" 机关落款已撤={noAgencySign}"
                    + $" 转呈落款=「{string.Join(" / ", tail)}」期望「{signLabel} / …年…月…日」");

                Debug.Log($"新闻官 具名={named}（{n1}）　同存档恒定={stable}　跨年不改名={timeProof}"
                          + $"　换存档换人={differs}　识别={idOk}　落款=「{sigLine}」\n"
                          + $"　外源投递 发件人={senderOk} 角色={roleOk}（{mail?.role}） 头像={glyphOk}"
                          + $" 原文保留={bodyOk} 不再露出发布单位作发件人={noDeptLeak}\n"
                          + $"　转呈落款={signOk2}（{string.Join(" / ", tail)}） 机关落款已撤={noAgencySign}\n"
                          + $"　正文首行：{(mail != null && mail.body.Length > 0 ? mail.body.Split('\n')[0] : "")}");
                return all;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        /// <summary>正文里是否存在「整行只剩这个机关名」的行（即原文的右对齐机关落款块）。</summary>
        static bool HasExactLine(string[] lines, string text)
        {
            string t = (text ?? "").Trim();
            if (lines == null || t.Length == 0) return false;
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].Trim() == t) return true;
            return false;
        }
    }
}
#endif

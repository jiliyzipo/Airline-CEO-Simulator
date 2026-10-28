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
    /// 转呈件的**信面**口径（2026-10-28）：一封信只署一个落款，就是新闻官。
    ///
    /// 为什么单独一支：`LogicSelfTests` 外事段原先用 `Debug.Log` 打印这几条，
    /// 靠人看 Console —— 那是观察，不是回归。§39.6 记录了这个缺口，这里把它补成
    /// 可机器执行的断言，`LogicSelfTests` 只留打印。
    ///
    /// 与 <see cref="PressDeskRegression"/> 的分工：
    /// 那边管「人是谁」（具名、恒定、换存档换人、信笺识别）；
    /// 这边管「信长什么样」（抬头、落款位置、机关落款撤没撤），四种体例全覆盖。
    /// </summary>
    public static class ExternalForwardingRegression
    {
        public static bool Run()
        {
            var go = new GameObject("__external_forwarding__");
            try
            {
                var gs = go.AddComponent<GameState>();
                gs.inbox = new List<InboxMail>();
                gs.dynamicFeed = new List<DynamicFeedItem>();
                gs.externalEvents = new List<ExternalEvent>();
                gs.staffPool = new List<Data.PersonnelData>();
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

                var recv = new DateTime(2030, 5, 10, 15, 0, 0, DateTimeKind.Utc);
                gs.SetSimUtc(recv);

                bool all = true;
                string officer = PressDesk.Name(gs);
                string signLabel = PressDesk.SignLabel(officer);

                // 四种体例各来一封：政府公文 / 通讯社电讯 / 企业公告 / 机构提示函。
                // 只有前三种原本带「机关名 + 日期」的右对齐落款块，News 体例从来只有电头，
                // 所以四种都要跑 —— 否则「机关落款已撤」可能只在一个分支上成立。
                //
                // 每封都必须带 effectType：`Trigger` 只对**有公司侧影响**的事件补发「已施行」确认函，
                // effectType 为空就根本不会有第二封（这是有意的产品行为，不是缺陷）。
                var cases = new[]
                {
                    MakePayload("ndrc_fuel", "发改委", "市场", "发改委调整成品油价格",
                        "国家发展改革委决定，国内汽、柴油价格每吨分别上调 260 元。",
                        "fuelAbs", 26f, null, 0f),
                    MakePayload("typhoon", "中央气象台", "季节", "台风将影响粤东机场",
                        "今年第14号台风预计3天后登陆粤东，汕头机场可能短时关闭。",
                        "freezeFlights", 0f, "SWA", 48f),
                    MakePayload("opec", "路透社", "市场", "OPEC+ 就减产配额产生分歧",
                        "据路透社报道，OPEC+ 成员国就下阶段减产配额分歧加大。",
                        "fuelPrice", 4f, null, 0f),
                    MakePayload("boeing", "波音公司", "行业", "波音发布新机型适航通告",
                        "波音公司通告，737 MAX 系列新批次需完成方向舵作动器检查。",
                        "compliance", 0f, null, 0f),
                    MakePayload("rating", "标普全球", "市场", "评级机构下调信用展望",
                        "标普全球将相关航司信用展望由「稳定」下调为「负面」。",
                        "cash", -200f, null, 0f),
                };

                var report = new List<string>();
                foreach (var p in cases)
                {
                    gs.SetSimUtc(recv);
                    var ev = ExternalEventManager.Receive(gs, p);
                    if (ev == null)
                    {
                        all = false;
                        Debug.LogError($"转呈件-① 投递失败：{p.source}");
                        continue;
                    }

                    // 预告信：当场就该能检查信面
                    bool pre = Check(gs, p.source, "预告", signLabel, out string preWhich);
                    all &= pre;
                    if (!pre) Debug.LogError($"转呈件-① 预告信面不符：{p.source} → {preWhich}");
                    report.Add($"预告 {p.source}：{preWhich}");

                    // 施行通报：同一封来文在生效时刻再发一封，信面口径必须一致
                    ExternalEventManager.Tick(gs, recv, ev.GameEffectiveTime.AddHours(1));
                    bool post = Check(gs, p.source, "施行通报", signLabel, out string postWhich);
                    all &= post;
                    if (!post) Debug.LogError($"转呈件-② 施行通报信面不符：{p.source} → {postWhich}");
                    report.Add($"施行 {p.source}：{postWhich}");
                }

                // ③ 无公司侧影响的来文不补「已施行」确认函 —— 这是 `Trigger` 的有意行为，
                //    钉住它，免得以后有人把「没有第二封」当成漏发去改坏。
                gs.SetSimUtc(recv);
                var quiet = MakePayload("quiet_news", "航空周刊", "行业", "某航司开通与本枢纽重叠的洲际新航线",
                    "据航空周刊报道，某航司宣布开通与本公司枢纽重叠的洲际新航线。",
                    "", 0f, null, 0f);
                bool quietOk = true;
                var quietEv = ExternalEventManager.Receive(gs, quiet);
                if (quietEv != null)
                {
                    ExternalEventManager.Tick(gs, recv, quietEv.GameEffectiveTime.AddHours(1));
                    quietOk = FindLatest(gs, "航空周刊", "施行通报") == null;
                }
                all &= quietOk;
                if (!quietOk) Debug.LogError("转呈件-③ 无影响的来文不该补「已施行」确认函");

                // ④ 兜底：整个收件箱里，任何一封以新闻官名义发的外部来文，
                //    都不许出现「整行只剩发布单位名」的行（那是旧版的原机关落款块）。
                var agencies = new HashSet<string>();
                foreach (var p in cases) agencies.Add(p.source);
                int leaked = 0;
                for (int i = 0; i < gs.inbox.Count; i++)
                {
                    var m = gs.inbox[i];
                    if (m == null || !PressDesk.IsPressOfficer(gs, m.sender)) continue;
                    foreach (var agency in agencies)
                        if (HasExactLine(m.body, agency)) { leaked++; break; }
                }
                all &= leaked == 0;
                if (leaked != 0) Debug.LogError($"转呈件-④ 仍有 {leaked} 封信带着右对齐的机关落款块");

                Debug.Log($"转呈件 落款口径 all={all} 泄漏={leaked} 无影响不补确认函={quietOk}\n　"
                    + string.Join("\n　", report));
                return all;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        /// <summary>检查某一类来文的信面：抬头带机关与文日、落款只有新闻官、机关落款块已撤。</summary>
        static bool Check(GameState gs, string source, string tag, string signLabel, out string which)
        {
            which = "";
            var mail = FindLatest(gs, source, tag);
            if (mail == null) { which = "（没找到这封信）"; return false; }

            if (!PressDesk.IsPressOfficer(gs, mail.sender)) { which = $"发件人不是新闻官（{mail.sender}）"; return false; }

            // 抬头：机关名 + 文日都必须在，且写明转呈
            if (!mail.body.Contains("整理转呈")) { which = "缺转呈抬头"; return false; }
            if (!mail.body.Contains(source)) { which = "抬头没点明来文机关"; return false; }
            if (!mail.body.Contains("年")) { which = "抬头没带文日"; return false; }

            // 机关落款块：整行只剩机关名的行必须为 0
            if (HasExactLine(mail.body, source)) { which = "机关落款块还在"; return false; }

            // 末尾两行 = 「新闻官 姓名」+ 文日
            var tail = Tail(mail.body, 2);
            if (tail.Count != 2) { which = "正文末尾不足两行"; return false; }
            if (tail[0] != signLabel) { which = $"末尾署名是「{tail[0]}」不是「{signLabel}」"; return false; }
            if (!tail[1].Contains("年") || !tail[1].Contains("日")) { which = $"末尾日期异常（{tail[1]}）"; return false; }

            which = "ok";
            return true;
        }

        static InboxMail FindLatest(GameState gs, string source, string tag)
        {
            for (int i = gs.inbox.Count - 1; i >= 0; i--)
            {
                var m = gs.inbox[i];
                if (m == null || string.IsNullOrEmpty(m.body)) continue;
                if (!m.body.Contains(source)) continue;
                if (tag == "施行通报" && (m.subject == null || !m.subject.Contains("已施行"))) continue;
                if (tag == "预告" && m.subject != null && m.subject.Contains("已施行")) continue;
                return m;
            }
            return null;
        }

        static List<string> Tail(string body, int n)
        {
            var lines = (body ?? "").Replace("\r\n", "\n").Split('\n');
            var tail = new List<string>();
            for (int i = lines.Length - 1; i >= 0 && tail.Count < n; i--)
            {
                string l = lines[i].Trim();
                if (l.Length > 0) tail.Insert(0, l);
            }
            return tail;
        }

        static bool HasExactLine(string body, string text)
        {
            string t = (text ?? "").Trim();
            if (string.IsNullOrEmpty(body) || t.Length == 0) return false;
            var lines = body.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].Trim() == t) return true;
            return false;
        }

        static ExternalEventPayload MakePayload(string id, string source, string category, string title,
            string content, string effectType, float effectValue, string region, float durationHours)
            => new ExternalEventPayload
            {
                id = id,
                source = source,
                category = category,
                title = title,
                content = content,
                publishedAt = new DateTime(2030, 5, 10, 12, 0, 0, DateTimeKind.Utc),
                realEffectiveAt = new DateTime(2030, 5, 11, 0, 0, 0, DateTimeKind.Utc),
                effectType = effectType,
                effectValue = effectValue,
                affectedRegion = region,
                durationHours = (int)durationHours
            };
    }
}
#endif

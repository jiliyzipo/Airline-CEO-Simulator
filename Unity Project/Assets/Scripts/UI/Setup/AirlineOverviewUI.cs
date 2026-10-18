using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using TMPro;
using UnityEngine;

namespace AirlineCEO.UI.Setup
{
    /// <summary>接管开局：航司概览七个 Tab 的文本内容。</summary>
    public static class AirlineOverviewUI
    {
        public static readonly string[] Tabs =
            { "机队", "航线", "财务", "团队", "董事会", "竞争", "遗留问题" };

        public static string Header(AirlineProfile p)
        {
            if (p == null) return "";
            return $"{p.nameCN} · 总部 {p.hubAirport} · 难度 {TakeoverWorldBuilder.Stars(p.difficulty)}\n{p.blurb}";
        }

        public static string Metrics(AirlineProfile p)
        {
            if (p == null) return "";
            return $"机队 {p.fleetCount} 架 ｜ 航线 {p.routeCount} 条 ｜ 现金 {p.cash:F0} 万 ｜ 负债率 {p.debtRatio * 100f:F0}% ｜ 跑道约 {p.cashRunwayMonths:F1} 月";
        }

        public static string Body(AirlineProfile p, int tab)
        {
            if (p == null) return "";
            var sb = new StringBuilder();
            switch (tab)
            {
                case 0:
                    sb.AppendLine($"平均机龄 {p.avgFleetAge:F1} 年");
                    int show = p.aircraft != null ? Mathf.Min(p.aircraft.Count, 12) : 0;
                    for (int i = 0; i < show; i++)
                    {
                        var a = p.aircraft[i];
                        if (a == null) continue;
                        string biz = BizOf(p, a.registration);
                        sb.AppendLine($"· {a.registration}  {a.type}  机龄{a.age}年  余寿{a.remainingLifeYears}年  {biz}");
                    }
                    if (p.aircraft != null && p.aircraft.Count > show)
                        sb.AppendLine($"…另有 {p.aircraft.Count - show} 架");
                    break;
                case 1:
                    int rs = p.routes != null ? Mathf.Min(p.routes.Count, 14) : 0;
                    for (int i = 0; i < rs; i++)
                    {
                        var r = p.routes[i];
                        if (r == null) continue;
                        string tag = r.dailyProfit < 0f ? "亏损" : "盈利";
                        sb.AppendLine($"· {AirportCatalog.Od(r.origin, r.destination)}  {r.flightNumber}  {r.dailyProfit:+0.00;-0.00} 万/日  {tag}");
                    }
                    break;
                case 2:
                    sb.AppendLine($"现金 {p.cash:F0} 万美元");
                    sb.AppendLine($"月收入约 {p.monthlyRevenue:F0} 万（航线 + 过渡业务）");
                    sb.AppendLine($"月成本约 {p.monthlyCost:F0} 万");
                    sb.AppendLine($"负债率 {p.debtRatio * 100f:F0}%");
                    if (p.openingLoans != null)
                        for (int i = 0; i < p.openingLoans.Count; i++)
                            if (p.openingLoans[i] != null)
                                sb.AppendLine($"贷款 {p.openingLoans[i].id} 余本 {p.openingLoans[i].remainingPrincipal:F0} 万");
                    break;
                case 3:
                    sb.AppendLine($"机组短缺约 {p.crewVacancyPercent}%");
                    if (p.departmentHeads != null)
                        for (int i = 0; i < p.departmentHeads.Count; i++)
                        {
                            var h = p.departmentHeads[i];
                            if (h != null) sb.AppendLine($"· {h.title}  {h.name}");
                        }
                    break;
                case 4:
                    sb.AppendLine("现有 9 席董事会（含 CEO）。接管后沿用本届董事，年度目标随即下达。");
                    sb.AppendLine("· 您（CEO）　年龄 / 性别 / 国籍见玩家资料");
                    var ids = p.boardMemberIds;
                    if (ids != null)
                    {
                        for (int i = 0; i < ids.Count; i++)
                        {
                            var m = FindBoard(ids[i]);
                            if (m == null) continue;
                            var bp = SetupCatalogs.ProfileFor(m);
                            sb.AppendLine($"· {m.fullName}　{bp.age}岁　{bp.gender}　{bp.nationality}");
                            sb.AppendLine($"　观点：{SetupCatalogs.ViewFor(m)}");
                        }
                    }
                    break;
                case 5:
                    if (p.competitors != null)
                        for (int i = 0; i < p.competitors.Count; i++)
                        {
                            var c = p.competitors[i];
                            if (c == null) continue;
                            sb.AppendLine($"· {c.name}  {AIAirlineTypes.DisplayName(c.type)}  {AIAirlineStates.DisplayName(c.state)}  机队{c.fleetSize}");
                        }
                    break;
                default:
                    if (p.legacyIssues != null)
                        for (int i = 0; i < p.legacyIssues.Count; i++)
                        {
                            var x = p.legacyIssues[i];
                            if (x == null) continue;
                            sb.AppendLine($"[{LegacyIssue.UrgencyLabel(x.urgency)}] {x.title}");
                            sb.AppendLine($"　{x.description}");
                        }
                    break;
            }
            return sb.ToString().TrimEnd();
        }

        static string BizOf(AirlineProfile p, string reg)
        {
            if (p?.transitionBusinesses == null) return "自营";
            for (int i = 0; i < p.transitionBusinesses.Count; i++)
            {
                var c = p.transitionBusinesses[i];
                if (c != null && c.aircraftReg == reg)
                    return TransitionKinds.Label(c.kind);
            }
            return "自营";
        }

        static BoardMemberData FindBoard(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var list = DataBase.Instance != null ? DataBase.Instance.BoardMembers : null;
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
            {
                var m = list[i];
                if (m != null && m.id == id) return m;
            }
            return null;
        }
    }
}

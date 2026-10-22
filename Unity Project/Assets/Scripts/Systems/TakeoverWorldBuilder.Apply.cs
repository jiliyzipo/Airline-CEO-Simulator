using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>
    /// <see cref="TakeoverWorldBuilder"/> 的「写入局内存档」半边：把已构建的航司档案落进 GameState。
    /// 单独成 partial 文件，是因为这一步依赖 GameState（MonoBehaviour）与机组生成器，
    /// 而 BuildPreview/FillWorld 只依赖纯数据——离线平衡工具（tools/balance）因此可以只链接后者。
    /// </summary>
    public static partial class TakeoverWorldBuilder
    {
        public static void Apply(GameState gs, AirlineProfile profile)
        {
            if (gs == null || profile == null) return;
            if (profile.aircraft == null || profile.aircraft.Count == 0)
                FillWorld(profile, previewOnly: false);

            gs.takeoverMode = true;
            gs.takeoverAirlineId = profile.id;
            gs.takeoverBriefingShown = false;
            gs.companyNameZh = profile.nameCN;
            gs.companyNameEn = profile.nameEN;
            gs.companyIata = profile.iataCode;
            gs.companyIcao = profile.icaoCode;
            gs.hubIata = profile.hubAirport;
            gs.cash = profile.cash;
            gs.crewVacancyPercent = profile.crewVacancyPercent;
            gs.firstFlightCompleted = true;
            gs.advisorTutorialCompleted = true;
            gs.advisorTutorialStep = 7;
            gs.transitionIntroSent = true;
            gs.legacyIssues = profile.legacyIssues != null
                ? new List<LegacyIssue>(profile.legacyIssues)
                : new List<LegacyIssue>();

            gs.fleet = new List<Aircraft>();
            for (int i = 0; i < profile.aircraft.Count; i++)
                gs.fleet.Add(CloneAc(profile.aircraft[i]));
            gs.nextRegistrationNumber = gs.fleet.Count + 1;

            gs.activeRoutes = new List<RouteData>();
            for (int i = 0; i < profile.routes.Count; i++)
                gs.activeRoutes.Add(CloneRoute(profile.routes[i]));
            RouteGeography.PruneImplausible(gs.activeRoutes, AirlineCEO.UI.MapWidget.GreatCircleKm);

            gs.transitionContracts = new List<TransitionContract>();
            if (profile.transitionBusinesses != null)
            {
                for (int i = 0; i < profile.transitionBusinesses.Count; i++)
                    gs.transitionContracts.Add(CloneTc(profile.transitionBusinesses[i]));
            }
            gs.transitionOffers = new List<TransitionOffer>();

            gs.activeLoans = new List<Loan>();
            if (profile.openingLoans != null)
            {
                for (int i = 0; i < profile.openingLoans.Count; i++)
                    gs.activeLoans.Add(CloneLoan(profile.openingLoans[i]));
            }

            gs.boardMemberIds = profile.boardMemberIds != null
                ? new List<string>(profile.boardMemberIds)
                : new List<string>();

            float rev = 0f;
            for (int i = 0; i < gs.activeRoutes.Count; i++)
                if (gs.activeRoutes[i] != null)
                    rev += gs.activeRoutes[i].dailyProfit
                           * LiveClockLogic.OperatingDaysPerMonth(
                               gs.activeRoutes[i].daysOfWeek, gs.activeRoutes[i].weeklyFlights);
            gs.monthlyBurnRate = Mathf.Max(40f, profile.monthlyCost);
            // monthlyRevenue 是计算属性时不要赋值；若是字段则设
            TrySetMonthlyHint(gs, rev);

            gs.AddDecisionLog($"接管 {profile.nameCN}（{profile.iataCode}/{profile.icaoCode}）· 枢纽 {profile.hubAirport} · 机队 {gs.fleet.Count} · 航线 {gs.activeRoutes.Count}");

            // 继承的机组必须真正入模，否则「机组人员短缺 X%」与「未建机组 → 利润 ×0.85」
            // 会同时成立（两套口径分叉）：叙事说有机组，模拟按 0 人罚款。
            int crew = AirlineCEO.Logic.PersonnelGenerator.SeedInheritedCrew(gs, profile.crewVacancyPercent);
            if (crew > 0)
            {
                gs.AddDecisionLog($"人力：接收在册机组 {crew} 人（在编 {gs.PilotEstablishment} 飞行员编制，"
                                  + $"缺员 {profile.crewVacancyPercent}%）——缺编部分可在职业介绍所补齐。");
                gs.NotifyChanged();
            }
        }

        static void TrySetMonthlyHint(GameState gs, float routeMonthRev)
        {
            // monthlyRevenue 在 GameState 多为计算属性；烧钱用 monthlyBurnRate。
            _ = routeMonthRev;
            _ = gs;
        }
    }
}

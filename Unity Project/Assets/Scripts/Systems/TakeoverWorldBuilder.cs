using System;
using System.Collections.Generic;
using System.Text;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.Systems
{
    /// <summary>把预设航司展开为可接管的机队、航线、过渡合同、贷款与遗留问题。</summary>
    public static partial class TakeoverWorldBuilder
    {
        // 目的地池不再用写死的清单（原「国内 24 场 / 亚洲 9 场 / 远程 8 场」以中国为中心，
        // 令伦敦/迪拜/洛杉矶等海外枢纽的整条网络都被判为洲际远程，班次清一色 2–5）。
        // 现由 BuildDestinationPool 按枢纽自身所在国/大洲生成，并以机队航程上限过滤。

        public static AirlineProfile BuildPreview(string id)
        {
            var t = PresetAirlines.Resolve(id);
            FillWorld(t, previewOnly: true);
            return t;
        }

        public static void FillWorld(AirlineProfile p, bool previewOnly)
        {
            if (p == null) return;
            var rng = new System.Random(Hash(p.id));
            p.aircraft = BuildFleet(p, rng);
            p.transitionBusinesses = BuildTransitions(p, rng);
            p.routes = BuildRoutes(p, rng);
            RouteGeography.PruneImplausible(p.routes, AirlineCEO.UI.MapWidget.GreatCircleKm);
            p.openingLoans = BuildLoans(p);
            p.legacyIssues = BuildIssues(p);
            p.competitors = SampleCompetitors();
            AttachBoard(p);
            p.fleetCount = p.aircraft.Count;
            p.routeCount = p.routes.Count;
            p.avgFleetAge = AvgAge(p.aircraft);
            p.monthlyRevenue = SumRouteMonth(p.routes) + SumTransitionMonth(p.transitionBusinesses);
            p.monthlyCost = Mathf.Max(p.cash * 0.08f, p.fleetCount * 4.5f + p.routeCount * 2f);
            p.cashRunwayMonths = p.monthlyCost > 0.01f
                ? p.cash / Mathf.Max(1f, p.monthlyCost - Mathf.Max(0f, p.monthlyRevenue) * 0.35f)
                : 12f;
            p.crewVacancyPercent = p.difficulty <= 2 ? 5 : p.difficulty <= 3 ? 15 : p.difficulty <= 4 ? 18 : 22;
            _ = previewOnly;
        }

        // —— 过渡业务占机数量（干租 / 湿租 / 包机 / 培训）——
        // 单独成函数，是因为**机队规模必须据此补足**（见 BuildFleet）：
        // 过渡业务是从机队尾部抽飞机的，抽完剩下的才够配航线。
        static int DryLeaseCount(int difficulty) => difficulty <= 2 ? 1 : difficulty == 3 ? 2 : difficulty == 4 ? 3 : 4;
        static int WetLeaseCount(int difficulty) => difficulty >= 3 ? 1 : 0;
        static int CharterCount(int difficulty) => difficulty >= 3 ? 1 : 0;
        static int TrainingCount(int difficulty) => difficulty >= 4 ? 1 : 0;

        /// <summary>过渡业务总共锁掉的飞机数。</summary>
        static int TransitionAircraftCount(int difficulty)
            => DryLeaseCount(difficulty) + WetLeaseCount(difficulty)
             + CharterCount(difficulty) + TrainingCount(difficulty);

        static List<Aircraft> BuildFleet(AirlineProfile p, System.Random rng)
        {
            var list = new List<Aircraft>();
            // 机队与网络必须自洽：★3 及以上是国际枢纽（HKG/SIN/FRA/AMS/SFO/SYD…），
            // 机队里没有宽体就飞不了洲际目的地，BuildDestinationPool 会把它们全部滤掉，
            // 网络就开不满航线数。故 ★3 起配四分之一宽体，★4–5 配五分之二。
            string[] types = p.difficulty >= 4
                ? new[] { "B737-800", "A320neo", "A321", "B787-9", "A330-300" }
                : p.difficulty == 3
                    ? new[] { "A320neo", "B737-800", "A321", "A330-300" }
                    : new[] { "A320neo", "B737-800", "A320" };
            // 规模底线：航线数 + 过渡业务占机数。
            // 旧实现直接用 p.fleetCount，与过渡业务各算各的 —— 过渡业务先抽走尾部 1/4，
            // 剩下的飞机配不满航线，于是生成出「无机可配」的航线（旧版 ★3 每家 2 条，
            // 全量 72 条，且因目的地池按近→中→远生成，全部落在洲际线上）。
            int needed = Mathf.Max(1, p.routeCount) + TransitionAircraftCount(p.difficulty);
            int n = Mathf.Max(1, Mathf.Max(p.fleetCount, needed));
            for (int i = 0; i < n; i++)
            {
                string type = types[i % types.Length];
                var cat = AircraftCatalog.Find(type);
                int age = 4 + (i * 3 + rng.Next(0, 5)) % 22;
                if (p.difficulty >= 4 && i >= n - 3) age = 22 + rng.Next(0, 4);
                int seats = cat != null ? cat.maxPassengers : 165;
                list.Add(new Aircraft
                {
                    registration = $"B-{2000 + i}",
                    type = type,
                    source = i % 5 == 0 ? "经营租赁" : "自有",
                    age = age,
                    condition = Mathf.Clamp(100f - age * 2.2f, 38f, 98f),
                    isActive = true,
                    status = AircraftStatuses.Airworthy,
                    purchaseYear = 1,
                    purchasePrice = cat != null ? cat.priceWan * 0.55f : 4000f,
                    economySeats = seats,
                    cargoTons = cat != null ? cat.cargoTons : 0f,
                    monthlyLeaseWan = i % 5 == 0 ? 18f + p.difficulty * 2f : 0f,
                    remainingLifeYears = Mathf.Max(1, 25 - age)
                });
            }
            return list;
        }

        static List<TransitionContract> BuildTransitions(AirlineProfile p, System.Random rng)
        {
            var list = new List<TransitionContract>();
            int dry = DryLeaseCount(p.difficulty);
            int wet = WetLeaseCount(p.difficulty);
            int charter = CharterCount(p.difficulty);
            int train = TrainingCount(p.difficulty);
            p.dryLeaseCount = dry;
            p.wetLeaseCount = wet;
            p.charterCount = charter;
            p.trainingCount = train;
            int idx = p.aircraft.Count - 1;
            void Add(string kind, int days, float rate, string client)
            {
                if (idx < 0) return;
                var ac = p.aircraft[idx];
                idx--;
                list.Add(new TransitionContract
                {
                    id = $"tc_{p.id}_{kind}_{ac.registration}",
                    aircraftReg = ac.registration,
                    kind = kind,
                    client = client,
                    rateWanPerDay = rate,
                    totalDays = days,
                    daysUsed = rng.Next(5, Math.Max(6, days / 3)),
                    recallDaysLeft = -1
                });
            }
            for (int i = 0; i < dry; i++)
                Add(TransitionKinds.Dry, 90, 0.55f, "亚太运力租赁");
            if (wet > 0) Add(TransitionKinds.Wet, 90, 1.10f, "海岸湿租伙伴");
            if (charter > 0) Add(TransitionKinds.Charter, 5, 2.60f, "商务包机客户");
            if (train > 0) Add(TransitionKinds.Training, 7, 1.65f, "南方航校");
            return list;
        }

        /// <summary>
        /// 种子航线：目的地池按**枢纽自身**所处国/大洲生成（见 <see cref="BuildDestinationPool"/>），
        /// 班次与班期统一交由 <see cref="RouteFrequencyModel.Materialize"/> 给出。
        /// </summary>
        static List<RouteData> BuildRoutes(AirlineProfile p, System.Random rng)
        {
            var list = new List<RouteData>();
            var busy = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (p.transitionBusinesses != null)
            {
                for (int i = 0; i < p.transitionBusinesses.Count; i++)
                    if (p.transitionBusinesses[i] != null)
                        busy.Add(p.transitionBusinesses[i].aircraftReg);
            }

            var dests = BuildDestinationPool(p);
            int need = Mathf.Max(1, p.routeCount);
            int lose = p.difficulty <= 2 ? 0 : p.difficulty == 3 ? 2 : p.difficulty == 4 ? 6 : 12;
            int loseFrom = Math.Max(0, Math.Min(need, dests.Count) - lose);

            // 一、先只建「航线壳」（不含机号/机型）：机号要等全部航线就位后统一分配，
            // 否则先到先得的贪心会把宽体浪费在短程线上（见 AssignAircraft）。
            for (int i = 0; i < dests.Count && list.Count < need; i++)
            {
                string dest = dests[i];
                float km = AirlineCEO.UI.MapWidget.GreatCircleKm(p.hubAirport, dest);

                // 亏损航线取池尾的薄市场（远程小市场 / 支线）；「网络过大需止血」的叙事与之自洽。
                bool loss = lose > 0 && list.Count >= loseFrom;
                float daily = loss
                    ? -(0.4f + (list.Count - loseFrom) * 0.05f)
                    : (0.6f + (i % 5) * 0.15f);

                list.Add(new RouteData
                {
                    routeId = $"{p.hubAirport}-{dest}-{i + 1}",
                    origin = p.hubAirport,
                    destination = dest,
                    dailyProfit = daily,
                    baseDailyProfit = daily,
                    openedDate = RouteData.OpenedBeforeTakeover,
                    score = loss ? 42 : 70 + (i % 20),
                    rating = loss ? "D" : "B",
                    distanceKm = km,
                    // 机型由选机结果写入；选不到时留空，展示层如实呈报（不得谎报机型）
                    aircraftType = "",
                    registration = "",
                    // ★块首号（100 / 108 / 116 …）：每条航线预留 8 个号（4 波次 × 去程/回程），
                    // 否则回程号（基号 + 1）会撞上下一条航线的基号 —— 2026-10-13 第六次报障。
                    flightNumber = $"{p.iataCode}{FlightScheduleFormat.BaseNumberOf(i)}",
                    depTime = "08:00",
                    season = "夏秋",
                    schedulePending = false,
                    groundStaffAssigned = 2
                });
            }

            // 二、按航距从远到近选机（宽体优先给洲际线）
            AssignAircraft(p, list, busy);

            // 三、班次/班期统一交给 RouteFrequencyModel（取代硬编码 km>=5000 ? 4 : 7）：
            // 按 需求 × 枢纽规模 × 机型座级 × 航距衰减 推每周班次，并物化与班次自洽的波次列表。
            // 必须放在选机之后 —— 模型要用最终机型的座级与利用率上限。
            for (int i = 0; i < list.Count; i++)
            {
                RouteFrequencyModel.Materialize(list[i]);
                list[i].schedulePending = false;
            }
            return list;
        }

        /// <summary>
        /// 逐条配机：按 <b>航距从远到近</b> 分配，宽体优先给洲际线。
        ///
        /// 旧实现按目的地池顺序（近→中→远）配机，而 <see cref="PickAircraft"/> 的启发式
        /// 「满足航程的最小座级」只在**单次调用内**比较候选，于是先轮到短程线时它会随手拿走
        /// 当时能飞的最小座级 —— 实测 FRA 的 A330-300 被派去飞 1,423 km 的 FRA–MAD，
        /// 真正需要宽体的 FRA–ATL（7,409 km）/ FRA–ORD（6,971 km）反而无机可配。
        /// 按航距降序后，「最小的够用座级」才真正等同于「不浪费宽体」。
        /// </summary>
        static void AssignAircraft(AirlineProfile p, List<RouteData> routes, HashSet<string> busy)
        {
            if (p == null || routes == null || routes.Count == 0) return;

            var order = new List<int>(routes.Count);
            for (int i = 0; i < routes.Count; i++) order.Add(i);
            order.Sort((a, b) =>
            {
                int c = routes[b].distanceKm.CompareTo(routes[a].distanceKm);
                return c != 0 ? c : a.CompareTo(b);   // 同航距按池序，保证种子可复现
            });

            var assigned = new List<RouteData>(routes.Count);
            int acIndex = 0;
            for (int k = 0; k < order.Count; k++)
            {
                var route = routes[order[k]];
                var plane = PickAircraft(p, assigned, busy, route.distanceKm, ref acIndex);
                if (plane == null) continue;   // 机队不足：留待复航，不谎报机型
                route.registration = plane.registration;
                route.aircraftType = plane.type;
                assigned.Add(route);
            }
        }

        /// <summary>
        /// 目的地池（枢纽本地化）：按**航距**分带取候选——近程（≤<see cref="EconomyBalanceParams.FreqBandNearKm"/>）
        /// → 中程（≤<see cref="EconomyBalanceParams.FreqBandMidKm"/>）→ 远程，带内按模型自身需求（含距离衰减）降序。
        ///
        /// 取代历史上写死的「国内 24 场 → 亚洲 9 场 → 远程 8 场」清单：那套清单以中国为中心，
        /// 于是伦敦/迪拜/洛杉矶等海外基地的**整条**网络都被判为洲际远程，班次清一色 2–5。
        ///
        /// 注意这里刻意用**航距**而不是「洲」：同洲未必短程——首尔→迪拜 6,700 km、上海→新加坡
        /// 4,450 km 都是洲际量级；按「洲」分带会把这些远程航线塞进高频带，反而压低中位班次。
        ///
        /// 同时以**机队航程上限**过滤：A320neo（6,300 km）飞不了 PEK–JFK，这类目的地不会入池，
        /// 因此生成航线的机型必定飞得到（窄体机队的小航司自然只做短程网络）。
        /// </summary>
        static List<string> BuildDestinationPool(AirlineProfile p)
        {
            string hub = p.hubAirport;
            float maxRange = FleetMaxRangeKm(p);

            var near = new List<DestinationCandidate>();
            var mid = new List<DestinationCandidate>();
            var far = new List<DestinationCandidate>();

            var catalog = CityEconomyCatalog.All;
            for (int i = 0; i < catalog.Length; i++)
            {
                var e = catalog[i];
                if (e == null || string.IsNullOrEmpty(e.iata)) continue;
                if (string.Equals(e.iata, hub, StringComparison.OrdinalIgnoreCase)) continue;
                float km = AirlineCEO.UI.MapWidget.GreatCircleKm(hub, e.iata);
                if (maxRange > 0f && km > maxRange * 1.05f) continue;   // 机队飞不到
                if (!RouteGeography.IsPlausiblePassengerOd(hub, e.iata, km)) continue;

                var c = new DestinationCandidate
                {
                    iata = e.iata,
                    km = km,
                    demand = RouteFrequencyModel.DemandPaxPerDay(hub, e.iata, km)
                };
                if (km <= EconomyBalanceParams.FreqBandNearKm) near.Add(c);
                else if (km <= EconomyBalanceParams.FreqBandMidKm) mid.Add(c);
                else far.Add(c);
            }
            SortByDemand(near);
            SortByDemand(mid);
            SortByDemand(far);

            int need = Mathf.Max(1, p.routeCount);
            // 远程占比随规模上升：★≤2 本就是短程网络，★5 才配三成以上的洲际长航线。
            float longShare = p.difficulty <= 2 ? 0f
                : p.difficulty == 3 ? 0.20f
                : p.difficulty == 4 ? 0.30f
                : 0.35f;
            int longTarget = (int)Math.Round(need * longShare, MidpointRounding.AwayFromZero);
            int shortTarget = need - longTarget;

            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Take(List<DestinationCandidate> pool, int count)
            {
                for (int i = 0; i < pool.Count && count > 0; i++)
                {
                    if (!seen.Add(pool[i].iata)) continue;
                    result.Add(pool[i].iata);
                    count--;
                }
            }

            // 远程带的硬约束：**条数不得超过能飞该航距的可用飞机数**。
            // 旧实现第 4 步「兜底」只按 routeCount 顶数，机队里的宽体没跟着涨，
            // 于是兜底配到窄体 —— 实测 ATL/DFW/DEN 出现 10 条 A320neo 飞 6,761–7,944 km 的洲际线。
            // 这里的「可用」= 未被过渡业务占用（干租/湿租/包机/培训），与 AssignAircraft 的 busy 同源；
            // 判据用「航程 ≥ 航距」这条物理线（与 --fleetcheck 的硬断言同一把尺），
            // 5% 运行余量仍由 PickAircraft 的启发式处理，此处不重复收紧。
            var longFleet = AvailableAircraftByRange(p);
            void TakeLong(int count)
            {
                for (int i = 0; i < far.Count && count > 0; i++)
                {
                    var cand = far[i];
                    if (seen.Contains(cand.iata)) continue;
                    int pick = -1;
                    for (int k = longFleet.Count - 1; k >= 0; k--)   // 航程降序 → 从尾部取「够用的最小航程」
                        if (longFleet[k] != null && RangeKmOf(longFleet[k]) >= cand.km) { pick = k; break; }
                    if (pick < 0) continue;                          // 机队里没有能飞它的飞机 → 该目的地不入池
                    longFleet.RemoveAt(pick);
                    seen.Add(cand.iata);
                    result.Add(cand.iata);
                    count--;
                }
            }

            Take(near, shortTarget);                     // 1) 短程（高频核心网）
            Take(mid, shortTarget - result.Count);        // 2) 中程
            TakeLong(longTarget);                        // 3) 远程（低频，受可用机队航程约束）
            TakeLong(need - result.Count);               // 4) 兜底：网络规模优先，再摊到中/近程
            Take(mid, need - result.Count);
            Take(near, need - result.Count);
            return result;
        }

        /// <summary>可用（未被过渡业务占用）飞机按航程降序 —— 供「远程带条数 ≤ 能飞远程的飞机数」使用。</summary>
        static List<Aircraft> AvailableAircraftByRange(AirlineProfile p)
        {
            var busy = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (p.transitionBusinesses != null)
                for (int i = 0; i < p.transitionBusinesses.Count; i++)
                    if (p.transitionBusinesses[i] != null)
                        busy.Add(p.transitionBusinesses[i].aircraftReg);

            var list = new List<Aircraft>();
            for (int i = 0; i < p.aircraft.Count; i++)
            {
                var a = p.aircraft[i];
                if (a == null || busy.Contains(a.registration)) continue;
                list.Add(a);
            }
            list.Sort((x, y) => RangeKmOf(y).CompareTo(RangeKmOf(x)));
            return list;
        }

        /// <summary>机型航程（km）；目录缺失返回 0（表示飞不了任何远程线）。</summary>
        static float RangeKmOf(Aircraft a)
        {
            if (a == null) return 0f;
            var c = AircraftCatalog.Find(a.type);
            return c != null ? c.maxRangeKm : 0f;
        }

        /// <summary>目的地候选（带内排序用）。</summary>
        struct DestinationCandidate
        {
            public string iata;
            public float km;
            /// <summary>模型自身口径的单向日客流，含距离衰减。</summary>
            public float demand;
        }

        /// <summary>按模型需求降序（同分按 IATA 稳定排序），使高频带优先取真正的大市场。</summary>
        static void SortByDemand(List<DestinationCandidate> pool)
        {
            pool.Sort((a, b) =>
            {
                float d = b.demand - a.demand;
                if (d > 0f) return 1;
                if (d < 0f) return -1;
                return string.CompareOrdinal(a.iata, b.iata);
            });
        }

        /// <summary>机队航程上限（km）。目录缺失返回 0，表示不设限。</summary>
        static float FleetMaxRangeKm(AirlineProfile p)
        {
            float best = 0f;
            if (p.aircraft == null) return best;
            for (int i = 0; i < p.aircraft.Count; i++)
            {
                var a = p.aircraft[i];
                if (a == null) continue;
                var c = AircraftCatalog.Find(a.type);
                if (c != null && c.maxRangeKm > best) best = c.maxRangeKm;
            }
            return best;
        }

        /// <summary>
        /// 选机：航距必须落在机型航程内（留 5% 余量）；在满足航程的候选里取「够用的最小座级」，
        /// 于是短程不排宽体、远程不排窄体；同座级按机队轮转顺序，保持机队自然轮换。
        /// 全机队都飞不到时退回航程最大的一架（并已由目的地池尽量避免此情形）。
        /// </summary>
        static Aircraft PickAircraft(AirlineProfile p, List<RouteData> used, HashSet<string> busy,
            float km, ref int startIndex)
        {
            if (p.aircraft == null || p.aircraft.Count == 0) return null;

            int n = p.aircraft.Count;
            Aircraft fit = null, any = null;
            float fitSeats = 0f, anyRange = -1f;
            int fitIdx = 0, anyIdx = 0;

            for (int k = 0; k < n; k++)
            {
                int j = (startIndex + k) % n;
                var a = p.aircraft[j];
                if (a == null || busy.Contains(a.registration)) continue;
                if (AircraftOnAnotherRoute(used, a.registration)) continue;

                var c = AircraftCatalog.Find(a.type);
                float range = c != null ? c.maxRangeKm : 0f;
                float seats = c != null ? c.maxPassengers : a.economySeats;

                if (range > anyRange) { anyRange = range; any = a; anyIdx = j; }
                if (range >= km * 1.05f && (fit == null || seats < fitSeats))
                {
                    fit = a;
                    fitSeats = seats;
                    fitIdx = j;
                }
            }

            if (fit != null) { startIndex = fitIdx + 1; return fit; }
            if (any != null) { startIndex = anyIdx + 1; return any; }
            return null;
        }

        static bool AircraftOnAnotherRoute(List<RouteData> routes, string reg)
        {
            if (string.IsNullOrEmpty(reg)) return false;
            for (int i = 0; i < routes.Count; i++)
                if (routes[i] != null && routes[i].IsFlownBy(reg)) return true;
            return false;
        }

        static List<Loan> BuildLoans(AirlineProfile p)
        {
            var list = new List<Loan>();
            if (p.debtRatio < 0.2f) return list;
            float principal = Mathf.Max(200f, p.cash * p.debtRatio * 2.2f);
            list.Add(new Loan
            {
                id = "L-TAKEOVER",
                type = "bank",
                principal = principal,
                remainingPrincipal = principal * 0.82f,
                interestRate = (6.5f + p.difficulty) / 100f,
                termMonths = 48,
                monthsRemaining = 36,
                monthlyPayment = principal * 0.028f,
                createdAt = "接管前"
            });
            return list;
        }

        static List<LegacyIssue> BuildIssues(AirlineProfile p)
        {
            var list = new List<LegacyIssue>();
            void Add(string id, string title, string desc, string sys, int urg, string link)
            {
                list.Add(new LegacyIssue
                {
                    id = id,
                    title = title,
                    description = desc,
                    system = sys,
                    urgency = urg,
                    actionLink = link
                });
            }

            int lose = 0;
            if (p.routes != null)
                for (int i = 0; i < p.routes.Count; i++)
                    if (p.routes[i] != null && p.routes[i].dailyProfit < 0f) lose++;
            if (lose > 0)
                Add("iss_loss_routes", $"{lose} 条航线持续亏损", "评估停飞、改班或重组票价。", "Route", 3, "n12_航线网络");

            int retiring = 0;
            if (p.aircraft != null)
                for (int i = 0; i < p.aircraft.Count; i++)
                    if (p.aircraft[i] != null && p.aircraft[i].remainingLifeYears <= 4) retiring++;
            if (retiring > 0)
                Add("iss_retire", $"{retiring} 架飞机即将退役", "决定购买、租赁或退出运力。", "Fleet", 2, "n9_机队");

            if (p.crewVacancyPercent >= 10)
                Add("iss_crew", $"机组人员短缺 {p.crewVacancyPercent}%", "启动招聘或调整排班。", "HR", 3, "n86_组织规划与招聘");

            if (p.difficulty >= 3)
                Add("iss_union", "工会合同即将到期", "准备谈判策略，避免停工风险。", "HR", 2, "n13_运营部");

            if (p.cashRunwayMonths < 6f)
                Add("iss_cash", $"现金跑道约 {p.cashRunwayMonths:F1} 个月", "削减成本或等待财务总监融资请示。", "Finance", 3, "n15_财务部");

            if (p.difficulty >= 3)
                Add("iss_rival", "竞争对手抢占核心航线", "对飞压力已在竞争面板可见。", "Competition", 2, "n12_航线网络");

            Add("iss_board", "董事会已下达年度目标", "优先完成高权重考核。", "Board", 2, "n20_董事会");

            if (p.transitionBusinesses != null && p.transitionBusinesses.Count > 0)
                Add("iss_transition", "部分飞机正在干租/包机/湿租", "决定召回、续约或维持。", "Fleet", 1, "n9_机队");

            list.Sort((a, b) => b.urgency.CompareTo(a.urgency));
            return list;
        }

        static List<AIAirline> SampleCompetitors()
        {
            return new List<AIAirline>
            {
                new AIAirline { id = "AI-01", name = "云翼航空", type = AIAirlineTypes.FullService, state = AIAirlineStates.Expanding, fleetSize = 12, cash = 2800f, isAlive = true },
                new AIAirline { id = "AI-02", name = "青鸾航空", type = AIAirlineTypes.LowCost, state = AIAirlineStates.Stable, fleetSize = 18, cash = 1600f, isAlive = true },
                new AIAirline { id = "AI-03", name = "银燕航空", type = AIAirlineTypes.Regional, state = AIAirlineStates.Defensive, fleetSize = 10, cash = 900f, isAlive = true }
            };
        }

        static void AttachBoard(AirlineProfile p)
        {
            p.boardMemberIds = new List<string> { "A", "B", "C", "D", "E", "F", "G", "H" };
            p.boardMemberNames = new List<string>();
            var db = DataBase.Instance;
            if (db?.BoardMembers != null)
            {
                for (int i = 0; i < p.boardMemberIds.Count; i++)
                {
                    string id = p.boardMemberIds[i];
                    for (int j = 0; j < db.BoardMembers.Count; j++)
                    {
                        var m = db.BoardMembers[j];
                        if (m != null && m.id == id)
                        {
                            p.boardMemberNames.Add(m.fullName);
                            break;
                        }
                    }
                }
            }
        }

        static float AvgAge(List<Aircraft> fleet)
        {
            if (fleet == null || fleet.Count == 0) return 0f;
            float s = 0f;
            for (int i = 0; i < fleet.Count; i++)
                if (fleet[i] != null) s += fleet[i].age;
            return s / fleet.Count;
        }

        static float SumRouteMonth(List<RouteData> routes)
        {
            float s = 0f;
            if (routes == null) return s;
            for (int i = 0; i < routes.Count; i++)
                if (routes[i] != null) s += routes[i].dailyProfit * 30f;
            return s;
        }

        static float SumTransitionMonth(List<TransitionContract> list)
        {
            float s = 0f;
            if (list == null) return s;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null) s += list[i].rateWanPerDay * 30f;
            return s;
        }

        static int Hash(string id)
        {
            int h = 17;
            if (string.IsNullOrEmpty(id)) return h;
            for (int i = 0; i < id.Length; i++) h = h * 31 + id[i];
            return h;
        }

        static Aircraft CloneAc(Aircraft a)
        {
            if (a == null) return null;
            return new Aircraft
            {
                registration = a.registration,
                type = a.type,
                source = a.source,
                age = a.age,
                condition = a.condition,
                isActive = a.isActive,
                status = a.status,
                purchaseYear = a.purchaseYear,
                purchasePrice = a.purchasePrice,
                economySeats = a.economySeats,
                cargoTons = a.cargoTons,
                monthlyLeaseWan = a.monthlyLeaseWan,
                remainingLifeYears = a.remainingLifeYears
            };
        }

        static RouteData CloneRoute(RouteData r)
        {
            if (r == null) return null;
            var n = new RouteData
            {
                routeId = r.routeId,
                origin = r.origin,
                destination = r.destination,
                dailyProfit = r.dailyProfit,
                baseDailyProfit = r.baseDailyProfit,
                openedDate = r.openedDate,
                score = r.score,
                rating = r.rating,
                distanceKm = r.distanceKm,
                aircraftType = r.aircraftType,
                registration = r.registration,
                flightNumber = r.flightNumber,
                weeklyFlights = r.weeklyFlights,
                daysOfWeek = r.daysOfWeek,
                depTime = r.depTime,
                arrTime = r.arrTime,
                season = r.season,
                schedulePending = false,
                groundStaffAssigned = r.groundStaffAssigned
            };
            n.SyncFlightsFromPrimary();
            n.schedulePending = false;
            return n;
        }

        static TransitionContract CloneTc(TransitionContract c)
        {
            if (c == null) return null;
            return new TransitionContract
            {
                id = c.id,
                aircraftReg = c.aircraftReg,
                kind = c.kind,
                client = c.client,
                rateWanPerDay = c.rateWanPerDay,
                totalDays = c.totalDays,
                daysUsed = c.daysUsed,
                startUtcTicks = c.startUtcTicks,
                recallDaysLeft = -1
            };
        }

        static Loan CloneLoan(Loan L)
        {
            if (L == null) return null;
            return new Loan
            {
                id = L.id,
                type = L.type,
                principal = L.principal,
                remainingPrincipal = L.remainingPrincipal,
                interestRate = L.interestRate,
                termMonths = L.termMonths,
                monthsRemaining = L.monthsRemaining,
                monthlyPayment = L.monthlyPayment,
                createdAt = L.createdAt
            };
        }

        public static string Stars(int n)
        {
            n = Mathf.Clamp(n, 1, 5);
            var sb = new StringBuilder();
            for (int i = 0; i < n; i++) sb.Append('★');
            return sb.ToString();
        }
    }
}

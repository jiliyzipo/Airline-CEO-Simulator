using System.Collections.Generic;
using AirlineCEO.Data;

namespace AirlineCEO.Systems
{
    /// <summary>内置事件池（≥20，可扩至 50+）。</summary>
    public static class RandomEventCatalog
    {
        private static List<RandomEventDef> _cache;

        public static IReadOnlyList<RandomEventDef> All
        {
            get
            {
                if (_cache == null) _cache = Build();
                return _cache;
            }
        }

        public static RandomEventDef Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var all = All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].id == id) return all[i];
            return null;
        }

        private static EventEffect E(string target, float value, bool pct = false) =>
            new EventEffect { target = target, value = value, isPercent = pct };

        private static RandomEventDef Make(
            string id, string title, string desc, string cat, float weight,
            EventEffect[] effects, string chain = null, int chainDelay = 1,
            bool oneShot = false, bool oil = false)
        {
            return new RandomEventDef
            {
                id = id,
                title = title,
                description = desc,
                category = cat,
                weight = weight,
                effects = effects ?? System.Array.Empty<EventEffect>(),
                chainEventId = chain,
                chainDelayQuarters = chainDelay,
                isOneShot = oneShot,
                consumesOilSlot = oil
            };
        }

        private static List<RandomEventDef> Build()
        {
            var list = new List<RandomEventDef>(32);

            // —— 自然灾害（连锁示范：台风 → 跑道积压 → 保险 → 机组疲劳 → 公关）——
            list.Add(Make("EVT_TYPHOON_01", "超强台风登陆沿海",
                "多条航线大面积延误，临时改航与旅客安置费用上升。",
                RandomEventCategories.Natural, 0.85f,
                new[] { E("cash", -45f), E("onTime", -6f) },
                "EVT_RUNWAY_BACKLOG", 1));

            list.Add(Make("EVT_RUNWAY_BACKLOG", "枢纽跑道积压",
                "天气过后起降时刻仍然紧张，准点继续承压。",
                RandomEventCategories.Natural, 0.01f, // 主要由连锁触发
                new[] { E("onTime", -4f), E("loadFactor", -2f), E("cash", -15f) },
                "EVT_INSURANCE_SPIKE", 1));

            list.Add(Make("EVT_INSURANCE_SPIKE", "航空保险费率上调",
                "承保方上调风险溢价，本季现金被额外抽走一笔。",
                RandomEventCategories.Market, 0.01f,
                new[] { E("cash", -35f), E("monthlyBurn", 8f) },
                "EVT_CREW_FATIGUE", 1));

            list.Add(Make("EVT_CREW_FATIGUE", "机组疲劳与排班紧张",
                "连续加班引发不满，服务与准点都受影响。",
                RandomEventCategories.Internal, 0.01f,
                new[] { E("satisfaction", -6f), E("onTime", -3f), E("brand", -3f) },
                "EVT_PR_STORM", 1));

            list.Add(Make("EVT_PR_STORM", "旅客投诉上热搜",
                "延误与服务争议发酵，品牌形象短时受损。",
                RandomEventCategories.Internal, 0.01f,
                new[] { E("brand", -8f), E("satisfaction", -4f), E("loadFactor", -3f) }));

            list.Add(Make("EVT_BLIZZARD_01", "北方暴雪封航",
                "多日积雪，除冰与备降费用抬升。",
                RandomEventCategories.Natural, 0.75f,
                new[] { E("cash", -40f), E("onTime", -5f), E("loadFactor", -2f) }));

            list.Add(Make("EVT_VOLCANO_01", "火山灰预警改航",
                "高空火山灰迫使绕飞，油耗与时刻成本上升。",
                RandomEventCategories.Natural, 0.55f,
                new[] { E("cash", -55f), E("fuelPrice", 8f, true) },
                oil: true));

            list.Add(Make("EVT_FLOOD_01", "机场周边洪涝",
                "地面交通受阻，旅客到达率下降。",
                RandomEventCategories.Natural, 0.6f,
                new[] { E("cash", -30f), E("loadFactor", -4f) }));

            // —— 市场冲击 ——
            list.Add(Make("EVT_FUEL_SPIKE", "航油期货突然拉升",
                "地缘与库存消息推高现货报价。",
                RandomEventCategories.Market, 1.0f,
                new[] { E("fuelPrice", 18f, true), E("cash", -20f) },
                oil: true));

            list.Add(Make("EVT_FUEL_DIP", "航油价格意外回落",
                "供应宽松，现货报价回落，成本压力减轻。",
                RandomEventCategories.Market, 0.9f,
                new[] { E("fuelPrice", -12f, true), E("cash", 25f) },
                oil: true));

            list.Add(Make("EVT_DEMAND_SURGE", "暑运/节庆需求爆发",
                "热门航线客座走高，本季现金回笼不错。",
                RandomEventCategories.Market, 1.0f,
                new[] { E("loadFactor", 6f), E("cash", 50f), E("brand", 2f) }));

            list.Add(Make("EVT_DEMAND_SLUMP", "商务出行短时萎缩",
                "企业差旅预算收紧，客座与收入承压。",
                RandomEventCategories.Market, 0.95f,
                new[] { E("loadFactor", -7f), E("cash", -40f) }));

            list.Add(Make("EVT_CURRENCY_SWING", "汇率剧烈波动",
                "跨境结算汇兑损失，账面现金缩水。",
                RandomEventCategories.Market, 0.7f,
                new[] { E("cash", -35f), E("satisfaction", -3f) }));

            list.Add(Make("EVT_TOURISM_BOOM", "入境游政策放宽",
                "国际线问询大增，品牌与客座双升。",
                RandomEventCategories.Market, 0.8f,
                new[] { E("loadFactor", 4f), E("brand", 5f), E("cash", 30f) },
                oneShot: true));

            // —— 内部危机 ——
            list.Add(Make("EVT_LABOR_TALK", "一线薪酬谈判升温",
                "工会施压加薪，董事会满意度下滑，现金被挤占。",
                RandomEventCategories.Internal, 0.9f,
                new[] { E("satisfaction", -10f), E("cash", -25f), E("brand", -2f) }));

            list.Add(Make("EVT_PR_SCANDAL", "服务质量舆论风波",
                "短视频曝光服务争议，品牌短期承压。",
                RandomEventCategories.Internal, 0.85f,
                new[] { E("brand", -7f), E("satisfaction", -5f), E("loadFactor", -2f) }));

            list.Add(Make("EVT_IT_OUTAGE", "订座系统故障",
                "销售与值机短时中断，退改签与赔偿上升。",
                RandomEventCategories.Internal, 0.8f,
                new[] { E("cash", -28f), E("onTime", -4f), E("brand", -4f) }));

            list.Add(Make("EVT_WHISTLEBLOW", "内部举报调查",
                "合规调查占用管理层精力，董事会略感不安。",
                RandomEventCategories.Internal, 0.65f,
                new[] { E("satisfaction", -8f), E("cash", -15f) }));

            list.Add(Make("EVT_TRAINING_WIN", "机务培训成果获赞",
                "安全文化宣传加分，指数与品牌小幅提升。",
                RandomEventCategories.Internal, 0.7f,
                new[] { E("safetyIndex", 3f), E("brand", 3f), E("satisfaction", 2f) }));

            // —— 政策变化 ——
            list.Add(Make("EVT_SLOT_EASE", "枢纽时刻协调放宽",
                "部分黄金时刻协调放宽，运营更顺一点。",
                RandomEventCategories.Policy, 0.7f,
                new[] { E("onTime", 3f), E("loadFactor", 2f), E("cash", 15f) }));

            list.Add(Make("EVT_SLOT_TIGHT", "时刻资源收紧",
                "监管收紧黄金时刻分配，准点与客座承压。",
                RandomEventCategories.Policy, 0.75f,
                new[] { E("onTime", -3f), E("loadFactor", -3f), E("cash", -18f) }));

            list.Add(Make("EVT_CARBON_TAX", "碳税试征通知",
                "本季先按示意额度计提一笔环保成本。",
                RandomEventCategories.Policy, 0.7f,
                new[] { E("cash", -40f), E("monthlyBurn", 5f), E("satisfaction", -2f) }));

            list.Add(Make("EVT_SUBSIDY", "支线航空补贴到位",
                "地方补贴到账，现金与董事会情绪双升。",
                RandomEventCategories.Policy, 0.65f,
                new[] { E("cash", 60f), E("satisfaction", 5f) },
                oneShot: true));

            list.Add(Make("EVT_VISA_POLICY", "签证便利化落地",
                "跨境客流预期改善，品牌与客座受益。",
                RandomEventCategories.Policy, 0.6f,
                new[] { E("brand", 4f), E("loadFactor", 3f) }));

            // —— 供应链 ——
            list.Add(Make("EVT_DELIVERY_DELAY", "新机交付延期",
                "制造商排产延误，运力扩张放缓，声誉略损。",
                RandomEventCategories.Supply, 0.7f,
                new[] { E("brand", -3f), E("satisfaction", -4f), E("cash", -10f) }));

            list.Add(Make("EVT_PARTS_SHORT", "航材供应紧张",
                "关键件周转变慢，维护成本抬升。",
                RandomEventCategories.Supply, 0.75f,
                new[] { E("monthlyBurn", 6f), E("cash", -22f), E("onTime", -2f) }));

            list.Add(Make("EVT_MRO_SLOT", "大修机位难求",
                "第三方机库档期紧张，飞机利用率受影响。",
                RandomEventCategories.Supply, 0.55f,
                new[] { E("loadFactor", -3f), E("cash", -18f) }));

            list.Add(Make("EVT_CATERING_WIN", "航食供应商降价续约",
                "重新招标压低餐食成本，本季现金回血。",
                RandomEventCategories.Supply, 0.6f,
                new[] { E("cash", 20f), E("monthlyBurn", -3f) }));

            list.Add(Make("EVT_CARGO_BOOM", "电商旺季货运溢价",
                "腹舱与货班需求旺盛，意外增收。",
                RandomEventCategories.Market, 0.7f,
                new[] { E("cash", 45f), E("brand", 2f) }));

            list.Add(Make("EVT_PANDEMIC_SCARE", "区域疫情防控升级",
                "部分航线客流骤降（示意），现金流与客座承压。",
                RandomEventCategories.Natural, 0.5f,
                new[] { E("loadFactor", -8f), E("cash", -50f), E("brand", -3f) },
                oneShot: true));

            AppendExpandedPool(list);

            return list;
        }

        /// <summary>P1 内容池扩充：补齐 120+ 事件（覆盖六分类，含新增「人为事故」）。</summary>
        private static void AppendExpandedPool(List<RandomEventDef> list)
        {
            // —— 自然灾害（续）——
            list.Add(Make("EVT_NAT_QUAKE", "内陆地震波及航路",
                "航路与备降场短时紧张，改航成本上升。",
                RandomEventCategories.Natural, 0.5f,
                new[] { E("cash", -38f), E("onTime", -5f) }));
            list.Add(Make("EVT_NAT_HEATWAVE", "极端高温限载",
                "跑道性能与载重受限，客座与收益承压。",
                RandomEventCategories.Natural, 0.55f,
                new[] { E("loadFactor", -4f), E("cash", -22f), E("onTime", -3f) }));
            list.Add(Make("EVT_NAT_FOG", "大雾封航",
                "低能见度导致大面积取消与备降。",
                RandomEventCategories.Natural, 0.6f,
                new[] { E("onTime", -7f), E("cash", -32f), E("brand", -2f) }));
            list.Add(Make("EVT_NAT_SANDSTORM", "沙尘暴影响起降",
                "中东枢纽视程骤降，发动机风险上升。",
                RandomEventCategories.Natural, 0.5f,
                new[] { E("onTime", -5f), E("monthlyBurn", 4f), E("cash", -20f) }));
            list.Add(Make("EVT_NAT_MONSOON", "季风暴雨季",
                "东南亚航线持续受扰，排班频繁调整。",
                RandomEventCategories.Natural, 0.6f,
                new[] { E("onTime", -4f), E("loadFactor", -3f), E("cash", -25f) }));
            list.Add(Make("EVT_NAT_LIGHTNING", "雷暴停场",
                "强对流天气导致地勤停摆，延误攀升。",
                RandomEventCategories.Natural, 0.55f,
                new[] { E("onTime", -6f), E("cash", -18f) }));
            list.Add(Make("EVT_NAT_COLD_SNAP", "寒潮侵袭除冰高峰",
                "除冰作业超负荷，燃油与除冰液成本上升。",
                RandomEventCategories.Natural, 0.55f,
                new[] { E("cash", -28f), E("onTime", -4f) }));
            list.Add(Make("EVT_NAT_WILDFIRE", "山火烟雾扩散",
                "能见度与空气质量下降，航班绕飞增多。",
                RandomEventCategories.Natural, 0.45f,
                new[] { E("onTime", -3f), E("loadFactor", -3f), E("cash", -24f) }));
            list.Add(Make("EVT_NAT_HAIL", "冰雹损毁停场",
                "部分飞机蒙皮受损，临时停场检修。",
                RandomEventCategories.Natural, 0.4f,
                new[] { E("cash", -45f), E("monthlyBurn", 5f) }));
            list.Add(Make("EVT_NAT_HURRICANE", "大西洋飓风登陆",
                "加勒比与美东航线大范围取消。",
                RandomEventCategories.Natural, 0.45f,
                new[] { E("cash", -55f), E("onTime", -6f), E("brand", -3f) }));
            list.Add(Make("EVT_NAT_TORNADO", "龙卷风短时过境",
                "枢纽机场短时关闭，后续积压明显。",
                RandomEventCategories.Natural, 0.35f,
                new[] { E("cash", -40f), E("onTime", -5f) }));
            list.Add(Make("EVT_NAT_SEA_FOG", "海雾影响沿海机场",
                "沿海枢纽早班延误，时刻资源错配。",
                RandomEventCategories.Natural, 0.5f,
                new[] { E("onTime", -4f), E("cash", -16f) }));

            // —— 人为事故（新增分类）——
            list.Add(Make("EVT_HUM_RUNWAY_INCURSION", "跑道入侵事件",
                "地面车辆误入跑道，安全审查升级。",
                RandomEventCategories.Human, 0.55f,
                new[] { E("safetyIndex", -6f), E("onTime", -3f), E("brand", -3f) }));
            list.Add(Make("EVT_HUM_GROUND_COLLISION", "地勤剐蹭事故",
                "拖车与飞机轻微剐蹭，停场检修。",
                RandomEventCategories.Human, 0.6f,
                new[] { E("cash", -30f), E("safetyIndex", -4f), E("onTime", -2f) }));
            list.Add(Make("EVT_HUM_BIRD_STRIKE", "鸟击致发动机受损",
                "起飞阶段鸟击，紧急返航检查。",
                RandomEventCategories.Human, 0.55f,
                new[] { E("cash", -42f), E("safetyIndex", -5f), E("onTime", -4f) }));
            list.Add(Make("EVT_HUM_ENGINE_FIRE", "发动机过热警告",
                "地面临检发现隐患，相关机型停场。",
                RandomEventCategories.Human, 0.4f,
                new[] { E("cash", -50f), E("monthlyBurn", 6f), E("safetyIndex", -3f) }));
            list.Add(Make("EVT_HUM_BAGGAGE", "行李系统故障",
                "行李分拣中断，旅客投诉上升。",
                RandomEventCategories.Human, 0.6f,
                new[] { E("brand", -4f), E("satisfaction", -4f), E("cash", -15f) }));
            list.Add(Make("EVT_HUM_CREW_ERROR", "机组误操作报备",
                "一起未遂差错被内部通报，安全复盘。",
                RandomEventCategories.Human, 0.5f,
                new[] { E("safetyIndex", -4f), E("satisfaction", -3f) }));
            list.Add(Make("EVT_HUM_MX_OVERSIGHT", "维护记录瑕疵",
                "适航检查发现记录不全，整改成本。",
                RandomEventCategories.Human, 0.5f,
                new[] { E("cash", -25f), E("safetyIndex", -5f), E("satisfaction", -3f) }));
            list.Add(Make("EVT_HUM_FUEL_SPILL", "加油溢漏事件",
                "加油作业溢漏，环保处置与停场。",
                RandomEventCategories.Human, 0.45f,
                new[] { E("cash", -20f), E("brand", -3f), E("safetyIndex", -2f) }));
            list.Add(Make("EVT_HUM_HARD_LANDING", "重着陆例行检查",
                "一起重着陆触发结构性检查。",
                RandomEventCategories.Human, 0.45f,
                new[] { E("cash", -28f), E("safetyIndex", -3f) }));
            list.Add(Make("EVT_HUM_NEAR_MISS", "空中接近事件",
                "空管通报一次近距事件，安全整改。",
                RandomEventCategories.Human, 0.35f,
                new[] { E("safetyIndex", -8f), E("satisfaction", -4f), E("onTime", -2f) }));
            list.Add(Make("EVT_HUM_JETBRIDGE", "廊桥故障",
                "廊桥停摆影响登机效率，准点承压。",
                RandomEventCategories.Human, 0.55f,
                new[] { E("onTime", -3f), E("cash", -12f) }));
            list.Add(Make("EVT_HUM_FOD", "跑道异物FOD",
                "跑道异物导致临时关闭检查。",
                RandomEventCategories.Human, 0.5f,
                new[] { E("onTime", -4f), E("cash", -18f), E("safetyIndex", -3f) }));

            // —— 市场冲击（续）——
            list.Add(Make("EVT_MKT_DUMPING", "竞对航线倾销",
                "竞争对手低价倾销，客源被分流。",
                RandomEventCategories.Market, 0.7f,
                new[] { E("loadFactor", -6f), E("cash", -30f) }));
            list.Add(Make("EVT_MKT_RATE_HIKE", "利率中枢上行",
                "融资成本抬升，月度开销增加。",
                RandomEventCategories.Market, 0.6f,
                new[] { E("monthlyBurn", 5f), E("satisfaction", -3f) }));
            list.Add(Make("EVT_MKT_COMPETITOR_BANKRUPT", "竞对破产出清",
                "主要对手停航，市场份额意外流入。",
                RandomEventCategories.Market, 0.5f,
                new[] { E("loadFactor", 5f), E("cash", 40f), E("brand", 2f) },
                oneShot: true));
            list.Add(Make("EVT_MKT_FUEL_SHORTAGE", "区域燃油供应紧张",
                "某枢纽燃油溢价，成本上升。",
                RandomEventCategories.Market, 0.55f,
                new[] { E("fuelPrice", 10f, true), E("cash", -18f) },
                oil: true));
            list.Add(Make("EVT_MKT_FX_LOSS", "汇兑损失确认",
                "跨境结算产生一次性汇兑损失。",
                RandomEventCategories.Market, 0.6f,
                new[] { E("cash", -32f) }));
            list.Add(Make("EVT_MKT_RECESSION", "衰退信号显现",
                "商务出行放缓，客座与信心双降。",
                RandomEventCategories.Market, 0.55f,
                new[] { E("loadFactor", -5f), E("satisfaction", -4f), E("cash", -25f) }));
            list.Add(Make("EVT_MKT_PRICE_WAR", "区域价格战升温",
                "同业争抢客源，票价承压。",
                RandomEventCategories.Market, 0.65f,
                new[] { E("cash", -28f), E("loadFactor", 2f) }));
            list.Add(Make("EVT_MKT_TOURISM_SLUMP", "旅游需求降温",
                "休闲客流回落，客座率下滑。",
                RandomEventCategories.Market, 0.6f,
                new[] { E("loadFactor", -4f), E("cash", -20f) }));
            list.Add(Make("EVT_MKT_STIMULUS", "消费刺激政策",
                "内需回暖，客座与现金双升。",
                RandomEventCategories.Market, 0.6f,
                new[] { E("loadFactor", 3f), E("cash", 35f), E("brand", 2f) }));
            list.Add(Make("EVT_MKT_FREIGHT_RATE", "货运运价上行",
                "腹舱与货班收益改善。",
                RandomEventCategories.Market, 0.6f,
                new[] { E("cash", 30f) }));
            list.Add(Make("EVT_MKT_AIRCRAFT_DEPR", "二手飞机贬值",
                "机队残值重估承压，融资条件收紧。",
                RandomEventCategories.Market, 0.5f,
                new[] { E("satisfaction", -4f), E("monthlyBurn", 3f) }));
            list.Add(Make("EVT_MKT_SAFE_HAVEN", "避险资金流入",
                "市场避险情绪推高航空资产关注度。",
                RandomEventCategories.Market, 0.45f,
                new[] { E("satisfaction", 3f), E("cash", 20f) }));

            // —— 政策变化（续）——
            list.Add(Make("EVT_POL_NOISE_CURFEW", "枢纽宵禁延长",
                "夜间起降受限，红眼航班排班承压。",
                RandomEventCategories.Policy, 0.55f,
                new[] { E("onTime", -3f), E("cash", -18f) }));
            list.Add(Make("EVT_POL_OPEN_SKIES", "开放天空协议",
                "新增航权机会，跨境客流预期改善。",
                RandomEventCategories.Policy, 0.55f,
                new[] { E("brand", 4f), E("loadFactor", 3f), E("cash", 22f) }));
            list.Add(Make("EVT_POL_SAFETY_AUDIT", "局方安全审计",
                "临时安全审计占用资源，安全指数短期承压。",
                RandomEventCategories.Policy, 0.5f,
                new[] { E("safetyIndex", -4f), E("cash", -20f), E("satisfaction", -2f) }));
            list.Add(Make("EVT_POL_TAX_INCENTIVE", "航空税收优惠",
                "地方税收减免落地，现金改善。",
                RandomEventCategories.Policy, 0.55f,
                new[] { E("cash", 40f), E("satisfaction", 3f) },
                oneShot: true));
            list.Add(Make("EVT_POL_ENV_REG", "环保新规落地",
                "排放合规成本上升，月度开销增加。",
                RandomEventCategories.Policy, 0.55f,
                new[] { E("monthlyBurn", 4f), E("satisfaction", -2f) }));
            list.Add(Make("EVT_POL_AIRPORT_FEE", "机场起降费上调",
                "枢纽运营成本抬升。",
                RandomEventCategories.Policy, 0.6f,
                new[] { E("monthlyBurn", 4f), E("cash", -15f) }));
            list.Add(Make("EVT_POL_SLOT_AUCTION", "黄金时刻拍卖",
                "竞拍新时刻需一次性支出，但提升长期收益。",
                RandomEventCategories.Policy, 0.45f,
                new[] { E("cash", -35f), E("loadFactor", 3f), E("brand", 2f) }));
            list.Add(Make("EVT_POL_NATIONALIZATION", "国有化传闻",
                "市场担忧股权稳定，董事会情绪波动。",
                RandomEventCategories.Policy, 0.35f,
                new[] { E("satisfaction", -6f), E("brand", -3f) }));
            list.Add(Make("EVT_POL_BILATERAL", "双边航权扩容",
                "与主要伙伴国新增航权配额。",
                RandomEventCategories.Policy, 0.5f,
                new[] { E("loadFactor", 3f), E("brand", 3f), E("cash", 20f) }));
            list.Add(Make("EVT_POL_CARBON_QUOTA", "碳配额分配收紧",
                "免费配额比例下调，碳成本上升。",
                RandomEventCategories.Policy, 0.5f,
                new[] { E("monthlyBurn", 5f), E("cash", -20f) }));
            list.Add(Make("EVT_POL_LABOR_LAW", "劳动法新规",
                "用工合规成本上升，人力预算承压。",
                RandomEventCategories.Policy, 0.5f,
                new[] { E("monthlyBurn", 3f), E("satisfaction", -2f) }));
            list.Add(Make("EVT_POL_EXPORT_CTRL", "机队引进管制",
                "关键机型引进受限，扩张计划暂缓。",
                RandomEventCategories.Policy, 0.35f,
                new[] { E("satisfaction", -5f), E("brand", -2f) }));

            // —— 供应链（续）——
            list.Add(Make("EVT_SUP_OEM_RECALL", "制造商批量召回",
                "在役机型批次召回检修，停场增多。",
                RandomEventCategories.Supply, 0.45f,
                new[] { E("cash", -48f), E("monthlyBurn", 5f), E("safetyIndex", 2f) }));
            list.Add(Make("EVT_SUP_ENGINE_ISSUE", "发动机批次隐患",
                "发动机检查间隔缩短，维护负担加重。",
                RandomEventCategories.Supply, 0.5f,
                new[] { E("monthlyBurn", 6f), E("onTime", -2f) }));
            list.Add(Make("EVT_SUP_PARTS_RECALL", "航材部件召回",
                "部分航材召回换件，临时成本。",
                RandomEventCategories.Supply, 0.55f,
                new[] { E("cash", -28f), E("onTime", -2f) }));
            list.Add(Make("EVT_SUP_SUPPLIER_BANKRUPT", "核心供应商破产",
                "关键件供应中断，维护排程受阻。",
                RandomEventCategories.Supply, 0.4f,
                new[] { E("monthlyBurn", 5f), E("cash", -30f), E("onTime", -3f) }));
            list.Add(Make("EVT_SUP_SPARE_SURPLUS", "备件冗余降价",
                "航材市场供过于求，维护成本下降。",
                RandomEventCategories.Supply, 0.55f,
                new[] { E("monthlyBurn", -4f), E("cash", 18f) }));
            list.Add(Make("EVT_SUP_OEM_DISCOUNT", "制造商促销让利",
                "新机采购折扣，现金回血。",
                RandomEventCategories.Supply, 0.5f,
                new[] { E("cash", 30f), E("satisfaction", 2f) }));
            list.Add(Make("EVT_SUP_LOGISTICS_STRIKE", "物流工人罢工",
                "航材运输中断，周转效率下降。",
                RandomEventCategories.Supply, 0.5f,
                new[] { E("monthlyBurn", 4f), E("onTime", -3f) }));
            list.Add(Make("EVT_SUP_FUEL_TRUCK", "加油车短缺",
                "枢纽加油排队，周转时间拉长。",
                RandomEventCategories.Supply, 0.5f,
                new[] { E("onTime", -4f), E("cash", -14f) }));
            list.Add(Make("EVT_SUP_SEAT_SUPPLIER", "座椅供应商延期",
                "客舱改装延期，两舱计划后移。",
                RandomEventCategories.Supply, 0.45f,
                new[] { E("brand", -3f), E("cash", -16f) }));
            list.Add(Make("EVT_SUP_AVIONICS", "航电升级优惠",
                "航电厂商让利，安全与效率双升。",
                RandomEventCategories.Supply, 0.45f,
                new[] { E("safetyIndex", 2f), E("cash", 15f) }));
            list.Add(Make("EVT_SUP_TIRE_SHORT", "轮胎供应紧张",
                "起落架轮胎换装成本上升。",
                RandomEventCategories.Supply, 0.55f,
                new[] { E("cash", -20f), E("monthlyBurn", 3f) }));
            list.Add(Make("EVT_SUP_MRO_EXPANSION", "维修产能扩张",
                "第三方机库新增档期，维护更从容。",
                RandomEventCategories.Supply, 0.5f,
                new[] { E("onTime", 3f), E("cash", 16f) }));

            // —— 内部危机（续）——
            list.Add(Make("EVT_INT_PILOT_SICKOUT", "飞行员集体病假",
                "排班缺口骤增，准点与服务承压。",
                RandomEventCategories.Internal, 0.55f,
                new[] { E("onTime", -6f), E("satisfaction", -5f), E("cash", -30f) }));
            list.Add(Make("EVT_INT_EXEC_POACHED", "核心高管被挖角",
                "关键岗位空缺，士气受挫。",
                RandomEventCategories.Internal, 0.5f,
                new[] { E("satisfaction", -6f), E("brand", -3f), E("cash", -20f) }));
            list.Add(Make("EVT_INT_DATA_BREACH", "旅客数据泄露",
                "系统漏洞致数据外泄，声誉受损。",
                RandomEventCategories.Internal, 0.45f,
                new[] { E("brand", -8f), E("satisfaction", -4f), E("cash", -25f) }));
            list.Add(Make("EVT_INT_STRIKE_COUNTDOWN", "罢工倒计时启动",
                "劳资谈判破裂风险上升，若恶化将连锁。",
                RandomEventCategories.Internal, 0.5f,
                new[] { E("satisfaction", -8f), E("onTime", -3f) },
                "EVT_LABOR_TALK", 2));
            list.Add(Make("EVT_INT_EMPLOYEE_AWARD", "员工荣获行业奖项",
                "团队士气提振，品牌加分。",
                RandomEventCategories.Internal, 0.55f,
                new[] { E("satisfaction", 4f), E("brand", 4f) }));
            list.Add(Make("EVT_INT_ACTIVIST", "激进股东施压",
                "要求削减成本或提高分红，董事会承压。",
                RandomEventCategories.Internal, 0.5f,
                new[] { E("satisfaction", -6f), E("cash", -15f) }));
            list.Add(Make("EVT_INT_MGMT_SHAKEUP", "管理层重组",
                "内部架构调整，短期效率波动。",
                RandomEventCategories.Internal, 0.5f,
                new[] { E("satisfaction", -3f), E("cash", -15f), E("brand", -2f) }));
            list.Add(Make("EVT_INT_WHISTLE_REWARD", "举报奖励机制生效",
                "合规文化加分，治理评分提升。",
                RandomEventCategories.Internal, 0.45f,
                new[] { E("safetyIndex", 3f), E("satisfaction", 3f) }));
            list.Add(Make("EVT_INT_BURNOUT", "员工过劳预警",
                "长期高压引发倦怠，效率与满意度下降。",
                RandomEventCategories.Internal, 0.6f,
                new[] { E("satisfaction", -5f), E("onTime", -3f) }));
            list.Add(Make("EVT_INT_LEAK", "内部决策泄密",
                "战略信息外泄，竞争对手提前反应。",
                RandomEventCategories.Internal, 0.45f,
                new[] { E("brand", -4f), E("satisfaction", -3f), E("cash", -18f) }));
            list.Add(Make("EVT_INT_SERVICE_HIT", "服务口碑爆棚",
                "社交媒体好评刷屏，品牌与客座双升。",
                RandomEventCategories.Internal, 0.55f,
                new[] { E("brand", 6f), E("loadFactor", 3f), E("satisfaction", 2f) }));
            list.Add(Make("EVT_INT_INNOVATION_WIN", "创新成果获赞",
                "流程创新降低成本，行业关注度提升。",
                RandomEventCategories.Internal, 0.5f,
                new[] { E("monthlyBurn", -3f), E("brand", 3f) }));

            // —— 补充批次（P1 达标 120+）——
            list.Add(Make("EVT_NAT_BLIZZARD_UP", "超级暴雪持续",
                "连续降雪超出除冰能力，多日大面积延误。",
                RandomEventCategories.Natural, 0.45f,
                new[] { E("cash", -48f), E("onTime", -7f) }));
            list.Add(Make("EVT_NAT_TYPHOON_CHAIN", "台风二次登陆",
                "罕见路径折返二次登陆，应急资源告急。",
                RandomEventCategories.Natural, 0.35f,
                new[] { E("cash", -52f), E("onTime", -6f), E("loadFactor", -3f) }));
            list.Add(Make("EVT_NAT_TSUNAMI", "海啸预警警报",
                "沿海机场短时关闭，跨洋航班备降。",
                RandomEventCategories.Natural, 0.3f,
                new[] { E("cash", -46f), E("onTime", -5f) }));
            list.Add(Make("EVT_NAT_FREEZING_RAIN", "冻雨致跑道结冰",
                "跑道摩擦系数骤降，起降受限。",
                RandomEventCategories.Natural, 0.5f,
                new[] { E("onTime", -5f), E("cash", -26f) }));

            list.Add(Make("EVT_HUM_FIRE_ALARM", "机库灭火误报",
                "误报触发全场疏散，维修与航班中断。",
                RandomEventCategories.Human, 0.5f,
                new[] { E("onTime", -3f), E("cash", -14f) }));
            list.Add(Make("EVT_HUM_GATE_CLASH", "登机口调度冲突",
                "地面调度失误致两班共用登机口。",
                RandomEventCategories.Human, 0.55f,
                new[] { E("onTime", -4f), E("brand", -2f) }));
            list.Add(Make("EVT_HUM_RWY_LIGHT", "跑道灯光故障",
                "夜航灯光系统故障，夜间起降受限。",
                RandomEventCategories.Human, 0.5f,
                new[] { E("onTime", -4f), E("cash", -20f), E("safetyIndex", -2f) }));

            list.Add(Make("EVT_MKT_HEDGE_WIN", "燃油套保盈利",
                "此前的套保头寸盈利，成本压力减轻。",
                RandomEventCategories.Market, 0.5f,
                new[] { E("cash", 30f), E("satisfaction", 2f) }));
            list.Add(Make("EVT_MKT_PAX_SHIFT", "客源结构转移",
                "低价客源流失、高价客源增长，收益结构变化。",
                RandomEventCategories.Market, 0.55f,
                new[] { E("loadFactor", -3f), E("cash", 15f) }));
            list.Add(Make("EVT_MKT_GOLD", "金价异动",
                "避险情绪推高贵金属，航空融资成本波动。",
                RandomEventCategories.Market, 0.4f,
                new[] { E("satisfaction", -2f), E("cash", -15f) }));
            list.Add(Make("EVT_MKT_SEASON_FARE", "旺季票价上浮",
                "旺季临近，平均票价上移，收益改善。",
                RandomEventCategories.Market, 0.6f,
                new[] { E("cash", 38f), E("loadFactor", 2f) }));

            list.Add(Make("EVT_POL_NEW_REG", "民航局新规发布",
                "运营合规要求升级，短期成本上升。",
                RandomEventCategories.Policy, 0.55f,
                new[] { E("cash", -22f), E("satisfaction", -2f) }));
            list.Add(Make("EVT_POL_VISA_TIGHT", "签证政策收紧",
                "跨境客流预期下滑，国际线承压。",
                RandomEventCategories.Policy, 0.55f,
                new[] { E("loadFactor", -4f), E("cash", -20f) }));
            list.Add(Make("EVT_POL_AIRSPACE_CTRL", "空域管制收紧",
                "临时空域管制，航路绕飞成本上升。",
                RandomEventCategories.Policy, 0.5f,
                new[] { E("fuelPrice", 5f, true), E("onTime", -3f) },
                oil: true));

            list.Add(Make("EVT_SUP_CARGO_EQUIP", "货舱设备升级",
                "新装载设备到位，腹舱效率提升。",
                RandomEventCategories.Supply, 0.5f,
                new[] { E("cash", 18f), E("onTime", 2f) }));
            list.Add(Make("EVT_SUP_IFE_DELAY", "机上娱乐延期",
                "娱乐系统升级延期，两舱体验受影响。",
                RandomEventCategories.Supply, 0.45f,
                new[] { E("brand", -3f), E("cash", -14f) }));
            list.Add(Make("EVT_SUP_CATERING_RECALL", "餐食供应商召回",
                "一批餐食质量召回，临时更换供应商。",
                RandomEventCategories.Supply, 0.45f,
                new[] { E("cash", -18f), E("brand", -3f) }));

            list.Add(Make("EVT_INT_EXEC_SCANDAL", "高管个人丑闻",
                "高管负面新闻发酵，治理形象受损。",
                RandomEventCategories.Internal, 0.4f,
                new[] { E("brand", -7f), E("satisfaction", -5f) }));
            list.Add(Make("EVT_INT_SILO", "部门壁垒加剧",
                "跨部门协作不畅，执行效率下降。",
                RandomEventCategories.Internal, 0.55f,
                new[] { E("satisfaction", -4f), E("cash", -15f) }));
            list.Add(Make("EVT_INT_COMMENDATION", "团队表彰大会",
                "年度表彰提振士气，员工满意度上升。",
                RandomEventCategories.Internal, 0.55f,
                new[] { E("satisfaction", 5f), E("brand", 2f) }));
        }
    }
}

using System;
using System.Collections.Generic;
using AirlineCEO.Data;

#nullable disable

namespace AirlineCEO.Logic
{
    /// <summary>
    /// 动态事件发生器（GDB 22/29 交叉补强 · 全球航空动态消息流）。
    /// 现实数据「平淡期」时接管，注入高拟真度「伪装新闻」，保证每日/每周都有压力。
    /// 纯 C#：通过 Context 注入世界快照 + 玩家状态，seed 决定挑选（可离线复现测试）。
    /// </summary>
    public static class EventGenerator
    {
        /// <summary>世界上下文（外部现实快照 + 玩家状态）。</summary>
        public struct Context
        {
            public float oilPrice;           // 美元/吨
            public int oilStableQuarters;    // 油价连续平淡季度数
            public int quietQuarters;        // 连续无重大事件季度数
            public int profitStreak;         // 连续盈利季度数
            public float onTimePercent;      // 准点率 %
            public float loadFactorPercent;  // 客座率 %
            public float boardSatisfaction;  // 董事会满意度 0~100
            public float cash;               // 现金（万美元）
            public int year;
            public int quarter;              // 1~4
            public long seed;                // 随机种子（稳定可复现）
        }

        /// <summary>单条「伪装新闻」模板：条件 + 文案 + 影响。</summary>
        public class FeedTemplate
        {
            public FeedCategory category;
            public FeedEffect effect;
            public float effectValue;
            public string source;
            public string headline;
            public Func<Context, string> body;       // 正文（可插入数值）
            public Func<Context, bool> condition;    // 触发条件（null=恒可触发）
        }

        static FeedTemplate T(FeedCategory c, FeedEffect e, float v, string src, string head,
            Func<Context, string> body = null, Func<Context, bool> cond = null)
            => new FeedTemplate { category = c, effect = e, effectValue = v, source = src, headline = head, body = body, condition = cond };

        /// <summary>恒可触发的「行业日报」池（市场/行业/竞对，保证每季度都有新鲜压力）。</summary>
        public static readonly List<FeedTemplate> FillerTemplates = new List<FeedTemplate>
        {
            T(FeedCategory.Market, FeedEffect.FuelCost, 4f, "路透社", "OPEC+ 内部就减产配额现分歧，市场预期供应收紧",
                c => $"据路透社报道，OPEC+ 成员国就下阶段减产配额产生分歧，交易商普遍押注供应收紧，国际油价应声上行。"),
            T(FeedCategory.Market, FeedEffect.FuelCost, -4f, "彭博社", "全球库存高企，国际油价回落",
                c => $"彭博社消息，主要消费国战略储备释放叠加需求走弱，国际油价自高位回落。"),
            T(FeedCategory.Market, FeedEffect.Cash, -200f, "中国人民银行", "人民币中间价下调，进口成本上升",
                c => $"中国人民银行今日下调人民币兑美元中间价，进口航材与飞机备件成本相应抬升。"),
            T(FeedCategory.Market, FeedEffect.Cash, 150f, "彭博社", "人民币走强，进口结算成本下降",
                c => $"彭博社报道，人民币兑美元走强，以美元计价的进口订单结算成本相应下降。"),
            T(FeedCategory.Industry, FeedEffect.None, 0f, "波音公司", "波音宣布提升 787 产能以满足复苏需求",
                c => $"波音公司宣布将 787 梦想客机月产能提升至 5 架，以应对长途航线机队更新潮。业内关注交付节奏。"),
            T(FeedCategory.Industry, FeedEffect.None, 0f, "空客公司", "空客上调 A320neo 全年交付指引",
                c => $"空客公司上调 A320neo 全年交付指引，称供应链瓶颈正在缓解，窄体机运力有望加速释放。"),
            T(FeedCategory.Competitor, FeedEffect.LoadFactor, -2f, "航空周刊", "竞对开通同枢纽洲际新航线",
                c => $"航空周刊消息，某竞对航司宣布开通与本公司枢纽重叠的洲际新航线，短线客源分流压力上升。"),
            T(FeedCategory.Competitor, FeedEffect.LoadFactor, -3f, "航空周刊", "竞对掀起票价补贴大战",
                c => $"据航空周刊报道，区域竞对航司启动为期一季的票价补贴，抢客意图明显，市场票价中枢下移。"),
            T(FeedCategory.Market, FeedEffect.LoadFactor, 3f, "民航局", "全球航空需求强劲复苏",
                c => $"民航局发布行业数据，国际航线需求同比大幅回升，主要市场出行意愿增强。"),
            T(FeedCategory.Regulation, FeedEffect.OnTime, -3f, "民航局", "民航局发布新机型适航指令，检修工时增加",
                c => $"民航局发布适航指令，要求部分机型加装检查项目，机务排班工时相应上升，短期准点率承压。"),
            T(FeedCategory.Industry, FeedEffect.None, 0f, "商飞公司", "国产大飞机 C919 交付提速",
                c => $"商飞公司宣布 C919 交付提速，多家国内航司排队接机，国产机型在窄体机市场的份额预期上修。"),
            T(FeedCategory.Market, FeedEffect.None, 0f, "路透社", "航空货运需求走弱，腹舱收益承压",
                c => $"路透社报道，全球贸易降温拖累航空货运，腹舱运价连续走弱，业内货机运力投放趋于谨慎。"),
        };

        /// <summary>条件触发的「压力事件」池。</summary>
        public static readonly List<FeedTemplate> ConditionalTemplates = new List<FeedTemplate>
        {
            T(FeedCategory.Market, FeedEffect.FuelCost, 6f, "路透社", "OPEC+ 减产分歧升级，燃油价格应声上涨",
                c => $"据路透社报道，OPEC+ 内部就减产配额产生严重分歧，市场预期供应收紧，燃油价格应声上涨。",
                c => c.oilStableQuarters >= 3),
            T(FeedCategory.Market, FeedEffect.Cash, -180f, "中国人民银行", "央行调整中间价定价机制，本币单日波动",
                c => $"中国人民银行宣布调整中间价定价机制，本币对美元单日贬值，进口飞机成本上升。",
                c => c.oilStableQuarters >= 5),
            T(FeedCategory.Regulation, FeedEffect.LoadFactor, -3f, "监管机构", "反垄断审查：代码共享协议暂停 90 天",
                c => $"监管机构通知，因行业反垄断审查，相关代码共享协议将被暂停 90 天，合作收益随之中断。",
                c => c.profitStreak >= 2),
            T(FeedCategory.Regulation, FeedEffect.Satisfaction, -4f, "监管机构", "连续扩张触发监管关注",
                c => $"监管机构发函提示，连续盈利下的快速扩张已触发行业集中度关注。",
                c => c.profitStreak >= 4),
            T(FeedCategory.Industry, FeedEffect.OnTime, -2f, "空客公司", "图卢兹工厂电力故障，A320neo 交付延迟",
                c => $"突发：空客图卢兹工厂因电力故障停产 72 小时，A320neo 交付预计延迟至少 2 个月。",
                c => c.quietQuarters >= 3),
            T(FeedCategory.Seasonal, FeedEffect.OnTime, -5f, "民航局", "全国热门机场实施流量控制",
                c => $"民航局宣布全国热门机场实施流量控制，准点率预期下降，建议提前部署备份排班。",
                c => c.onTimePercent < 75f),
            T(FeedCategory.Regulation, FeedEffect.Cash, 300f, "民航局", "行业纾困方案落地",
                c => $"民航局发布行业纾困方案，对客座率偏低的航司发放一次性运营补贴。",
                c => c.loadFactorPercent < 50f),
            T(FeedCategory.Competitor, FeedEffect.Satisfaction, -3f, "人力资源顾问", "行业人才争夺加剧，核心岗位流失风险",
                c => $"人力资源顾问提示：行业人才争夺白热化，核心技术岗位面临挖角风险。",
                c => c.boardSatisfaction < 40f),
            T(FeedCategory.Market, FeedEffect.Satisfaction, -3f, "标普全球", "评级机构下调信用展望",
                c => $"标普全球将相关航司信用展望由「稳定」下调为「负面」，融资成本可能抬升。",
                c => c.cash < 0f),
            T(FeedCategory.Seasonal, FeedEffect.LoadFactor, 5f, "民航局", "春运临近，订票系统拥堵",
                c => $"春运临近，全国订票系统已出现拥堵，建议提前部署加班航班。",
                c => c.quarter == 1),
            T(FeedCategory.Seasonal, FeedEffect.LoadFactor, 4f, "民航局", "暑运高峰来临，热门航线一票难求",
                c => $"暑运高峰来临，学生客流叠加旅游客流，热门航线一票难求。",
                c => c.quarter == 3),
            T(FeedCategory.Seasonal, FeedEffect.LoadFactor, 4f, "民航局", "国庆黄金周临近，流量控制预期上升",
                c => $"国庆黄金周临近，民航局预告全国热门机场将实施流量控制，准点率预期下降，建议提前排班。",
                c => c.quarter == 4),
        };

        /// <summary>生成一批「伪装新闻」：恒 1 条行业日报 + 命中的条件事件，上限 maxItems。</summary>
        public static List<DynamicFeedItem> Generate(Context ctx, int maxItems, out int conditionalCount)
        {
            conditionalCount = 0;
            var result = new List<DynamicFeedItem>();
            var rng = new Random(unchecked((int)(ctx.seed & 0x7fffffff)));

            // 1) 行业日报（从填充池随机挑 1 条）
            var filler = FillerTemplates[rng.Next(FillerTemplates.Count)];
            result.Add(Build(ctx, filler, rng));

            // 2) 条件事件（命中即纳入，按模板顺序稳定）
            for (int i = 0; i < ConditionalTemplates.Count; i++)
            {
                if (result.Count >= maxItems) break;
                var t = ConditionalTemplates[i];
                if (t.condition != null && !t.condition(ctx)) continue;
                result.Add(Build(ctx, t, rng));
                conditionalCount++;
            }

            if (result.Count > maxItems)
                result.RemoveRange(maxItems, result.Count - maxItems);
            return result;
        }

        static DynamicFeedItem Build(Context ctx, FeedTemplate t, Random rng)
        {
            string body = t.body != null ? t.body(ctx) : t.headline;
            return new DynamicFeedItem
            {
                category = (int)t.category,
                effect = (int)t.effect,
                effectValue = t.effectValue,
                source = t.source,
                headline = t.headline,
                body = body,
                year = ctx.year,
                quarter = ctx.quarter,
                generated = true
            };
        }

        /// <summary>季度 → 近似月份（农历/公历节日锚点用）。</summary>
        public static int MonthOfQuarter(int quarter) => quarter switch
        {
            1 => 2,   // 春运
            2 => 5,   // 五一前后
            3 => 8,   // 暑运
            _ => 11   // 国庆黄金周
        };
    }
}

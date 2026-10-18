using System;
using System.Collections.Generic;

namespace AirlineCEO.Data
{
    /// <summary>单条 CEO 目标定义（GDD 第 8 章 T-01~T-23）。</summary>
    [Serializable]
    public class TargetDef
    {
        public string id;           // "T-01"
        public string chemistryId;  // 触发组合 "B-01"
        public string initiator;    // 发起董事，如 "A" / "P,I,C"
        public BoardTargetType type; // 简化为 4 类用于进度/结算
        public string title;        // 短标题
        public string difficulty;   // "中" / "高" / "极高"...
        public string description;  // 目标描述
        public string reward;       // 达标奖励
        public string penalty;      // 未达标惩罚
    }

    /// <summary>
    /// CEO 目标清单 T-01~T-23 权威目录（GDD 第 8 章）。
    /// TargetGenerator 依据化学反应组合在此查找对应目标编号与元数据。
    /// </summary>
    public static class TargetCatalog
    {
        private static List<TargetDef> _cache;

        public static IReadOnlyList<TargetDef> All
        {
            get
            {
                if (_cache == null) _cache = Build();
                return _cache;
            }
        }

        public static TargetDef Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < All.Count; i++)
                if (All[i] != null && All[i].id == id) return All[i];
            return null;
        }

        /// <summary>按化学反应组合 ID 查找对应目标（一组合对应一目标）。</summary>
        public static TargetDef GetByChemistry(string chemistryId)
        {
            if (string.IsNullOrEmpty(chemistryId)) return null;
            for (int i = 0; i < All.Count; i++)
            {
                var t = All[i];
                if (t != null && string.Equals(t.chemistryId, chemistryId, StringComparison.OrdinalIgnoreCase))
                    return t;
            }
            return null;
        }

        private static TargetDef T(string id, string chem, string initiator, BoardTargetType type,
            string title, string difficulty, string desc, string reward, string penalty)
        {
            return new TargetDef
            {
                id = id, chemistryId = chem, initiator = initiator, type = type,
                title = title, difficulty = difficulty, description = desc,
                reward = reward, penalty = penalty
            };
        }

        private static List<TargetDef> Build()
        {
            var list = new List<TargetDef>(23)
            {
                T("T-01", "B-01", "A", BoardTargetType.RouteExpansion, "北极航线", "中",
                    "任期内开辟至少1条北极航线，连续4季度客座率≥70%。",
                    "A满意度+30，国家形象+15，永久燃油补贴+5%。",
                    "A满意度-40，触发战略质疑事件。"),
                T("T-02", "B-02", "P,I,C", BoardTargetType.RouteExpansion, "金砖直飞", "高",
                    "开通至少2条金砖国家间直飞航线（覆盖2大洲），首年客座率≥65%。",
                    "三方满意度各+25，新兴市场补贴+30%。",
                    "三方满意度各-30，触发不信任声明。"),
                T("T-03", "B-03", "A,K,M,H", BoardTargetType.LoadFactor, "准点率+星级+机龄", "中高",
                    "连续两年保持准点率≥85%，Skytrax≥4星，机龄≤10年。",
                    "四方满意度各+15，欧洲融资成本永久-8%。",
                    "任一不达标则四方各-20，触发信任危机。"),
                T("T-04", "B-04", "B,J,O,I", BoardTargetType.LoadFactor, "亚洲占比+毛利", "中",
                    "亚洲内部航线占比≥40%，亚洲毛利率≥20%。",
                    "四方满意度各+20，亚洲审批速度+30%。",
                    "四方满意度各-25，限制亚洲预算。"),
                T("T-05", "B-05", "L", BoardTargetType.RouteExpansion, "中东协议", "中低",
                    "与至少1个中东枢纽签署≥5年战略合作协议。",
                    "L满意度+35，中东过境费-10%。",
                    "L满意度-30，收回货运支持。"),
                T("T-06", "B-06", "G,N,C", BoardTargetType.RouteExpansion, "美洲排名前3", "高",
                    "美洲航线网络覆盖率进入市场前3，区域营收年增≥15%。",
                    "三方满意度各+20，美洲合规审查-25%。",
                    "三方满意度各-25，G推动重组投票。"),
                T("T-07", "B-07", "E,C,K", BoardTargetType.Safety, "安全评级+零事故", "高",
                    "保持IASA 1级安全评级，零重大事故，机龄年降≥0.5岁。",
                    "三方满意度各+25，保险-8%，安全奖励+10%。",
                    "任何事故则三方-50，E提交不信任案。"),
                T("T-08", "B-08", "D,E,F", BoardTargetType.Safety, "ESG提升2级", "高",
                    "ESG评级提升至少2个等级，三项子指标高于行业平均。",
                    "三方满意度各+30，融资成本-10%，品牌+15%，碳合规成本-15%。",
                    "三方发起ESG整改令，每季度报告否则-10/季度。"),
                T("T-09", "B-09", "B,K", BoardTargetType.NetProfit, "负债率+回租+成本", "中高",
                    "资产负债率≤65%，售后回租比例≥30%，运营成本同比降≥8%。",
                    "B+35，K+30，融资成本-5%。",
                    "B-40，K-35，限制资本支出12个月。"),
                T("T-10", "B-10", "O,G", BoardTargetType.NetProfit, "停飞3条+再配置", "中",
                    "停飞或重组至少3条亏损航线，再配置后收益率提升≥10%。",
                    "O+30，G+35，网络效率+12%。",
                    "O-30，G-40，G发起战略审查。"),
                T("T-11", "B-11", "H,Q", BoardTargetType.LoadFactor, "两舱+NPS+非航", "中高",
                    "两舱硬件升级，高端NPS≥75，非航收入占比≥8%。",
                    "H+35，Q+30，高端客单价+15%。",
                    "H-35，Q-25，触发高端流失预警。"),
                T("T-12", "B-12", "A,P,I", BoardTargetType.RouteExpansion, "3条战略航线", "中高",
                    "开通至少3条战略性远程航线，5年预期IRR≥8%。",
                    "三方满意度各+25，政策补贴+20%，国家形象+20。",
                    "三方满意度各-30，质疑战略格局。"),
                T("T-13", "B-13", "B,G,J,K", BoardTargetType.NetProfit, "股息+EPS+股价", "高",
                    "每年股息率≥35%，EPS年增≥10%，股价回报≥行业平均。",
                    "四方满意度各+20，股价稳定+15%，机构增持。",
                    "四方各-35，集体质询，可能罢免。"),
                T("T-14", "B-14", "C,L,M,Q", BoardTargetType.RouteExpansion, "2项产业链合作", "高",
                    "完成至少2项产业链重大合作，项目IRR≥12%。",
                    "四方满意度各+20，合作成本-12%。",
                    "四方满意度各-25，议价能力下降。"),
                T("T-15", "B-15", "D,E,N,F,O", BoardTargetType.Safety, "零违规+审计", "中高",
                    "全年零重大违规，内部审计评分≥90分。",
                    "五人满意度各+15，治理评分+20，监管信任。",
                    "任何违规则五人各-30，独立调查进驻。"),
                T("T-16", "B-16", "B vs H", BoardTargetType.NetProfit, "成本vs服务平衡", "中",
                    "最终服务成本降幅≤3%，高端满意度降幅≤2%。",
                    "双方各+10，获「调解者」称号。",
                    "偏袒一方导致另一方-40，后续议案阻力+30%。"),
                T("T-17", "B-17", "C vs K", BoardTargetType.NetProfit, "买新vs租旧折中", "中",
                    "新机采购占40%~60%，综合融资成本增幅≤5%。",
                    "双方各+10，计划顺利推进。",
                    "偏袒一方导致另一方-45，议案无限期搁置。"),
                T("T-18", "B-18", "A,P,I vs G,O", BoardTargetType.RouteExpansion, "开停比+网络增速", "中高",
                    "新开/停飞航线比1.2~1.8，网络年增8%~15%。",
                    "两派各+10，网络健康度+15%。",
                    "过度扩张/收缩导致对应派-35，极端分裂。"),
                T("T-19", "B-19", "D vs G,L", BoardTargetType.Safety, "环保投入比例", "中",
                    "环保投入占净利润3%~4%，碳强度下降≥8%。",
                    "D+15，G/L各+10，合规达标。",
                    "比例<3%则D-50；>4%则G/L各-35。"),
                T("T-20", "B-20", "F vs G,B", BoardTargetType.LoadFactor, "劳资和谐", "高",
                    "成本节省≥60%，员工满意度降幅≤10%，零罢工。",
                    "F+20，G/B各+15，关系稳定。",
                    "罢工则F-80，G/B-50，危机问责。"),
                T("T-21", "B-21", "D,E,N,F,O", BoardTargetType.Safety, "独立治理", "中高",
                    "独立报告覆盖率100%，治理评分≥85，中小股东满意度≥80%。",
                    "治理+20，融资-8%，品牌+15%。",
                    "独立委员会发起整改令，决策自由度-30%。"),
                T("T-22", "B-22", "A,P,I,B", BoardTargetType.NetProfit, "大股东议案", "中",
                    "每年至少2项大股东议案通过，中小股东投诉率≤3%。",
                    "四方满意度各+20，CEO连任支持率+25%。",
                    "四方满意度各-30，触发履职评估。"),
                T("T-23", "B-23", "C,L,M,Q", BoardTargetType.RouteExpansion, "全产业链闭环", "极高",
                    "完成1个全链路整合项目，IRR≥15%，合作方满意度≥80%。",
                    "四方满意度各+30，永久成本-18%，解锁成就。",
                    "四方满意度各-35，合作破裂。"),
            };
            return list;
        }
    }
}

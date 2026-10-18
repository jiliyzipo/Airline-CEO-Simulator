using System;
using System.Collections.Generic;

namespace AirlineCEO.Data
{
    /// <summary>AI 竞争对手航司（Phase 11 · GDD 9.2）。</summary>
    [Serializable]
    public class AIAirline
    {
        public string id;
        public string name;
        public string type;   // full_service / low_cost / regional / cargo
        public string state;  // stable / expanding / defensive / dying
        public float cash;
        public float marketShare;
        public string[] routes = Array.Empty<string>();
        public float avgFare = 100f;
        public int fleetSize = 5;
        public int reputation = 60;
        public int expansionTimer;
        public bool isAlive = true;
        public bool isPredator;
        public float debtRatio;          // 0～1
        public int dyingQuarters;       // 垂死持续季度数
        public int bankruptYear;        // 破产年份，用于复活计时
        public bool hatesPlayer;        // 仇恨标记（代码共享背叛）
        public float askUnits;          // 本季 ASK 估算
        public float weeklyFreqOnOverlap = 7f; // 与玩家对飞时的周班示意
    }

    public static class AIAirlineTypes
    {
        public const string FullService = "full_service";
        public const string LowCost = "low_cost";
        public const string Regional = "regional";
        public const string Cargo = "cargo";

        public static string DisplayName(string t) => t switch
        {
            FullService => "全服务巨头",
            LowCost => "低成本杀手",
            Regional => "区域支线王",
            Cargo => "货运物流商",
            _ => t ?? "未知"
        };
    }

    public static class AIAirlineStates
    {
        public const string Stable = "stable";
        public const string Expanding = "expanding";
        public const string Defensive = "defensive";
        public const string Dying = "dying";

        public static string DisplayName(string s) => s switch
        {
            Stable => "稳态",
            Expanding => "进攻",
            Defensive => "防御",
            Dying => "垂死挣扎",
            _ => s ?? "—"
        };
    }

    [Serializable]
    public class AIAirlineListSave
    {
        public AIAirline[] items;
        public float playerMarketShare;
        public float brandAwareness = 70f;
        public float codeShareLoadBonus;
        public bool codeShareActive;
        public string codeSharePartnerId;
        public int lastCodeShareYear;
        public string pendingCodeShareFromId;
        public string lastCompetitionLog;
        public float[] playerShareHistory;
        public int nextAiSpawnYear;
    }

    [Serializable]
    public class CodeShareProposal
    {
        public string fromAiId;
        public string fromAiName;
        public string message;
    }
}

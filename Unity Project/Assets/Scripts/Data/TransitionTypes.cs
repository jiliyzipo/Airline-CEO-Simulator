using System;

namespace AirlineCEO.Data
{
    /// <summary>
    /// 过渡业务目录（GDD ·「开局过渡业务机制 · 三架飞机的盈利设计」）。
    /// 公司刚注册、定期航线许可还在审批、机组还在招聘时，把闲置飞机投入这四类业务换取现金流。
    /// 四类业务共用一套「合同占用飞机 N 天、逐日入账」模型，差别只在门槛 / 档期 / 费率 / 并发上限。
    /// </summary>
    public static class TransitionKinds
    {
        /// <summary>干租：只出租飞机，承租方自备机组。开局唯一可用。</summary>
        public const string Dry = "dry";
        /// <summary>湿租：飞机连同机组一并出租，租金约为干租两倍。</summary>
        public const string Wet = "wet";
        /// <summary>包机：用自己的飞机与机组执飞临时任务，按趟结算、单价最高。</summary>
        public const string Charter = "charter";
        /// <summary>培训：为航校提供带飞与模拟机训练，按批结算。</summary>
        public const string Training = "training";

        public static readonly string[] All = { Dry, Wet, Charter, Training };

        public static string Label(string kind) => kind switch
        {
            Dry => "干租",
            Wet => "湿租",
            Charter => "包机",
            Training => "培训",
            _ => "过渡业务"
        };

        /// <summary>列表用的单字图标。</summary>
        public static string Glyph(string kind) => kind switch
        {
            Dry => "干",
            Wet => "湿",
            Charter => "包",
            Training => "训",
            _ => "过"
        };

        public static string Hint(string kind) => kind switch
        {
            Dry => "只出租飞机，机组与维护由承租方自备。长期有客户，租金稳定，是等许可证期间的主力现金来源。",
            Wet => "飞机连同机组一并出租，租金约为干租两倍，但市场客户稀少，同时只能接一家。",
            Charter => "用自己的飞机与机组执飞临时任务，按天结算、单价最高，但报价有效期仅 3 天，手慢即失。",
            Training => "为航校提供带飞与模拟机训练，按天结算，需要客舱经理到位组织教员。",
            _ => ""
        };

        /// <summary>长档期业务（可提前召回）。</summary>
        public static bool IsLease(string kind) => kind == Dry || kind == Wet;

        public static bool IsValid(string kind)
        {
            for (int i = 0; i < All.Length; i++)
                if (All[i] == kind) return true;
            return false;
        }
    }

    /// <summary>
    /// 过渡业务合同：占用一架飞机 <see cref="totalDays"/> 天，合同总额 = <see cref="rateWanPerDay"/> × 天数。
    /// 付款分三段（CEO 定金/尾款制）：签约收 <see cref="depositWan"/> 定金 → 执行期逐日入账
    /// <see cref="dailyCashWan"/> → 交机结算 <see cref="balanceWan"/> 尾款。
    /// <see cref="startUtcTicks"/> 可指向未来日期：起始日未到时合同处于「预租」（已占机位、未执行）。
    /// </summary>
    [Serializable]
    public class TransitionContract
    {
        public string id;
        /// <summary>被占用的飞机注册号。</summary>
        public string aircraftReg;
        /// <summary>过渡业务类型，见 <see cref="TransitionKinds"/>。</summary>
        public string kind = TransitionKinds.Dry;
        /// <summary>承租方 / 包机客户 / 航校名称。</summary>
        public string client;
        /// <summary>名义日费率（万美元/天），合同总额 = 该值 × 天数。</summary>
        public float rateWanPerDay;
        /// <summary>合同总档期（天）。</summary>
        public int totalDays;
        /// <summary>合同起始时刻（UTC ticks）；可位于未来（预租）；0 = 旧档，按已执行天数反推。</summary>
        public long startUtcTicks;
        /// <summary>已执行天数。</summary>
        public int daysUsed;
        /// <summary>-1 = 未申请召回；>=0 = 召回通知期剩余天数（归零即交机）。</summary>
        public int recallDaysLeft = -1;
        /// <summary>提前召回的违约金（万美元），仅在 <see cref="recallDaysLeft"/> 归零时扣收。</summary>
        public float recallFeeWan;
        /// <summary>已汇报过的履约节点（天）：0=尚未、30=三分之一、60=三分之二。</summary>
        public int lastMilestone;

        // ———— 付款里程碑（定金 / 逐日 / 尾款）————
        /// <summary>定金（万美元），签约当日收讫。</summary>
        public float depositWan;
        /// <summary>定金是否已收。</summary>
        public bool depositPaid;
        /// <summary>尾款（万美元），交机 / 到期当日结算。</summary>
        public float balanceWan;
        /// <summary>尾款是否已结清。</summary>
        public bool balancePaid;
        /// <summary>执行期逐日入账金额（万美元/天）= 名义日费率 × 逐日比例。</summary>
        public float dailyCashWan;
        /// <summary>签约时刻（UTC ticks），用于「预租」排序与到期提示。</summary>
        public long signedUtcTicks;

        public int DaysLeft => Math.Max(0, totalDays - daysUsed);
        public float Progress01 => totalDays <= 0 ? 0f : Math.Min(1f, daysUsed / (float)totalDays);
        /// <summary>合同全程总额（万美元）。</summary>
        public float TotalWan => rateWanPerDay * totalDays;
        public bool Recalling => recallDaysLeft >= 0;

        /// <summary>起始时刻（UTC ticks）；0 = 旧档（视为已开始）。</summary>
        public long StartTicks => startUtcTicks;
        /// <summary>结束时刻（UTC ticks）；0 = 未知（旧档）。</summary>
        public long EndTicks => startUtcTicks > 0 ? startUtcTicks + (long)Math.Max(1, totalDays) * TimeSpan.TicksPerDay : 0;

        /// <summary>是否处于「预租」：起始日在未来，已占机位但尚未执行。</summary>
        public bool IsReserved(long nowTicks) => startUtcTicks > 0 && nowTicks < startUtcTicks;

        /// <summary>执行期日入账金额（旧档回退为名义日费率）。</summary>
        public float EffectiveDailyCash => dailyCashWan > 0f ? dailyCashWan : rateWanPerDay;

        /// <summary>已到账合计（定金 + 逐日累计 + 尾款）。</summary>
        public float ReceivedWan =>
            (depositPaid ? depositWan : 0f) + EffectiveDailyCash * daysUsed + (balancePaid ? balanceWan : 0f);

        /// <summary>待收尾款（未结算时为尾款金额）。</summary>
        public float PendingBalanceWan => balancePaid ? 0f : balanceWan;
    }

    /// <summary>待接单报价：挂在「过渡业务」面板上的机会，签下后转为合同。</summary>
    [Serializable]
    public class TransitionOffer
    {
        public string id;
        public string kind = TransitionKinds.Dry;
        public string client;
        /// <summary>基准日费率（万美元/天，按 180 座级报价）；实际费率再乘签约机型的座位系数。</summary>
        public float rateWanPerDay;
        /// <summary>档期天数（干租/湿租签约时统一为 90 天）。</summary>
        public int totalDays;
        /// <summary>报价失效时刻（UTC ticks）；0 = 常驻不过期（干租/湿租客户）。</summary>
        public long expireUtcTicks;
        /// <summary>剩余有效天数；0 = 常驻。</summary>
        public int DaysLeftUtc(long nowTicks)
        {
            if (expireUtcTicks <= 0) return 0;
            long rest = expireUtcTicks - nowTicks;
            if (rest <= 0) return 0;
            return (int)Math.Ceiling(rest / (double)TimeSpan.TicksPerDay);
        }
    }

    [Serializable]
    public class TransitionSave
    {
        public TransitionContract[] contracts;
        public TransitionOffer[] offers;
        public bool crewMailSent;
        public bool instructorMailSent;
    }
}

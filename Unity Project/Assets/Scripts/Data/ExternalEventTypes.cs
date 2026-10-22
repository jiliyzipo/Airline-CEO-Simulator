using System;
using System.Collections.Generic;

#nullable disable

namespace AirlineCEO.Data
{
    /// <summary>
    /// 已映射到游戏时间轴的外部事件（现实只提供内容，游戏提供时间戳）。
    /// DateTime 以 UTC ticks 存档，避免 JsonUtility 丢时区。
    /// </summary>
    [Serializable]
    public class ExternalEvent
    {
        public string id;
        public string category;
        public string title;
        public string content;
        public string source;
        public float relativeOffsetHours;
        public string relativeDesc;
        public long gameReceivedTicks;
        public long gameEffectiveTicks;
        public string effectType;
        public float effectValue;
        public string affectedRegion;
        public int durationHours;
        public string chainId;
        public bool isolated;
        public bool isTriggered;
        public bool isResolved;
        public bool generated;
        /// <summary>数值影响由关键词研判而来（非原文自带），邮件里会注明「公司研判」。</summary>
        public bool inferred;
        /// <summary>研判依据（命中的关键词 + 幅度口径），供邮件内部标注使用。</summary>
        public string impactNote;

        public DateTime GameReceivedTime
        {
            get => TicksToUtc(gameReceivedTicks);
            set => gameReceivedTicks = value.ToUniversalTime().Ticks;
        }

        public DateTime GameEffectiveTime
        {
            get => TicksToUtc(gameEffectiveTicks);
            set => gameEffectiveTicks = value.ToUniversalTime().Ticks;
        }

        static DateTime TicksToUtc(long ticks)
            => ticks <= 0 ? DateTime.MinValue : new DateTime(ticks, DateTimeKind.Utc);
    }

    /// <summary>
    /// 外部数据源入站载荷：可带现实发布时间，仅用于提取相对间隔，入队后丢弃。
    /// <para>
    /// 时间取值优先级：正文/标题里的「N 天后 / N 小时后 / 即刻」→ 日期型（日粒度，落在游戏本地零时）
    /// → 无明确时间则次日本地零时。realEffectiveAt 仅作留档，不参与换算（避免引入现实绝对时刻）。
    /// </para>
    /// </summary>
    public class ExternalEventPayload
    {
        public string id;
        public string category;
        public string title;
        public string content;
        public string source;
        public DateTime? publishedAt;
        /// <summary>现实生效时刻：仅留档参考，不参与游戏时间映射。</summary>
        public DateTime? realEffectiveAt;
        public bool expired;
        public string effectType;
        public float effectValue;
        public string affectedRegion;
        public int durationHours;
        public string chainId;
        public bool generated;
        /// <summary>数值影响由关键词研判而来（非原文自带）。</summary>
        public bool inferred;
        /// <summary>研判依据（命中的关键词 + 幅度口径）。</summary>
        public string impactNote;
    }

    [Serializable]
    public class ExternalEventListSave
    {
        public List<ExternalEvent> items = new List<ExternalEvent>();
        public string freezeRegion = "";
        public long freezeUntilTicks;
    }
}

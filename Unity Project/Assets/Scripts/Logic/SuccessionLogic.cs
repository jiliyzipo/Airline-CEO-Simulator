using System.Collections.Generic;
using AirlineCEO.Data;

namespace AirlineCEO.Logic
{
    /// <summary>n91 高潜/继任名单，与员工池同一数据源。</summary>
    public static class SuccessionLogic
    {
        public const float HipoMin = 0.75f;
        public const int KeyPosts = 6;

        public static List<PersonnelData> HipoEmployed(IList<PersonnelData> pool)
        {
            var list = new List<PersonnelData>();
            if (pool == null) return list;
            for (int i = 0; i < pool.Count; i++)
            {
                var p = pool[i];
                if (p == null || p.onMarket) continue;
                // 运行序列（飞行员/机务等渠道批量岗）不属于「关键岗继任」口径：
                // 猎头潜力被 clamp 到 0.98，6 人即可把覆盖率顶到 100%，会伪造这个 KPI。
                if (p.isOperational) continue;
                if (p.potential < HipoMin) continue;
                list.Add(p);
            }
            list.Sort((a, b) => b.potential.CompareTo(a.potential));
            return list;
        }

        public static int AttritionWatch(IList<PersonnelData> pool)
        {
            int n = 0;
            if (pool == null) return 0;
            for (int i = 0; i < pool.Count; i++)
            {
                var p = pool[i];
                if (p == null || p.onMarket) continue;
                if (p.isOperational) continue;   // 与 HipoEmployed 同口径：本页只看关键人才库
                if (p.loyalty < 45f) n++;
            }
            return n;
        }

        public static float CoverPercent(int hipoCount)
        {
            float r = hipoCount / (float)KeyPosts;
            if (r > 1f) r = 1f;
            if (r < 0f) r = 0f;
            return r * 100f;
        }
    }
}

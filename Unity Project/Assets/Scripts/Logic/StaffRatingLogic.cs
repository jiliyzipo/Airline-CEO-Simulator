namespace AirlineCEO.Logic
{
    /// <summary>将人事数值映射为 1–20 能力栏（对标 FM 职员属性，不另存档）。</summary>
    public static class StaffRatingLogic
    {
        public struct Block
        {
            public string heading;
            public (string name, int value)[] items;
        }

        public static int To20(float t01)
        {
            int v = UnityEngine.Mathf.RoundToInt(UnityEngine.Mathf.Clamp01(t01) * 19f) + 1;
            return UnityEngine.Mathf.Clamp(v, 1, 20);
        }

        public static Block[] Build(string dept, string rank, float loyalty, float potential, float efficiency)
        {
            float eff01 = UnityEngine.Mathf.InverseLerp(0.45f, 1.35f, efficiency);
            float loy01 = UnityEngine.Mathf.Clamp01(loyalty / 100f);
            float pot01 = UnityEngine.Mathf.Clamp01(potential);
            int command = To20(eff01 * 0.7f + loy01 * 0.3f);
            int judgment = To20(loy01 * 0.5f + pot01 * 0.5f);
            int stamina = To20(0.4f + eff01 * 0.4f + Hash01(rank, 1) * 0.2f);

            var mental = new Block
            {
                heading = "精神",
                items = new[]
                {
                    ("决断", judgment),
                    ("适应", To20(pot01 * 0.6f + Hash01(dept, 2) * 0.4f)),
                    ("抗压", stamina),
                    ("忠诚倾向", To20(loy01)),
                }
            };
            var knowledge = new Block
            {
                heading = "知识",
                items = new[]
                {
                    ("专业判断", command),
                    ("培养潜力", To20(pot01)),
                    ("流程意识", To20(eff01 * 0.5f + Hash01(rank, 3) * 0.5f)),
                }
            };
            return new[] { Domain(dept, eff01, pot01), mental, knowledge };
        }

        static Block Domain(string dept, float eff01, float pot01)
        {
            return dept switch
            {
                "OPS" => new Block
                {
                    heading = "运行",
                    items = new[]
                    {
                        ("航班协调", To20(eff01)),
                        ("签派意识", To20(eff01 * 0.8f + pot01 * 0.2f)),
                        ("客舱标准", To20(0.45f + Hash01("OPS", 4) * 0.4f)),
                    }
                },
                "MNT" => new Block
                {
                    heading = "工程",
                    items = new[]
                    {
                        ("适航判断", To20(eff01)),
                        ("航材计划", To20(eff01 * 0.7f + pot01 * 0.3f)),
                        ("质量控制", To20(0.5f + Hash01("MNT", 5) * 0.4f)),
                    }
                },
                "MKT" => new Block
                {
                    heading = "市场",
                    items = new[]
                    {
                        ("收益直觉", To20(eff01)),
                        ("客户关系", To20(pot01 * 0.6f + eff01 * 0.4f)),
                        ("品牌公关", To20(0.45f + Hash01("MKT", 6) * 0.4f)),
                    }
                },
                "FIN" => new Block
                {
                    heading = "财务",
                    items = new[]
                    {
                        ("现金流", To20(eff01)),
                        ("预算控制", To20(eff01 * 0.8f + Hash01("FIN", 7) * 0.2f)),
                        ("融资判断", To20(pot01)),
                    }
                },
                "HR" => new Block
                {
                    heading = "人力",
                    items = new[]
                    {
                        ("招聘眼力", To20(pot01)),
                        ("劳资沟通", To20(eff01)),
                        ("组织设计", To20(0.5f + Hash01("HR", 8) * 0.4f)),
                    }
                },
                "SAF" => new Block
                {
                    heading = "安全",
                    items = new[]
                    {
                        ("SMS", To20(eff01)),
                        ("监察力度", To20(eff01 * 0.7f + pot01 * 0.3f)),
                        ("应急指挥", To20(0.5f + Hash01("SAF", 9) * 0.4f)),
                    }
                },
                "STG" => new Block
                {
                    heading = "战略",
                    items = new[]
                    {
                        ("网络规划", To20(eff01)),
                        ("航权研判", To20(pot01)),
                        ("投资分析", To20(0.45f + Hash01("STG", 10) * 0.45f)),
                    }
                },
                _ => new Block
                {
                    heading = "专业",
                    items = new[] { ("综合能力", To20(eff01)) }
                }
            };
        }

        static float Hash01(string s, int salt)
        {
            int h = salt * 397;
            if (!string.IsNullOrEmpty(s))
            {
                for (int i = 0; i < s.Length; i++)
                    h = unchecked(h * 31 + s[i]);
            }
            h = (h & 0x7fffffff) % 1000;
            return h / 999f;
        }
    }
}

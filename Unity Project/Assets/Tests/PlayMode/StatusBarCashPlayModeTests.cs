using AirlineCEO.Systems;
using AirlineCEO.UI.Shell;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using System.Collections;

namespace AirlineCEO.Tests.PlayMode
{
    /// <summary>
    /// 底栏状态读数（2026-11-02）。
    ///
    /// 起因是用户两次报「买了一条时刻槽，现金没有变化」——查下来钱确实扣了（页顶现金 3055→1875 万，
    /// 正好等于叫价 1180 万），但他看的是底栏那三个数：那里只有**季度运营口径**
    /// （净贡献 / 支出 / 盈亏），买卖时刻槽是资产置换，不进运营口径，所以三个数一个都不会动。
    /// 于是本栏补了一项 `现金:`（与航权页顶部同口径、同单位「万」）。
    ///
    /// 本用例钉两件事：
    /// <list type="number">
    /// <item><b>现金那一项真的跟着 gs.cash 走</b> —— 改一次余额、刷一次，读数必须变。</item>
    /// <item><b>六项都装得进自己的槽</b>（参考分辨率 1920，底栏左右各留 12px、读数区占左侧 40%）。
    ///   多插一项最容易的翻车方式是把邻居挤成省略号 —— 那样「现金」虽然加了，
    ///   另外三个数却变成「季度净贡…」，等于用一个新缺陷换一个旧缺陷。</item>
    /// </list>
    /// </summary>
    public class StatusBarCashPlayModeTests : IsolatedPlayModeFixture
    {
        static Transform Find(Transform parent, string name)
        {
            if (parent == null) return null;
            for (int i = 0; i < parent.childCount; i++)
            {
                var c = parent.GetChild(i);
                if (c.name == name) return c;
                var deep = Find(c, name);
                if (deep != null) return deep;
            }
            return null;
        }

        static TextMeshProUGUI Label(Transform root, string name)
        {
            var t = Find(root, name);
            return t != null ? t.GetComponent<TextMeshProUGUI>() : null;
        }

        [UnityTest]
        public IEnumerator CashLabel_FollowsGaState_AndSixItemsFitTheirSlots()
        {
            var gsHost = new GameObject("StatusBarGs");
            var gs = gsHost.AddComponent<GameState>();   // Awake 里登记 Instance
            gs.cash = 3055f;

            // 参考分辨率 1920：底栏左右各留 12px → 1896；读数区占左侧 40% ≈ 758px。
            var dock = new GameObject("Dock", typeof(RectTransform));
            dock.GetComponent<RectTransform>().sizeDelta = new Vector2(1896f, BottomDockView.BarHeight);

            var statsGo = new GameObject("Stats", typeof(RectTransform));
            statsGo.transform.SetParent(dock.transform, false);
            var srt = statsGo.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0f, 0f);
            srt.anchorMax = new Vector2(0.40f, 1f);
            srt.offsetMin = Vector2.zero;
            srt.offsetMax = Vector2.zero;

            // 必须走 Build：标签是在 BuildLabels() 里建的，AddComponent 出来的实例没有子项
            //（这条断言第一次跑就红了，正好证明它不是「夹具里空跑」）。
            var view = StatusBarView.Build(statsGo.transform, 0f, 1f);
            view.RefreshStats();
            Canvas.ForceUpdateCanvases();
            yield return null;

            var cash = Label(statsGo.transform, "Cash");
            Assert.IsNotNull(cash, "底栏缺「现金」项 —— 「花掉钱而底栏不动」会被读成没扣钱");
            Assert.IsTrue(cash.text.StartsWith(StatusBarView.CashPrefix),
                $"现金读数应以「{StatusBarView.CashPrefix}」开头，实际「{cash.text}」");

            string before = cash.text;
            gs.cash = 1875f;
            view.RefreshStats();
            yield return null;
            string after = cash.text;
            Assert.AreNotEqual(before, after,
                $"改了 gs.cash 之后底栏现金没变（{before} → {after}）—— 那一项就是死的，加了等于没加");
            StringAssert.Contains("1875", after.Replace(",", "").Replace("，", ""),
                $"现金读数没跟上余额：{after}");

            // —— 六项都装得下：标签的首选宽度超过自己的矩形，就说明被挤成了省略号 ——
            // 用「本游戏实际出现过的最长文案」量，而不是当前夹具碰巧算出来的数字：
            // 夹具里的余额 / 收支是随手给的，量出来的宽度偏小就测不出挤压。
            string[] names = { "Cash", "Income", "Expense", "Profit", "Routes", "Fleet" };
            string[] worst =
            {
                "现金:12,345万",                 // 现金单位是「万」，六位数已经是后期大航司
                "季度净贡献:21,015,410",
                "季度支出:21,015,410",
                "季度盈亏:-15,505,480",
                "航线:8条",
                "机队:12架"
            };
            for (int i = 0; i < names.Length; i++)
            {
                var label = Label(statsGo.transform, names[i]);
                Assert.IsNotNull(label, "底栏缺读数项 " + names[i]);

                label.text = worst[i];
                label.ForceMeshUpdate();
                float need = label.preferredWidth;
                float have = label.rectTransform.rect.width;
                Debug.Log($"[STATW] {names[i]} need={need:F1} have={have:F1} text={label.text}");

                Assert.Greater(have, 40f, names[i] + " 的槽宽只有 " + have.ToString("F0") + "px —— 量到的是夹具不是界面");
                Assert.LessOrEqual(need, have + 1f,
                    $"{names[i]}「{label.text}」要 {need:F0}px，槽只有 {have:F0}px —— 会被省略号截断。"
                    + "加新项时必须重新分配宽度，不能挤掉邻居。");
            }

            Object.Destroy(dock);
            Object.Destroy(gsHost);
            yield return null;
        }
    }
}

using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI.Setup
{
    /// <summary>接管开局：大洲 → 国家 → 城市 → 航司，交互对齐城市经济指数列表。</summary>
    public static class AirlineSelectionUI
    {
        /// <summary>滚动区域宿主名。</summary>
        public const string HostName = "AirlineTree";

        /// <summary>
        /// 由「距顶部像素偏移」换算 ScrollRect 的归一化位置（1 = 顶部，0 = 底部）。
        /// 内容不足一屏时一律回顶部 —— 这种情形下 ScrollRect.verticalNormalizedPosition
        /// 自身会退化成 0（语义上等于「底部」），若拿它当基准，会在列表由短变长的一瞬间
        /// 把视图甩到末尾（例：展开第一个大洲，列表刚超出一屏）。
        /// </summary>
        public static float NormalizedFor(float topOffset, float contentHeight, float viewHeight)
        {
            float hidden = contentHeight - viewHeight;
            return hidden > 0.01f ? Mathf.Clamp01(1f - topOffset / hidden) : 1f;
        }

        /// <summary>
        /// 建立本页滚动区域，**只应在首次绘制时调用一次**。
        /// 重绘列表时沿用返回的 Content（见 <see cref="Draw"/>），绝不重建 ——
        /// 重建会把 ScrollRect 一起换掉，归一化位置回到 1，表现为「点任意行列表跳回顶部」。
        /// </summary>
        public static RectTransform CreateArea(Transform form, out ScrollRect scroll)
        {
            var content = UIFactory.VerticalScrollArea(form, HostName,
                new UIFactory.Anchor(new Vector2(0.01f, 0.02f), new Vector2(0.99f, 0.98f), Vector2.zero, Vector2.zero));
            scroll = content != null ? content.GetComponentInParent<ScrollRect>(true) : null;
            return content;
        }

        /// <summary>
        /// 清空列表行，保留滚动区域本身。
        /// 不能 DestroyImmediate：本页的重绘由按下的那一行自己触发（onPick），
        /// 此刻那一行仍在自己的 <c>onClick</c> 调用栈里。
        /// 但 Destroy 要到帧末才生效，旧行会与新行共存一帧 —— 那一帧里整张表是双份的、
        /// 且被点中的按钮仍压在同名位置，所以先 SetParent(null) 让它立刻退出层级。
        /// </summary>
        public static void Clear(RectTransform content)
        {
            if (content == null) return;
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                var go = content.GetChild(i).gameObject;
                go.transform.SetParent(null, false);
                UnityEngine.Object.Destroy(go);
            }
        }

        /// <summary>重绘树；content 由调用方跨重绘复用（CreateArea 只建一次），滚动位置由调用方恢复。</summary>
        public static void Draw(RectTransform content, string selectedId,
            string expandedContinent, string expandedCountry, string expandedCity,
            Action<string> onPick, Action<string, string, string> onExpand)
        {
            if (content == null) return;
            Clear(content);

            var all = PresetAirlines.AllSelectable();
            float y = 4f;
            const float continentH = 42f;
            const float countryH = 38f;
            const float cityH = 36f;
            const float airlineH = 56f;

            for (int c = 0; c < CityEconomyCatalog.ContinentOrder.Length; c++)
            {
                string continent = CityEconomyCatalog.ContinentOrder[c];
                int nInContinent = 0;
                for (int i = 0; i < all.Count; i++)
                    if (all[i] != null && all[i].continentZh == continent) nInContinent++;
                if (nInContinent == 0) continue;

                bool openC = expandedContinent == continent;
                string capC = continent + (openC ? "  ▲" : "  ▼") + $"  ·  {nInContinent} 家";
                string continentCap = continent;
                var btnC = UIFactory.Button(content, "Cont" + c, capC, UIFactory.Fill,
                    openC ? UITheme.NavActive : UITheme.NavIdle, () =>
                    {
                        string next = continentCap == expandedContinent ? "" : continentCap;
                        onExpand(next, "", "");
                    }, 14);
                UIFactory.PinFromTop(btnC.gameObject, y, continentH, 0.01f, 0.96f);
                y += continentH + 6f;
                if (!openC) continue;

                var countries = new List<string>();
                for (int i = 0; i < all.Count; i++)
                {
                    var p = all[i];
                    if (p == null || p.continentZh != continent) continue;
                    if (!string.IsNullOrEmpty(p.countryZh) && !countries.Contains(p.countryZh))
                        countries.Add(p.countryZh);
                }

                for (int k = 0; k < countries.Count; k++)
                {
                    string country = countries[k];
                    int nInCountry = 0;
                    for (int i = 0; i < all.Count; i++)
                        if (all[i] != null && all[i].continentZh == continent && all[i].countryZh == country)
                            nInCountry++;

                    bool openK = expandedCountry == country;
                    string capK = "　　" + country + (openK ? "  ▲" : "  ▼") + $"  ·  {nInCountry}";
                    string countryCap = country;
                    var btnK = UIFactory.Button(content, "Nat" + c + "_" + k, capK, UIFactory.Fill,
                        openK ? UITheme.PanelDeep : UITheme.NavIdle, () =>
                        {
                            string next = countryCap == expandedCountry ? "" : countryCap;
                            onExpand(continentCap, next, "");
                        }, 13);
                    UIFactory.PinFromTop(btnK.gameObject, y, countryH, 0.04f, 0.96f);
                    y += countryH + 5f;
                    if (!openK) continue;

                    var cities = new List<string>();
                    for (int i = 0; i < all.Count; i++)
                    {
                        var p = all[i];
                        if (p == null || p.continentZh != continent || p.countryZh != country) continue;
                        if (!string.IsNullOrEmpty(p.cityZh) && !cities.Contains(p.cityZh))
                            cities.Add(p.cityZh);
                    }

                    for (int t = 0; t < cities.Count; t++)
                    {
                        string city = cities[t];
                        string cityKey = country + "|" + city;
                        int nInCity = 0;
                        for (int i = 0; i < all.Count; i++)
                            if (all[i] != null && all[i].continentZh == continent && all[i].countryZh == country
                                && all[i].cityZh == city)
                                nInCity++;

                        bool openCity = expandedCity == cityKey;
                        string capCity = "　　　　" + city + (openCity ? "  ▲" : "  ▼") + $"  ·  {nInCity}";
                        string cityCap = city;
                        var btnCity = UIFactory.Button(content, "City" + c + "_" + k + "_" + t, capCity, UIFactory.Fill,
                            openCity ? UITheme.PanelDeep : UITheme.NavIdle, () =>
                            {
                                string next = cityKey == expandedCity ? "" : cityKey;
                                onExpand(continentCap, countryCap, next);
                            }, 13);
                        UIFactory.PinFromTop(btnCity.gameObject, y, cityH, 0.07f, 0.96f);
                        y += cityH + 4f;
                        if (!openCity) continue;

                        for (int i = 0; i < all.Count; i++)
                        {
                            var p = all[i];
                            if (p == null || p.continentZh != continent || p.countryZh != country || p.cityZh != city)
                                continue;
                            bool on = selectedId == p.id;
                            string label =
                                $"{p.nameCN}  {p.iataCode}  ·  {p.hubAirport}\n" +
                                $"{TakeoverWorldBuilder.Stars(p.difficulty)} · 机队 {p.fleetCount} · 航线 {p.routeCount} · 现金 {p.cash:F0} 万";
                            var btn = UIFactory.Button(content, "Al_" + p.id, label, UIFactory.Fill,
                                on ? UITheme.Accent : UITheme.PanelDeep, () => onPick(p.id), 13);
                            UIFactory.PinFromTop(btn.gameObject, y, airlineH, 0.10f, 0.96f);
                            if (on)
                                UIFactory.AddBorder(btn.gameObject, UITheme.Warning, new Vector2(1.2f, -1.2f));
                            y += airlineH + 5f;
                        }
                    }
                }
            }

            UIFactory.SetScrollContentHeight(content, y + 12f);
        }
    }
}

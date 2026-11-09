using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>
    /// 「机场时刻」页（n77「航权与时刻槽」的第 4 个二级标签）—— 按用户 UI 原型重做（★2026-11-08）。
    ///
    /// **视觉**：机场汇总卡（可折叠）+ 展开后的时刻矩阵（早/中/晚/夜四段 × 15 分钟刻度 ×
    /// 离港/到港双泳道，方块上写逐刻可申请余量），工具栏带搜索 / 排序 / 过滤器，
    /// 点方块弹「时刻详情」。配色与排版对齐原型，飞机图标用原型那两条 SVG 路径 1:1 光栅化
    /// （见 <see cref="UIFactory.TakeoffSprite"/>）。
    ///
    /// **数据**：一个演示值都没有，全部走既有领域层 ——
    /// <list type="bullet">
    /// <item><see cref="IndustryAirlineLogic.SlotsSummary"/> —— 顶部全行业汇总；</item>
    /// <item><see cref="IndustryAirlineLogic.TotalsOf"/> —— 每张卡的池容量 / 我司 / 他航 / 余量；</item>
    /// <item><see cref="IndustryAirlineLogic.ChipsOf"/> —— 矩阵里每个 15 分钟刻的容量、已占、可申请；</item>
    /// <item><see cref="IndustryAirlineLogic.FilteredAirports(GameState, IndustryAirlineLogic.SlotFilterQuery)"/>
    /// —— 搜索 / 等级 / 我司航网 / 余量四组条件；</item>
    /// <item><see cref="AirportCatalog.InCurfew"/> / <see cref="AirportCatalog.CurfewLabel"/> —— 宵禁；</item>
    /// <item><see cref="SlotUsageLogic.KeyOf"/> —— 航季归属（夏秋 = Q2+Q3，冬春 = Q4+Q1）。</item>
    /// </list>
    ///
    /// **业务**：本页只读。时刻申请 / 二级市场买入 / 审批仍是运营部递提案、CEO 在收件箱批
    /// （2026-11-04 用户裁定），这里不放任何能绕过该渠道的按钮。
    ///
    /// **性能**：80 个协调机场一次全建是上千个 GameObject（旧卡片版实测「明显卡一下」）。
    /// ★2026-11-08 用户裁定取消分页、改一屏滚动后，改为**按视口窗口建卡**：只建当前可见的
    /// 几张 + 上下各三行余量，滚到边缘才补建 / 回收，滚动交给 ScrollRect；另只有展开的那一张
    /// 才建矩阵。
    /// </summary>
    public class AirportSlotsView : MonoBehaviour
    {
        // ———— 配色（取自原型 :root / .airport / .slot 等规则）————
        static readonly Color CardBg = new Color(0.078f, 0.161f, 0.263f, 0.97f);      // #142943
        static readonly Color HeadBg = new Color(0.098f, 0.184f, 0.314f, 0.98f);      // #192f50
        static readonly Color DetailBg = new Color(0.067f, 0.129f, 0.216f, 0.98f);    // #11203a
        static readonly Color MatrixBg = new Color(0.067f, 0.149f, 0.251f, 1f);       // #112640
        static readonly Color SlotBg = new Color(0.090f, 0.204f, 0.318f, 1f);         // #173451
        static readonly Color SlotGreen = new Color(0.298f, 0.890f, 0.620f, 1f);      // #4ce39e
        static readonly Color SlotFull = new Color(0.839f, 0.878f, 0.933f, 1f);       // #d6e0ee
        static readonly Color SlotCurfew = new Color(0.894f, 0.627f, 0.655f, 1f);     // #e4a0a7
        static readonly Color SlotCurfewBg = new Color(0.188f, 0.173f, 0.259f, 1f);   // #302c42
        static readonly Color SlotNa = new Color(0.443f, 0.553f, 0.663f, 1f);         // #718da9
        static readonly Color SlotMine = new Color(0.165f, 0.412f, 0.855f, 1f);       // #2a69da
        static readonly Color TrackBg = new Color(0.161f, 0.275f, 0.396f, 1f);        // #294665
        static readonly Color TrackFill = new Color(0.392f, 0.541f, 0.690f, 1f);      // #648ab0
        static readonly Color PopupBg = new Color(0.078f, 0.161f, 0.271f, 0.99f);     // #142945
        static readonly Color ChevronColor = new Color(0.529f, 0.710f, 0.925f, 1f);   // #87b5ec

        // ———— 版式常量（像素，画布参考分辨率 1920×1080）————
        const float HeaderH = 74f;
        const float DetailsH = 138f;
        const float MatrixH = 120f;
        const float RowGap = 6f;

        /// <summary>排序：0 = 我司持有 / 1 = 可申请余量 / 2 = 利用率 / 3 = 机场代码。</summary>
        static readonly string[] SortNames = { "按我司持有 ↓", "按可申请余量 ↓", "按利用率 ↓", "按机场代码" };

        RectTransform _root;
        RectTransform _cardsContent;
        RectTransform _cardsViewport;
        ScrollRect _cardsScroll;
        TextMeshProUGUI _countLabel;
        TMP_InputField _search;
        TextMeshProUGUI _sortText;
        TextMeshProUGUI _filterText;
        GameObject _filterPanel;
        GameObject _sortPanel;

        string _query = "";
        IndustryAirlineLogic.SlotFilterQuery _filter = IndustryAirlineLogic.SlotFilterQuery.Default;
        int _sortMode;
        string _expanded;

        // ———— 一屏滚动（★2026-11-08 用户裁定：取消 ◀ ▶ 翻页，改右侧滑条滚动）————
        //
        // 80 个协调机场一次全建是上千个 GameObject，旧卡片版实测「明显卡一下」—— 那正是当初
        // 加翻页的原因。改滚动后**不能**退回去全建：只建视口附近的卡（上下各留几行余量），
        // 滚到窗口边缘才补建 / 回收。于是滚动本身是原生 ScrollRect，帧内成本只有新露出的那几张卡。
        //
        // ★2026-11-08 修「滚到 DFW 以下是空的」：第一版窗口按 `anchoredPosition` 反推滚动距离，
        // 一旦读到的偏移与被建窗口对不上，判据 `last-1 <= _winHi` 会永远成立 → 窗口再不长个，
        // 屏幕上就是一片空白。现在改成**不信任何缓存值**：把视口 RectTransform 的四个世界角
        // 换到 Content 的本地空间，直接问「现在真正露出来的是哪几行」；窗口是否还要补建，
        // 按 `_live` 里逐行是否真有对象来判 —— 记账错了也自愈。
        readonly List<AirportInfo> _rows = new List<AirportInfo>();
        readonly List<float> _rowTop = new List<float>();
        readonly List<float> _rowH = new List<float>();
        readonly Vector3[] _corners = new Vector3[4];
        readonly Dictionary<string, int> _index = new Dictionary<string, int>();
        readonly Dictionary<string, GameObject> _live = new Dictionary<string, GameObject>();
        GameObject _emptyCard;
        int _winLo = -1;          // 已建窗口的首行（-1 = 未建）
        int _winHi = -1;          // 已建窗口的末行（含）
        bool _scrollHooked;
        bool _scrollRefResolved;

        // 过滤器面板里的「待确定」状态：勾选只改这里，点「确定」才落到 _filter 上。
        int _pTier = -1;
        bool _pMine;
        int _pOpen;
        int _pSort;

        public static AirportSlotsView Build(Transform host)
        {
            var go = new GameObject("AirportSlotsView", typeof(RectTransform));
            go.transform.SetParent(host, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;

            var view = go.AddComponent<AirportSlotsView>();
            view._root = rt;
            view.Rebuild();
            return view;
        }

        void OnDestroy()
        {
            // 页面被销毁时把还开着的详情弹窗一并关掉，免得它留在别的页面上。
            AirportSlotDetailModal.Close();
        }

        // ==================== 整页 ====================

        void Rebuild()
        {
            for (int i = _root.childCount - 1; i >= 0; i--) Destroy(_root.GetChild(i).gameObject);
            _cardsContent = null;
            _cardsViewport = null;
            _cardsScroll = null;
            _scrollHooked = false;
            _scrollRefResolved = false;
            _live.Clear();
            _index.Clear();
            _emptyCard = null;
            _winLo = _winHi = -1;
            _search = null;
            _sortText = null;
            _filterText = null;
            _filterPanel = null;
            _sortPanel = null;

            var gs = GameState.Instance;
            if (gs == null) return;

            BuildStatusRow(gs);
            BuildToolbar();
            BuildLegend();
            BuildCardsHost();
            RebuildCards();
        }

        void BuildStatusRow(GameState gs)
        {
            var stat = UIFactory.Label(_root, "SlotStat", IndustryAirlineLogic.SlotsSummary(gs), 12,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0f, 0.945f), new Vector2(0.615f, 1f),
                    Vector2.zero, Vector2.zero),
                UITheme.Success, FontStyles.Bold);
            UIFactory.ClampSingleLine(stat);

            var season = UIFactory.Label(_root, "SlotSeason", SeasonBadge(gs), 11,
                TextAlignmentOptions.MidlineRight,
                new UIFactory.Anchor(new Vector2(0.615f, 0.945f), new Vector2(1f, 1f),
                    Vector2.zero, Vector2.zero),
                UITheme.TextMuted);
            UIFactory.ClampSingleLine(season);
        }

        /// <summary>
        /// 航季口径以**游戏内日期 + 既有航季规则**为准（不编现实日历）：
        /// 夏秋 = 起始年 Q2–Q3，冬春 = 起始年 Q4 – 次年 Q1，见 <see cref="SlotUsageLogic.KeyOf"/>。
        /// </summary>
        static string SeasonBadge(GameState gs)
        {
            int year = gs.year > 0 ? gs.year : 1;
            int quarter = Mathf.Clamp(gs.quarter, 1, 4);
            int key = SlotUsageLogic.KeyOf(year, quarter);
            string name = SlotUsageLogic.SeasonName(key);
            int startYear = SlotUsageLogic.StartYearOf(key);
            string span = SlotUsageLogic.SeasonOf(key) == SlotUsageLogic.Summer
                ? $"第 {startYear} 年 Q2–Q3"
                : $"第 {startYear} 年 Q4 – 第 {startYear + 1} 年 Q1";
            return $"{name}航季　{span}　当前 第 {year} 年 Q{quarter}";
        }

        void BuildToolbar()
        {
            _search = UIFactory.Input(_root, "SlotSearch", "搜索机场代码或城市（如 AMS）",
                new UIFactory.Anchor(new Vector2(0f, 0.884f), new Vector2(0.175f, 0.938f),
                    Vector2.zero, Vector2.zero));
            // UIFactory.Input 会把占位符当值写进去（占位符里没有省略号时），这里显式清掉。
            _search.text = _query;
            // 只重建卡片区：重建整页会把正在输入的输入框销毁，每敲一个字就丢焦点。
            _search.onValueChanged.AddListener(v =>
            {
                _query = v ?? "";
                _filter.query = _query;
                RebuildCards(false);   // 换了搜索词回顶部看结果
            });

            var sortBtn = UIFactory.Button(_root, "SlotSortBtn", SortNames[0],
                new UIFactory.Anchor(new Vector2(0.182f, 0.884f), new Vector2(0.300f, 0.938f),
                    Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, ToggleSortPanel, 11);
            _sortText = sortBtn.GetComponentInChildren<TextMeshProUGUI>();
            if (_sortText != null) _sortText.text = SortNames[_sortMode];

            var filterBtn = UIFactory.Button(_root, "SlotFilterBtn", "过滤器 ▼",
                new UIFactory.Anchor(new Vector2(0.307f, 0.884f), new Vector2(0.400f, 0.938f),
                    Vector2.zero, Vector2.zero),
                UITheme.PanelDeep, ToggleFilterPanel, 11);
            _filterText = filterBtn.GetComponentInChildren<TextMeshProUGUI>();

            _countLabel = UIFactory.ClampSingleLine(UIFactory.Label(_root, "SlotCount", "", 10,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.410f, 0.884f), new Vector2(0.700f, 0.938f),
                    Vector2.zero, Vector2.zero),
                UITheme.TextMuted));

            // 原型的翻页按钮已按用户裁定撤掉：右侧滑条 + 滚轮一屏滚到底。
            // 留一句提示，免得玩家以为列表只有一屏、找不到其余机场。
            UIFactory.ClampSingleLine(UIFactory.Label(_root, "SlotScrollHint",
                "滚轮或右侧滑条滚动查看全部", 10, TextAlignmentOptions.MidlineRight,
                new UIFactory.Anchor(new Vector2(0.700f, 0.884f), new Vector2(0.985f, 0.938f),
                    Vector2.zero, Vector2.zero),
                new Color(UITheme.TextMuted.r, UITheme.TextMuted.g, UITheme.TextMuted.b, 0.75f)));

            UpdateFilterButtonText();
        }

        void BuildLegend()
        {
            UIFactory.ClampSingleLine(UIFactory.Label(_root, "SlotLegend",
                "绿＝该刻仍可申请　蓝＝我司持有　灰＝已满　禁＝宵禁时段　—＝该刻未公布容量　"
                + "点任一方块看该刻的离港 / 到港明细",
                9, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0f, 0.846f), new Vector2(1f, 0.879f),
                    Vector2.zero, Vector2.zero),
                UITheme.TextMuted));
        }

        void BuildCardsHost()
        {
            _cardsContent = UIFactory.VerticalScrollArea(_root, "SlotCards",
                new UIFactory.Anchor(new Vector2(0f, 0.004f), new Vector2(1f, 0.840f),
                    Vector2.zero, Vector2.zero));
            if (_cardsContent == null) return;
            _cardsViewport = _cardsContent.parent as RectTransform;
            HookScroll();
        }

        /// <summary>
        /// 拿到滚动区并挂一次 onValueChanged。拿不到也不要紧 —— 每帧的 <see cref="SyncWindow"/>
        /// 才是主路径，这里只是让拖动时同一帧就补建，省一帧空窗。
        /// </summary>
        void HookScroll()
        {
            if (_scrollHooked || _cardsContent == null) return;
            if (!_scrollRefResolved)
            {
                _scrollRefResolved = true;
                if (_cardsScroll == null)
                    _cardsScroll = _cardsContent.GetComponentInParent<ScrollRect>(true);
            }
            if (_cardsScroll == null) return;
            _cardsScroll.onValueChanged.AddListener(_ => SyncWindow(false));
            _scrollHooked = true;
        }

        /// <summary>
        /// 每帧确认窗口。**不**依赖任何缓存判据：真正露出来的是哪几行，每次都重新算；
        /// 该建的行只要有一个不在 <c>_live</c> 里就补建。静止帧只是十几次字典查询。
        /// </summary>
        void LateUpdate()
        {
            SyncWindow(false);
        }

        // ==================== 机场卡 ====================

        void RebuildCards(bool keepScroll = true)
        {
            if (_cardsContent == null) return;
            var gs = GameState.Instance;
            if (gs == null) return;

            var rows = IndustryAirlineLogic.FilteredAirports(gs, _filter);
            SortRows(gs, rows);
            LayoutRows(rows);

            ResetCards();
            if (!keepScroll) ScrollToTop();

            if (_countLabel != null)
                _countLabel.text = $"共 {_rows.Count} 个协调机场";

            if (_rows.Count == 0)
            {
                _emptyCard = UIFactory.Label(_cardsContent, "SlotEmpty",
                    "没有符合条件的机场。\n换个筛选条件，或清空搜索框 —— 支线机场为非协调机场，本来就不设时刻槽。",
                    11, TextAlignmentOptions.TopLeft, UIFactory.Fill, UITheme.TextMuted).gameObject;
                UIFactory.PinFromTop(_emptyCard, 14f, 64f, 0.02f, 0.98f);
                UIFactory.SetScrollContentHeight(_cardsContent, 96f);
                return;
            }

            SyncWindow(true);
        }

        /// <summary>
        /// 逐行算顶边与整表高度。行高 = 头 + （展开时）明细 + 行距；
        /// 只有一张卡能展开，所以高度表是确定的，窗口切分才能只靠一次前缀累加。
        /// </summary>
        void LayoutRows(List<AirportInfo> rows)
        {
            _rows.Clear();
            _rows.AddRange(rows);
            _rowTop.Clear();
            _rowH.Clear();
            _index.Clear();

            float y = 2f;
            for (int i = 0; i < _rows.Count; i++)
            {
                float h = RowHeight(_rows[i].code);
                _rowTop.Add(y);
                _rowH.Add(h);
                _index[_rows[i].code] = i;
                y += h + RowGap;
            }
            UIFactory.SetScrollContentHeight(_cardsContent, y + 8f);
        }

        float RowHeight(string code)
            => HeaderH + (string.Equals(_expanded, code, System.StringComparison.Ordinal) ? DetailsH : 0f);

        float RowHeight(int index) => _rowH[index];

        /// <summary>拆掉全部已建卡（改搜索词 / 筛选 / 排序后重排用）。</summary>
        void ResetCards()
        {
            foreach (var kv in _live)
            {
                var go = kv.Value;
                if (go == null) continue;
                go.SetActive(false);   // Destroy 要到帧末才移除，先失活免得与新卡叠一帧
                Destroy(go);
            }
            _live.Clear();
            if (_emptyCard != null)
            {
                _emptyCard.SetActive(false);
                Destroy(_emptyCard);
                _emptyCard = null;
            }
            _winLo = _winHi = -1;
        }

        void ScrollToTop()
        {
            if (_cardsContent == null) return;
            _cardsContent.anchoredPosition = new Vector2(_cardsContent.anchoredPosition.x, 0f);
            if (_cardsScroll != null) _cardsScroll.verticalNormalizedPosition = 1f;
        }

        /// <summary>视口高度。首帧未布局时给一个够用的估计，下一帧就用真值了。</summary>
        float ViewportHeight()
        {
            if (_cardsViewport != null && _cardsViewport.rect.height > 1f) return _cardsViewport.rect.height;
            float pageH = _root != null ? _root.rect.height : 0f;
            return pageH > 1f ? pageH * 0.55f : 480f;
        }

        /// <summary>
        /// 现在**真正露出来**的是哪几行。
        ///
        /// 不读 `anchoredPosition` 也不做「视口高度 × 系数」的估算：把视口四个世界角换到
        /// Content 的本地空间，直接和每行的顶边比。这样无论 ScrollRect 是拖滑条、滚轮还是
        /// 代码改位置，算出来的都是屏幕上那几行 —— 之前「滚到中间以下全是空白」就是因为
        /// 旧版拿一个可能过期的偏移量去比，判据恒真，窗口再也不长个。
        /// </summary>
        void VisibleRowRange(out int first, out int last)
        {
            int n = _rows.Count;
            first = 0;
            last = n;
            if (n == 0 || _cardsContent == null || _cardsViewport == null) return;

            _cardsViewport.GetWorldCorners(_corners);   // 0 左下 / 1 左上 / 2 右上 / 3 右下
            // Content 的 pivot 在顶边：行顶边在本地坐标里就是 -yTop。视口上沿 / 下沿换成同一坐标系。
            float visTop = _cardsContent.InverseTransformPoint(_corners[1]).y;
            float visBottom = _cardsContent.InverseTransformPoint(_corners[0]).y;

            first = n - 1;
            for (int i = 0; i < n; i++)
            {
                if (-(_rowTop[i] + _rowH[i]) <= visTop) { first = i; break; }
            }

            last = first + 1;
            for (int i = first; i < n; i++)
            {
                last = i + 1;
                if (-_rowTop[i] < visBottom) break;
            }
        }

        /// <summary>
        /// 已建窗口还包得住可见行（上下各留 2 行余量），且可见行**真的都有对象**。
        /// 后者是关键：旧版只比「上次记下的窗口号」，一旦记账错位就永久早退，屏幕一片空白。
        /// </summary>
        bool WindowCovers(int visLo, int visHi)
        {
            if (_winLo < 0) return false;
            // 余量也要按表头 / 表尾夹紧：不然滚到最顶或最底时「想要两行余量」永远满足不了，每帧重建。
            int needLo = Mathf.Max(0, visLo - 2);
            int needHi = Mathf.Min(_rows.Count - 1, visHi + 2);
            if (_winLo > needLo || _winHi < needHi) return false;
            for (int i = visLo; i <= visHi; i++)
            {
                string code = _rows[i].code;
                if (!_live.TryGetValue(code, out var go) || go == null) return false;
            }
            return true;
        }

        /// <summary>
        /// 补齐 / 回收可见窗口：可见行上方留 4 行、下方留 6 行余量。
        /// 余量是为了「滚两行才重建一次」，不然滚一格滚轮就要重拆重搭一屏。
        /// </summary>
        void SyncWindow(bool force)
        {
            if (_cardsContent == null || _rows.Count == 0) return;
            HookScroll();

            int n = _rows.Count;
            VisibleRowRange(out int first, out int last);
            int visLo = Mathf.Clamp(first, 0, n - 1);
            int visHi = Mathf.Clamp(last - 1, visLo, n - 1);

            if (!force && WindowCovers(visLo, visHi)) return;

            int lo = Mathf.Max(0, visLo - 4);
            int hi = Mathf.Min(n - 1, visHi + 6);

            var drop = new List<string>();
            foreach (var kv in _live)
            {
                if (kv.Value == null) { drop.Add(kv.Key); continue; }
                if (!_index.TryGetValue(kv.Key, out int idx)) { drop.Add(kv.Key); continue; }
                if (idx < lo || idx > hi) drop.Add(kv.Key);
            }
            for (int i = 0; i < drop.Count; i++)
            {
                if (_live.TryGetValue(drop[i], out var go))
                {
                    _live.Remove(drop[i]);
                    if (go != null) { go.SetActive(false); Destroy(go); }
                }
            }

            var gs = GameState.Instance;
            if (gs == null) return;
            for (int i = lo; i <= hi; i++)
            {
                string code = _rows[i].code;
                if (_live.TryGetValue(code, out var existing) && existing != null) continue;
                _live.Remove(code);
                var card = BuildCard(gs, _rows[i], _rowTop[i]);
                if (card != null) _live[code] = card;
            }
            _winLo = lo;
            _winHi = hi;

#if UNITY_EDITOR
            UnityEngine.Debug.Log($"[SlotScroll] 建窗 [{lo},{hi}] 可见 [{visLo},{visHi}] "
                + $"共 {n} 行 · 已建 {_live.Count} 张 · 内容高 {_cardsContent.rect.height:0} "
                + $"视口高 {ViewportHeight():0}");
#endif
        }

        /// <summary>
        /// 领域层的 <see cref="IndustryAirlineLogic.FilteredAirports(GameState, IndustryAirlineLogic.SlotFilterQuery)"/>
        /// 只认「我司持有 / 可申请余量」两种顺序；原型的下拉还有「利用率 / 代码」，
        /// 这两项在 UI 侧补排序，不改领域签名（回归用例仍按旧口径调用）。
        /// </summary>
        void SortRows(GameState gs, List<AirportInfo> rows)
        {
            if (_sortMode == 2)
            {
                var usage = new Dictionary<string, int>(rows.Count);
                for (int i = 0; i < rows.Count; i++)
                    usage[rows[i].code] = UsagePct(IndustryAirlineLogic.TotalsOf(gs, rows[i].code));
                rows.Sort((a, b) =>
                {
                    int ua = usage[a.code], ub = usage[b.code];
                    if (ua != ub) return ub.CompareTo(ua);
                    return string.CompareOrdinal(a.code, b.code);
                });
            }
            else if (_sortMode == 3)
            {
                rows.Sort((a, b) => string.CompareOrdinal(a.code, b.code));
            }
        }

        /// <summary>建一张机场汇总卡（含展开时的矩阵），返回卡片根节点供窗口回收。</summary>
        GameObject BuildCard(GameState gs, AirportInfo ap, float yTop)
        {
            var t = IndustryAirlineLogic.TotalsOf(gs, ap.code);
            bool open = string.Equals(_expanded, ap.code, System.StringComparison.Ordinal);
            float height = RowHeight(ap.code);

            var card = UIFactory.Panel(_cardsContent, "SlotAirportCard_" + ap.code, UIFactory.Fill, CardBg);
            UIFactory.PinFromTop(card, yTop, height, 0.002f, 0.996f);
            UIFactory.AddBorder(card, open
                ? new Color(0.247f, 0.388f, 0.561f, 1f)
                : new Color(0.173f, 0.271f, 0.396f, 1f), new Vector2(1.2f, -1.2f));

            // —— 头部：整块可点，折叠 / 展开 ——
            float headBottom = (height - HeaderH) / height;
            var head = UIFactory.Button(card.transform, "SlotHead_" + ap.code, "",
                new UIFactory.Anchor(new Vector2(0f, headBottom), new Vector2(1f, 1f),
                    Vector2.zero, Vector2.zero),
                HeadBg, () => Toggle(ap.code), 10);
            // 空的功能标签没有意义，删掉以免居中排版占位。
            var headLabel = head.transform.Find("Label");
            if (headLabel != null) headLabel.gameObject.SetActive(false);

            var h = head.transform;
            UIFactory.Label(h, "Code", ap.code, 18, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.008f, 0.50f), new Vector2(0.050f, 0.96f),
                    Vector2.zero, Vector2.zero),
                UITheme.TextPrimary, FontStyles.Bold);

            UIFactory.ClampSingleLine(UIFactory.Label(h, "City", ap.nameZh, 10,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.053f, 0.545f), new Vector2(0.165f, 0.925f),
                    Vector2.zero, Vector2.zero),
                UITheme.TextMuted));

            var tag = UIFactory.Panel(h, "Tier",
                new UIFactory.Anchor(new Vector2(0.053f, 0.12f), new Vector2(0.093f, 0.44f),
                    Vector2.zero, Vector2.zero),
                ap.tier == 0
                    ? new Color(0.129f, 0.278f, 0.471f, 1f)
                    : new Color(0.125f, 0.196f, 0.310f, 1f));
            var tagImg = tag.GetComponent<Image>();
            if (tagImg != null) tagImg.raycastTarget = false;
            UIFactory.Label(tag.transform, "L", AirportCatalog.TierLabel(ap.tier), 7,
                TextAlignmentOptions.Center, UIFactory.Fill, new Color(0.624f, 0.776f, 0.973f, 1f));

            // 占用：标签 + 进度条（留白部分就是还能申请的余量，比再读一个数字更快）
            int used = t.own + t.other;
            int pct = UsagePct(t);
            UIFactory.ClampSingleLine(UIFactory.Label(h, "Occupancy",
                $"机场时刻已占用 · {(t.cap > 0 ? $"{used}/{t.cap}" : "—")}", 10,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(0.170f, 0.545f), new Vector2(0.560f, 0.925f),
                    Vector2.zero, Vector2.zero),
                UITheme.TextMuted));

            var track = UIFactory.Panel(h, "Track",
                new UIFactory.Anchor(new Vector2(0.170f, 0.16f), new Vector2(0.545f, 0.30f),
                    Vector2.zero, Vector2.zero), TrackBg);
            var trackImg = track.GetComponent<Image>();
            if (trackImg != null) trackImg.raycastTarget = false;
            if (pct > 0)
            {
                var fill = UIFactory.Panel(track.transform, "Fill", UIFactory.Fill, TrackFill);
                var fillRt = fill.GetComponent<RectTransform>();
                fillRt.anchorMax = new Vector2(pct / 100f, 1f);
                var fillImg = fill.GetComponent<Image>();
                if (fillImg != null) fillImg.raycastTarget = false;
            }

            Metric(h, "Owned", 0.575f, 0.665f, "我司持有", t.own.ToString(), UITheme.TextPrimary);
            Metric(h, "Open", 0.680f, 0.770f, "可申请", t.open.ToString(),
                t.open > 0 ? SlotGreen : UITheme.TextMuted);
            Metric(h, "Usage", 0.785f, 0.900f, "利用率", pct >= 0 ? pct + "%" : "—",
                UITheme.TextPrimary);

            UIFactory.Label(h, "Chevron", open ? "▲" : "▼", 12, TextAlignmentOptions.Center,
                new UIFactory.Anchor(new Vector2(0.940f, 0.28f), new Vector2(0.990f, 0.72f),
                    Vector2.zero, Vector2.zero),
                ChevronColor);

            if (open)
            {
                var details = UIFactory.Panel(card.transform, "SlotDetails_" + ap.code, UIFactory.Fill, DetailBg);
                UIFactory.PinFromTop(details, HeaderH, DetailsH, 0f, 1f);
                var detailsImg = details.GetComponent<Image>();
                if (detailsImg != null) detailsImg.raycastTarget = false;
                BuildMatrix(gs, details.transform, ap);
            }

            return card;
        }

        static void Metric(Transform head, string name, float x0, float x1, string caption,
            string value, Color valueColor)
        {
            UIFactory.ClampSingleLine(UIFactory.Label(head, name + "Cap", caption, 9,
                TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(x0, 0.545f), new Vector2(x1, 0.900f),
                    Vector2.zero, Vector2.zero),
                UITheme.TextMuted));
            UIFactory.Label(head, name + "Val", value, 14, TextAlignmentOptions.MidlineLeft,
                new UIFactory.Anchor(new Vector2(x0, 0.120f), new Vector2(x1, 0.520f),
                    Vector2.zero, Vector2.zero),
                valueColor, FontStyles.Bold);
        }

        void Toggle(string code)
        {
            _expanded = string.Equals(_expanded, code, System.StringComparison.Ordinal) ? null : code;
            RebuildCards();
            if (_expanded != null) RevealRow(_expanded);
        }

        /// <summary>
        /// 展开后把这张卡拉进视野：卡片变高 138px，原先「刚好在屏幕底部」的那张
        /// 一展开就会把矩阵露在视口外，玩家会以为点了没反应。
        /// 只做最小幅度滚动：下面被切就下沿对齐，上面被切就上沿对齐。
        ///
        /// 全部用「视口角点 → Content 本地坐标」算，不猜 `anchoredPosition` 的正负号 ——
        /// 首版就是把它当成「向上为正」反用，导致滚动距离恒读成 0，窗口再也不长个。
        /// </summary>
        void RevealRow(string code)
        {
            if (_cardsContent == null || _cardsViewport == null) return;
            if (!_index.TryGetValue(code, out int idx)) return;

            _cardsViewport.GetWorldCorners(_corners);
            float visTop = _cardsContent.InverseTransformPoint(_corners[1]).y;
            float visBottom = _cardsContent.InverseTransformPoint(_corners[0]).y;

            float rowTop = -_rowTop[idx];
            float rowBottom = -(_rowTop[idx] + _rowH[idx]);

            float shift = 0f;
            if (rowBottom < visBottom) shift = rowBottom - visBottom;   // 下面超出 → 内容上移
            else if (rowTop > visTop) shift = rowTop - visTop;          // 上面超出 → 内容下移
            if (Mathf.Abs(shift) < 0.5f) return;

            _cardsContent.anchoredPosition += new Vector2(0f, -shift);
            SyncWindow(false);
        }

        static int UsagePct(IndustryAirlineLogic.AirportTotals t)
        {
            if (t.cap <= 0) return -1;
            int used = t.own + t.other;
            if (used < 0) used = 0;
            return Mathf.RoundToInt(used * 100f / t.cap);
        }

        // ==================== 时刻矩阵 ====================

        /// <summary>
        /// 矩阵：段头 / 小时头 / 15 分钟刻度 / 离港泳道 / 到港泳道。
        ///
        /// 列宽**按各段真实刻度数**分配（早 16 / 中 20 / 晚 20 / 夜 16），与原型
        /// `56px 16fr 20fr 20fr 40fr` 同一思路 —— 原型靠固定 min-width + 横向滚动，
        /// 这里改成铺满可用宽度：所有刻度一屏可见，不必横向拖动才能看到夜班。
        /// </summary>
        void BuildMatrix(GameState gs, Transform parent, AirportInfo ap)
        {
            var container = UIFactory.Panel(parent, "SlotMatrix_" + ap.code, UIFactory.Fill, MatrixBg);
            UIFactory.PinFromTop(container, 6f, MatrixH, 0.006f, 0.994f);
            var containerImg = container.GetComponent<Image>();
            if (containerImg != null) containerImg.raycastTarget = false;

            var grids = new List<string>[4];
            var chips = new IndustryAirlineLogic.SlotChip[4][];
            int total = 0;
            for (int b = 0; b < 4; b++)
            {
                // 先取逐刻数据，再据此决定画哪些刻：00–05 时通常没有容量，但真若有公布，
                // 也必须画出来 —— 宁可多一列，不能少一条能申请的槽。
                var bandChips = IndustryAirlineLogic.ChipsOf(gs, ap.code, b, out _, out _);
                grids[b] = MatrixGrid(b, bandChips);
                chips[b] = AlignChips(bandChips, grids[b]);
                total += grids[b].Count;
            }
            if (total <= 0) return;

            const float labelW = 0.034f;
            float cellW = (1f - labelW) / total;

            var shiftRow = MatrixRow(container.transform, "ShiftHead", 0f, 24f);
            var hourRow = MatrixRow(container.transform, "HourHead", 24f, 16f);
            var tickRow = MatrixRow(container.transform, "TimeHead", 40f, 14f);
            var depRow = MatrixRow(container.transform, "LaneDep", 54f, 30f);
            var arrRow = MatrixRow(container.transform, "LaneArr", 84f, 30f);

            PlaneIcon(depRow, "DepIcon", true);
            PlaneIcon(arrRow, "ArrIcon", false);

            int col = 0;
            for (int b = 0; b < 4; b++)
            {
                int n = grids[b].Count;
                float x0 = labelW + col * cellW;
                float x1 = labelW + (col + n) * cellW;
                col += n;

                UIFactory.ClampSingleLine(UIFactory.Label(shiftRow, "Shift" + b,
                    $"{ScheduleSlots.SlotBandName(b)}班　{BandRangeText(b)}",
                    9, TextAlignmentOptions.Center,
                    new UIFactory.Anchor(new Vector2(x0, 0f), new Vector2(x1, 1f),
                        Vector2.zero, Vector2.zero),
                    UITheme.TextPrimary, FontStyles.Bold));

                int hours = n / 4;
                for (int hi = 0; hi < hours; hi++)
                {
                    float hx0 = x0 + hi * 4 * cellW;
                    float hx1 = hx0 + 4 * cellW;
                    // 小时头按**这一格真实的刻**取整点，不按 band 起始小时推算 ——
                    // 万一某段多出/少了一个刻，表头也不会与刻度错位。
                    int hour = ScheduleSlots.ParseClock(grids[b][hi * 4]) / 60;

                    UIFactory.ClampSingleLine(UIFactory.Label(hourRow, "H" + b + "_" + hi,
                        $"{hour:00}:00", 8, TextAlignmentOptions.Center,
                        new UIFactory.Anchor(new Vector2(hx0, 0f), new Vector2(hx1, 1f),
                            Vector2.zero, Vector2.zero),
                        new Color(0.847f, 0.906f, 0.988f, 1f)));

                    for (int q = 0; q < 4; q++)
                    {
                        float tx0 = hx0 + q * cellW;
                        UIFactory.Label(tickRow, "T" + b + "_" + hi + "_" + q,
                            (q * 15).ToString("00"), 7, TextAlignmentOptions.Center,
                            new UIFactory.Anchor(new Vector2(tx0, 0f), new Vector2(tx0 + cellW, 1f),
                                Vector2.zero, Vector2.zero),
                            new Color(0.773f, 0.847f, 0.953f, 1f));
                    }
                }

                for (int k = 0; k < n; k++)
                {
                    float cx0 = x0 + k * cellW;
                    float cx1 = cx0 + cellW;
                    float pad = cellW * 0.10f;

                    SlotCell(depRow, ap, b, grids[b][k], k, chips[b][k], true, cx0 + pad, cx1 - pad);
                    SlotCell(arrRow, ap, b, grids[b][k], k, chips[b][k], false, cx0 + pad, cx1 - pad);
                }
            }
        }

        static string BandRangeText(int band)
        {
            int s = ScheduleSlots.BandHourStart(band);
            int e = ScheduleSlots.BandHourEnd(band);
            return $"{s:00}:00–{e:00}:45";
        }

        /// <summary>
        /// 矩阵里某一段要画哪些 15 分钟刻。
        ///
        /// <see cref="ScheduleSlots.BandGridLabels"/> 是按 <c>BandOfDep</c> 的整点归属铺满全天的，
        /// 早段因此含 00:00–05:45 共 24 个刻 —— 而容量表在这些小时恒为 0（见
        /// <see cref="ScheduleSlots.BandHourStart"/> 的说明）。若照原样画，矩阵开头会多出
        /// 六小时「未公布容量」的空格，看着像数据缺失。故按
        /// <see cref="ScheduleSlots.BandHourStart"/> / <see cref="ScheduleSlots.BandHourEnd"/>
        /// 裁到**真正有容量的整点区间**：早 06–09 / 中 10–14 / 晚 15–19 / 夜 20–23。
        /// </summary>
        static List<string> MatrixGrid(int band, List<IndustryAirlineLogic.SlotChip> chips)
        {
            var full = ScheduleSlots.BandGridLabels(band);
            int h0 = ScheduleSlots.BandHourStart(band);
            int h1 = ScheduleSlots.BandHourEnd(band);
            var list = new List<string>(full.Count);
            for (int i = 0; i < full.Count; i++)
            {
                int minutes = ScheduleSlots.ParseClock(full[i]);
                if (minutes < 0) continue;
                int hour = minutes / 60;
                if (hour >= h0 && hour <= h1) { list.Add(full[i]); continue; }
                // 00–05 时通常没有容量；万一某机场真公布了，也要画出来 ——
                // 宁可多一列，不能少一条还能申请的槽。
                for (int k = 0; k < chips.Count; k++)
                {
                    if (string.Equals(chips[k].time, full[i]))
                    {
                        list.Add(full[i]);
                        break;
                    }
                }
            }
            return list;
        }

        static RectTransform MatrixRow(Transform parent, string name, float yTop, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UIFactory.PinFromTop(go, yTop, height, 0f, 1f);
            return go.GetComponent<RectTransform>();
        }

        static void PlaneIcon(RectTransform lane, string name, bool takeoff)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(lane, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.004f, 0.08f);
            rt.anchorMax = new Vector2(0.030f, 0.92f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.sprite = takeoff ? UIFactory.TakeoffSprite : UIFactory.LandingSprite;
            img.preserveAspect = true;
            img.color = new Color(0.945f, 0.969f, 1f, 1f);
            img.raycastTarget = false;
        }

        /// <summary>
        /// 一个 15 分钟方块。四种状态与原型一致：可申请（绿·写余量）/ 我司（蓝）/ 已满（灰·「满」）
        /// / 宵禁（紫·「禁」）。第五种是原型没有、我们数据里真有的：**该刻未公布容量**（「—」）——
        /// `ChipsOf` 只吐「已公布且可持有」的刻，剩下的刻画成占用 0 的方块等于凭空造了 45% 的噪声，
        /// 故明写「—」并标注在图例里。
        /// </summary>
        void SlotCell(RectTransform lane, AirportInfo ap, int band, string clock, int index,
            IndustryAirlineLogic.SlotChip chip, bool departure, float x0, float x1)
        {
            bool published = chip.capacity > 0;
            bool curfew = published && AirportCatalog.InCurfew(ap.code, ScheduleSlots.ParseClock(clock));
            int free = published ? (departure ? chip.freeLeft : chip.arrFreeLeft) : 0;
            bool mine = published && (departure ? chip.mineHeld > 0 : chip.arrMine);

            string text;
            Color fg;
            Color bg;
            if (!published) { text = "—"; fg = SlotNa; bg = SlotBg; }
            else if (curfew) { text = "禁"; fg = SlotCurfew; bg = SlotCurfewBg; }
            else if (mine) { text = free > 0 ? free.ToString() : "满"; fg = Color.white; bg = SlotMine; }
            else if (free > 0) { text = free.ToString(); fg = SlotGreen; bg = SlotBg; }
            else { text = "满"; fg = SlotFull; bg = SlotBg; }

            string name = (departure ? "SlotDep_b" : "SlotArr_b") + band + "_" + index;
            var anchor = new UIFactory.Anchor(new Vector2(x0, 0.14f), new Vector2(x1, 0.86f),
                Vector2.zero, Vector2.zero);

            if (!published)
            {
                // 未公布的刻不给按钮：没有可查的东西，做成可点会让人以为「点了能申请」。
                var plain = UIFactory.Panel(lane, name, anchor, bg, rounded: false);
                var plainImg = plain.GetComponent<Image>();
                if (plainImg != null) plainImg.raycastTarget = false;
                UIFactory.Label(plain.transform, "L", text, 7, TextAlignmentOptions.Center,
                    UIFactory.Fill, fg);
                return;
            }

            var btn = UIFactory.Button(lane, name, text, anchor, bg, () => OpenDetail(ap, band, clock, chip), 7);
            var label = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                label.color = fg;
                label.fontStyle = mine || free > 0 ? FontStyles.Bold : FontStyles.Normal;
            }
        }

        static IndustryAirlineLogic.SlotChip[] AlignChips(
            List<IndustryAirlineLogic.SlotChip> chips, List<string> grid)
        {
            var aligned = new IndustryAirlineLogic.SlotChip[grid.Count];
            int k = 0;
            for (int i = 0; i < grid.Count; i++)
            {
                while (k < chips.Count && string.CompareOrdinal(chips[k].time, grid[i]) < 0) k++;
                if (k < chips.Count && string.CompareOrdinal(chips[k].time, grid[i]) == 0)
                    aligned[i] = chips[k++];
            }
            return aligned;
        }

        void OpenDetail(AirportInfo ap, int band, string clock, IndustryAirlineLogic.SlotChip chip)
        {
            AirportSlotDetailModal.Open(OverlayRoot(), ap.code, ap.nameZh, band, clock, chip);
        }

        /// <summary>
        /// 弹窗必须挂在画布级：挂在页面里会被卡片区 / ScrollRect 裁掉。
        /// 取不到 Canvas 时（隔离用例里常见）退回最顶层节点，行为一致。
        /// </summary>
        Transform OverlayRoot()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null) return canvas.transform;
            Transform t = transform;
            while (t.parent != null) t = t.parent;
            return t;
        }

        // ==================== 过滤器 / 排序面板 ====================

        void ToggleFilterPanel()
        {
            if (_filterPanel != null)
            {
                Destroy(_filterPanel);
                _filterPanel = null;
                return;
            }
            CloseSortPanel();
            _pTier = _filter.tier;
            _pMine = _filter.mineOnly;
            _pOpen = _filter.openMode;
            _pSort = _sortMode;
            BuildFilterPanel();
        }

        void BuildFilterPanel()
        {
            var panel = UIFactory.Panel(_root, "SlotFilterPanel",
                new UIFactory.Anchor(new Vector2(0.305f, 0.335f), new Vector2(0.600f, 0.872f),
                    Vector2.zero, Vector2.zero),
                PopupBg);
            UIFactory.AddBorder(panel, new Color(0.208f, 0.329f, 0.463f, 1f), new Vector2(1.2f, -1.2f));
            panel.transform.SetAsLastSibling();
            _filterPanel = panel;

            float y = 6f;
            UIFactory.ClampSingleLine(UIFactory.Label(PanelRow(panel.transform, "Help", y, 18f),
                "HelpLabel", "勾选后点确定", 9, TextAlignmentOptions.MidlineRight,
                UIFactory.Fill, new Color(0.561f, 0.659f, 0.776f, 1f)));
            y += 20f;

            y = OptionGroup(panel.transform, y, "机场等级", new[]
            {
                ("全部机场", _pTier < 0, (UnityEngine.Events.UnityAction)(() => SetTier(-1))),
                ("只有枢纽", _pTier == 0, (UnityEngine.Events.UnityAction)(() => SetTier(0))),
                ("只有干线", _pTier == 1, (UnityEngine.Events.UnityAction)(() => SetTier(1))),
            });
            y = OptionGroup(panel.transform, y, "范围", new[]
            {
                ("全部机场", !_pMine, (UnityEngine.Events.UnityAction)(() => SetMine(false))),
                ("只有我司航网", _pMine, (UnityEngine.Events.UnityAction)(() => SetMine(true))),
            });
            y = OptionGroup(panel.transform, y, "余量", new[]
            {
                ("不限", _pOpen == 0, (UnityEngine.Events.UnityAction)(() => SetOpen(0))),
                ("只有仍有空位", _pOpen == 1, (UnityEngine.Events.UnityAction)(() => SetOpen(1))),
                ("只有池已满", _pOpen == 2, (UnityEngine.Events.UnityAction)(() => SetOpen(2))),
            });
            y = OptionGroup(panel.transform, y, "排序", new[]
            {
                (SortNames[0], _pSort == 0, (UnityEngine.Events.UnityAction)(() => SetPendingSort(0))),
                (SortNames[1], _pSort == 1, (UnityEngine.Events.UnityAction)(() => SetPendingSort(1))),
                (SortNames[2], _pSort == 2, (UnityEngine.Events.UnityAction)(() => SetPendingSort(2))),
                (SortNames[3], _pSort == 3, (UnityEngine.Events.UnityAction)(() => SetPendingSort(3))),
            });

            var actions = PanelRow(panel.transform, "Actions", y + 4f, 32f);
            UIFactory.Button(actions, "Reset", "重置",
                new UIFactory.Anchor(new Vector2(0f, 0f), new Vector2(0.47f, 1f),
                    Vector2.zero, Vector2.zero),
                new Color(0.114f, 0.220f, 0.357f, 1f), ResetFilters, 11);
            UIFactory.Button(actions, "Apply", "确定",
                new UIFactory.Anchor(new Vector2(0.53f, 0f), new Vector2(1f, 1f),
                    Vector2.zero, Vector2.zero),
                UITheme.Accent, ApplyFilters, 11);
        }

        void SetTier(int tier) { _pTier = tier; RebuildFilterPanel(); }
        void SetMine(bool mine) { _pMine = mine; RebuildFilterPanel(); }
        void SetOpen(int mode) { _pOpen = mode; RebuildFilterPanel(); }
        void SetPendingSort(int mode) { _pSort = mode; RebuildFilterPanel(); }

        /// <summary>勾选只改待确定状态，面板整体重建（选项不多，重建比逐个改色简单且不会漏）。</summary>
        void RebuildFilterPanel()
        {
            if (_filterPanel == null) return;
            Destroy(_filterPanel);
            _filterPanel = null;
            BuildFilterPanel();
        }

        void ApplyFilters()
        {
            _filter.tier = _pTier;
            _filter.mineOnly = _pMine;
            _filter.openMode = _pOpen;
            _sortMode = _pSort;
            _filter.sortByOpen = _sortMode == 1;

            CloseFilterPanel();
            UpdateFilterButtonText();
            UpdateSortButtonText();
            RebuildCards(false);   // 换了条件回顶部，免得停在一段空列表中间
        }

        void ResetFilters()
        {
            _pTier = -1;
            _pMine = false;
            _pOpen = 0;
            _pSort = 0;
            ApplyFilters();
        }

        void CloseFilterPanel()
        {
            if (_filterPanel == null) return;
            Destroy(_filterPanel);
            _filterPanel = null;
        }

        void ToggleSortPanel()
        {
            if (_sortPanel != null)
            {
                Destroy(_sortPanel);
                _sortPanel = null;
                return;
            }
            CloseFilterPanel();
            BuildSortPanel();
        }

        void BuildSortPanel()
        {
            var panel = UIFactory.Panel(_root, "SlotSortPanel",
                new UIFactory.Anchor(new Vector2(0.182f, 0.742f), new Vector2(0.335f, 0.872f),
                    Vector2.zero, Vector2.zero),
                PopupBg);
            UIFactory.AddBorder(panel, new Color(0.208f, 0.329f, 0.463f, 1f), new Vector2(1.2f, -1.2f));
            panel.transform.SetAsLastSibling();
            _sortPanel = panel;

            float y = 5f;
            for (int i = 0; i < SortNames.Length; i++)
            {
                int mode = i;
                var row = PanelRow(panel.transform, "Sort" + i, y, 22f, 0.03f, 0.97f);
                UIFactory.Button(row, "B", SortNames[i],
                    new UIFactory.Anchor(Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
                    _sortMode == mode ? new Color(0.161f, 0.376f, 0.722f, 1f)
                                      : new Color(0.110f, 0.204f, 0.337f, 1f),
                    () => PickSort(mode), 10);
                y += 24f;
            }
        }

        void PickSort(int mode)
        {
            _sortMode = mode;
            _filter.sortByOpen = mode == 1;
            CloseSortPanel();
            UpdateSortButtonText();
            UpdateFilterButtonText();
            RebuildCards(false);
        }

        void CloseSortPanel()
        {
            if (_sortPanel == null) return;
            Destroy(_sortPanel);
            _sortPanel = null;
        }

        void UpdateSortButtonText()
        {
            if (_sortText != null) _sortText.text = SortNames[_sortMode];
        }

        void UpdateFilterButtonText()
        {
            if (_filterText == null) return;
            bool active = !_filter.IsDefault || _sortMode != 0;
            _filterText.text = active ? "过滤器 ▼ ●" : "过滤器 ▼";
            var btn = _filterText.transform.parent != null
                ? _filterText.transform.parent.GetComponent<Image>()
                : null;
            if (btn != null)
                btn.color = active ? new Color(0.133f, 0.259f, 0.420f, 1f)
                                   : UITheme.PanelDeep;
        }

        static RectTransform PanelRow(Transform parent, string name, float yTop, float height,
            float x0 = 0.04f, float x1 = 0.96f)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UIFactory.PinFromTop(go, yTop, height, x0, x1);
            return go.GetComponent<RectTransform>();
        }

        /// <summary>一组选项（标题 + N 个整行按钮），返回下一组的起始 y。</summary>
        float OptionGroup(Transform parent, float yTop, string title,
            (string label, bool active, UnityEngine.Events.UnityAction onClick)[] options)
        {
            var header = PanelRow(parent, "H_" + title, yTop, 16f);
            UIFactory.ClampSingleLine(UIFactory.Label(header, "T", title, 10,
                TextAlignmentOptions.MidlineLeft, UIFactory.Fill, UITheme.TextPrimary));
            float y = yTop + 17f;

            for (int i = 0; i < options.Length; i++)
            {
                var row = PanelRow(parent, "O_" + title + "_" + i, y, 21f);
                var btn = UIFactory.Button(row, "B", (options[i].active ? "✓ " : "　 ") + options[i].label,
                    new UIFactory.Anchor(Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
                    options[i].active ? new Color(0.161f, 0.376f, 0.722f, 1f)
                                      : new Color(0.110f, 0.204f, 0.337f, 0.55f),
                    options[i].onClick, 10);
                var lbl = btn.GetComponentInChildren<TextMeshProUGUI>();
                if (lbl != null) lbl.alignment = TextAlignmentOptions.MidlineLeft;
                y += 23f;
            }
            return y + 2f;
        }
    }
}

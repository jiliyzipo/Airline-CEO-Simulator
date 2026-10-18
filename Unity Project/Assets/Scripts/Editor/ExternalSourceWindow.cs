#if UNITY_EDITOR
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using UnityEditor;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// 外部数据源配置窗（编辑器）：看状态、改地址、手动拉一轮。
    /// 运行时同一份配置存在 PlayerPrefs，游戏内改法与这里等价。
    /// </summary>
    public class ExternalSourceWindow : EditorWindow
    {
        string _url;
        bool _enabled;
        int _interval;
        int _maxPerFetch;
        bool _filter;
        Vector2 _scroll;
        double _pollUntil;
        string _baseline;
        string _pollNote = "";

        [MenuItem("AirlineCEO/外部数据源/打开配置窗口", false, 81)]
        public static void Open()
        {
            var w = GetWindow<ExternalSourceWindow>("外部数据源");
            w.minSize = new Vector2(560, 460);
            w.Reload();
            w.Show();
        }

        void Reload()
        {
            _enabled = ExternalDataFetcher.Enabled;
            _url = ExternalDataFetcher.SourceUrl;
            _interval = ExternalDataFetcher.IntervalSeconds;
            _maxPerFetch = ExternalDataFetcher.MaxPerFetch;
            _filter = ExternalDataFetcher.RelevanceFilter;
        }

        void Update()
        {
            if (_pollUntil <= 0) return;
            if (EditorApplication.timeSinceStartup > _pollUntil)
            {
                _pollUntil = 0;
                _pollNote = "（等待超时：源可能不可达，游戏内只会静默跳过）";
                Repaint();
                return;
            }
            if (!string.IsNullOrEmpty(ExternalDataFetcher.LastStatus)
                && ExternalDataFetcher.LastStatus != _baseline)
            {
                _pollUntil = 0;
                _pollNote = "";
                if (Application.isPlaying && GameState.Instance != null)
                {
                    int n = ExternalEventManager.DrainSource(GameState.Instance);
                    _pollNote = n > 0 ? $"已入队 {n} 条，下一拍写进收件箱。" : "本轮无新内容。";
                }
                Repaint();
            }
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("外部数据源（现实提供内容，游戏提供时间轴）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "支持三类源，可用「;」分隔多个：\n" +
                "① http(s) JSON：{\"events\":[{id,title,content,source,category,publishedAt,\n" +
                "    effectType,effectValue,affectedRegion,durationHours,chainId}]}\n" +
                "   effectType 可填 fuelPrice / fuelAbs / loadFactor / onTime / cash / satisfaction /\n" +
                "   freezeFlights / compliance；不填则只作内容，不改数值。\n" +
                "② http(s) RSS / Atom：通讯社消息稿，只作内容。\n" +
                "③ 本地文件：绝对路径或 file://（离线自测；相对路径按 StreamingAssets 解析）。",
                MessageType.None);

            EditorGUILayout.Space();
            _enabled = EditorGUILayout.ToggleLeft("启用自动拉取", _enabled);

            EditorGUILayout.LabelField("数据源地址（多个用 ; 分隔）");
            _url = EditorGUILayout.TextArea(_url ?? "", GUILayout.MinHeight(48));

            EditorGUILayout.BeginHorizontal();
            _interval = EditorGUILayout.IntField("拉取间隔（现实秒，0=只手动）", _interval);
            _maxPerFetch = EditorGUILayout.IntField("每轮最多采纳条数", _maxPerFetch);
            EditorGUILayout.EndHorizontal();
            _filter = EditorGUILayout.ToggleLeft("关键词过滤（滤掉股市行情类噪声，只留民航相关）", _filter);

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("保存配置", GUILayout.Height(24)))
            {
                ExternalDataFetcher.Enabled = _enabled;
                ExternalDataFetcher.SourceUrl = _url;
                ExternalDataFetcher.IntervalSeconds = _interval;
                ExternalDataFetcher.MaxPerFetch = _maxPerFetch;
                ExternalDataFetcher.RelevanceFilter = _filter;
                Reload();
            }
            if (GUILayout.Button("恢复默认源", GUILayout.Height(24)))
            {
                ExternalDataFetcher.SourceUrl = ExternalDataFetcher.DefaultUrl;
                ExternalDataFetcher.IntervalSeconds = ExternalDataFetcher.DefaultIntervalSeconds;
                ExternalDataFetcher.MaxPerFetch = ExternalDataFetcher.DefaultMaxPerFetch;
                ExternalDataFetcher.RelevanceFilter = true;
                ExternalDataFetcher.Enabled = true;
                Reload();
            }
            if (GUILayout.Button("清空缓存与去重表", GUILayout.Height(24)))
            {
                ExternalDataFetcher.Reset();
                _pollNote = "已清空。";
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("状态", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(string.IsNullOrEmpty(ExternalDataFetcher.LastStatus)
                ? "尚未拉取。" : ExternalDataFetcher.LastStatus);
            if (!string.IsNullOrEmpty(_pollNote)) EditorGUILayout.LabelField(_pollNote);
            EditorGUILayout.LabelField(Application.isPlaying ? "Play 模式：可拉取并入库。" : "非 Play 模式：可拉取验证解析，但不会进入游戏。");

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("立即拉取一轮并入库", GUILayout.Height(28)))
                RefreshNow();
            if (GUILayout.Button("注入演示载荷（离线）", GUILayout.Height(28)))
                InjectDemo();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("说明", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "• 拉取在后台线程完成，失败只静默返回，不打印 Unity 红字，也不阻塞游戏。\n" +
                "• 同一条内容（按 id / 链接去重）只会进一次收件箱；每轮按时间倒序取最新若干条。\n" +
                "• 收到的现实日期会被换成相对措辞（明日零时 / 今日 / 3天后），游戏里只出现游戏时间。\n" +
                "• 游戏内同样能改：底栏齿轮 →「设置 / 存档」→「外部信息源」（读写的就是这份配置）。",
                MessageType.None);

            EditorGUILayout.EndScrollView();
        }

        void RefreshNow()
        {
            _baseline = ExternalDataFetcher.LastStatus;
            _pollNote = "拉取中……";
            _pollUntil = EditorApplication.timeSinceStartup + 20.0;
            ExternalDataFetcher.RequestRefresh(true);
        }

        void InjectDemo()
        {
            ExternalDataFetcher.Inject(ExternalDataFetcher.DemoPayloads());
            if (Application.isPlaying && GameState.Instance != null)
            {
                int n = ExternalEventManager.DrainSource(GameState.Instance);
                _pollNote = $"演示载荷已入队 {n} 条。";
            }
            else
            {
                _pollNote = "已投递到缓存；进入 Play 模式后由游戏时钟入库。";
            }
            Repaint();
        }
    }
}
#endif

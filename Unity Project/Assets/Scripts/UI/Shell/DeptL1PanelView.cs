using AirlineCEO.Data;
using AirlineCEO.UI.Framework;
using TMPro;
using UnityEngine;

namespace AirlineCEO.UI.Shell
{
    /// <summary>部门 L1 面板（以运营部为模板）。</summary>
    public class DeptL1PanelView : ScrollListPanel
    {
        [SerializeField] TextMeshProUGUI bodyLabel;
        [SerializeField] TextMeshProUGUI metaLabel;
        [SerializeField] TextMeshProUGUI codeLabel;

        public string DepartmentCode { get; private set; }

        public void ApplyConfig(UIConfig cfg, string deptCode = null)
        {
            DepartmentCode = deptCode ?? GuessCode(cfg);
            BindPage(cfg != null ? cfg.pageId : PageId, cfg != null ? cfg.title : null);
            if (metaLabel != null && cfg != null) metaLabel.text = cfg.meta ?? "";
            if (codeLabel != null) codeLabel.text = DepartmentCode;
            if (bodyLabel != null)
            {
                bodyLabel.text =
                    $"【{cfg?.title ?? "部门"}】\n\n" +
                    "本页为 Phase 1 Prefab 骨架。\n" +
                    "后续 Phase 2 将按白板补齐子屏、列表与决策流。\n\n" +
                    $"pageId: {cfg?.pageId}\n" +
                    $"tabGroup: {cfg?.tabGroup}\n" +
                    $"badge: {cfg?.badge}";
            }
        }

        public void SetBody(string text)
        {
            if (bodyLabel != null) bodyLabel.text = text;
        }

        public void AssignExtraRefs(TextMeshProUGUI body, TextMeshProUGUI meta, TextMeshProUGUI code)
        {
            bodyLabel = body;
            metaLabel = meta;
            codeLabel = code;
        }

        static string GuessCode(UIConfig cfg)
        {
            if (cfg == null || string.IsNullOrEmpty(cfg.pageId)) return "";
            if (cfg.pageId.Contains("运营")) return "OPS";
            if (cfg.pageId.Contains("市场")) return "MKT";
            if (cfg.pageId.Contains("财务")) return "FIN";
            if (cfg.pageId.Contains("战略")) return "STG";
            if (cfg.pageId.Contains("机务")) return "MNT";
            if (cfg.pageId.Contains("人力")) return "HR";
            if (cfg.pageId.Contains("安全")) return "SAF";
            return cfg.tabGroup?.ToUpperInvariant() ?? "";
        }
    }
}

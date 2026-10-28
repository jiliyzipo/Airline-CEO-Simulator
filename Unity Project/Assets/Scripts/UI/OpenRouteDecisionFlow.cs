using System;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.UI
{
    /// <summary>
    /// 开通航线：选机场后提交开航申请，运营部召集各部门会商，把意见发到收件箱请 CEO 决策。
    /// </summary>
    public class OpenRouteDecisionFlow : MonoBehaviour
    {
        Transform _canvas;
        RPMCalculator.RPMOutput _previewRpm;
        Action _onStateChanged;

        public static OpenRouteDecisionFlow Ensure(Transform canvasRoot)
        {
            if (canvasRoot == null) return null;
            var existing = canvasRoot.GetComponentInChildren<OpenRouteDecisionFlow>(true);
            if (existing != null)
            {
                if (existing._canvas == null) existing._canvas = canvasRoot;
                return existing;
            }
            var go = new GameObject("OpenRouteDecisionFlow", typeof(RectTransform));
            go.transform.SetParent(canvasRoot, false);
            UIFactory.ApplyAnchor(go.GetComponent<RectTransform>(), UIFactory.Fill);
            var img = go.AddComponent<Image>();
            img.color = new Color(0, 0, 0, 0);
            img.raycastTarget = false;
            var comp = go.AddComponent<OpenRouteDecisionFlow>();
            comp._canvas = canvasRoot;
            return comp;
        }

        public void Bind(Action onStateChanged, Action<string> openOpsPage = null, Action<int> gotoNav = null)
        {
            _onStateChanged = onStateChanged;
            _canvas = transform.parent;
        }

        /// <summary>
        /// 提交开航研究申请。正文与准入判据全部在 <see cref="OpenRouteStudyLogic"/> 里 ——
        /// 命令台说「开通北京—纽约」走的也是那一份，两个入口不会对同一条航线给出两种说法。
        /// </summary>
        public string SubmitStudy(OpenRouteModalUI.Selection sel)
        {
            var plan = OpenRouteStudyLogic.Build(GameState.Instance, sel.originCode, sel.destCode, sel.viaCode);
            if (!plan.ok) return plan.reject;
            _previewRpm = plan.rpm;
            InboxManager.QueueOpenRouteStudy(GameState.Instance, sel.originCode, sel.destCode, sel.viaCode,
                plan.subject, plan.body);
            _onStateChanged?.Invoke();
            return plan.path;
        }

        public void BeginAfterAirportPick(OpenRouteModalUI.Selection sel)
        {
            SubmitStudy(sel);
        }

        public void BeginFromMap(string originCode, string destCode)
        {
            AirportCatalog.TryResolve(originCode, out var o);
            AirportCatalog.TryResolve(destCode, out var d);
            BeginAfterAirportPick(new OpenRouteModalUI.Selection
            {
                originCode = string.IsNullOrEmpty(o.code) ? originCode : o.code,
                destCode = string.IsNullOrEmpty(d.code) ? destCode : d.code,
                viaCode = null,
                originLabel = string.IsNullOrEmpty(o.Display) ? originCode : o.Display,
                destLabel = string.IsNullOrEmpty(d.Display) ? destCode : d.Display,
                viaLabel = "直飞"
            });
        }
    }
}

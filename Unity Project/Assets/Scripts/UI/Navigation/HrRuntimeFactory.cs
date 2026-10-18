using AirlineCEO.UI;
using UnityEngine;

namespace AirlineCEO.UI.Navigation
{
    /// <summary>HR 子页运行时工厂：无 Prefab 绑定时仍可打开白板页。</summary>
    public static class HrRuntimeFactory
    {
        public static GameObject TryCreate(string pageId, Transform parent)
        {
            if (string.IsNullOrEmpty(pageId)) return null;

            if (pageId.StartsWith("n114") || pageId.Contains("员工信息"))
            {
                var profile = StaffProfilePanel.Build();
                Parent(profile.gameObject, parent);
                return profile.gameObject;
            }
            if (pageId.StartsWith("n113") || pageId.Contains("部门信息"))
            {
                var info = DepartmentInfoPanel.Build();
                Parent(info.gameObject, parent);
                return info.gameObject;
            }
            if (pageId.StartsWith("n112") || pageId.Contains("职业介绍所"))
            {
                var office = RecruitmentOfficePanel.Build();
                Parent(office.gameObject, parent);
                return office.gameObject;
            }

            string kind = null;
            string title = null;
            if (pageId.StartsWith("n86") || pageId.Contains("招聘") || pageId.Contains("人力职能"))
            {
                kind = "n86";
                title = "人力职能";
            }
            else if (pageId.StartsWith("n87") || pageId.Contains("培训与发展"))
            {
                var hub = TrainingHubPanel.Build();
                Parent(hub.gameObject, parent);
                return hub.gameObject;
            }
            else if (pageId.StartsWith("n88") || pageId.Contains("绩效") || pageId.Contains("薪酬"))
            {
                kind = "n88";
                title = "绩效与薪酬管理";
            }
            else if (pageId.StartsWith("n89") || pageId.Contains("员工关系"))
            {
                kind = "n89";
                title = "员工关系与企业文化";
            }
            else if (pageId.StartsWith("n90") || pageId.Contains("合规"))
            {
                kind = "n90";
                title = "合规与风险管理";
            }
            else if (pageId.StartsWith("n37") || pageId.Contains("工会"))
            {
                kind = "n37";
                title = "工会谈判桌";
            }
            else if (pageId.StartsWith("n91") || pageId.Contains("继任"))
            {
                kind = "n91";
                title = "人才档案与继任";
            }
            // n99「招聘策略弹窗」已并入 n112 职业介绍所，不再注册独立页面。

            if (kind == null) return null;

            var panel = HrSubpagePanel.Build(kind, title);
            panel.BindPage(pageId, title);
            Parent(panel.gameObject, parent);
            return panel.gameObject;
        }

        static void Parent(GameObject go, Transform parent)
        {
            if (go == null || parent == null) return;
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) return;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
        }
    }
}

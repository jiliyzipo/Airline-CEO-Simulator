using System;

namespace AirlineCEO.Data
{
    [Serializable]
    public class LegacyIssue
    {
        public string id;
        public string title;
        public string description;
        public string system;
        public int urgency = 2;
        public bool isResolved;
        public string actionLink;

        public static string UrgencyLabel(int u) => u >= 3 ? "高" : u <= 1 ? "低" : "中";
    }

    [Serializable]
    public class LegacyIssueListSave
    {
        public LegacyIssue[] items;
        public string airlineId;
        public bool briefingShown;
    }

    [Serializable]
    public class DepartmentHeadSnapshot
    {
        public string dept;
        public string name;
        public string title;
    }
}

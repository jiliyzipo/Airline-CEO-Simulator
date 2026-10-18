using System;

namespace AirlineCEO.Data
{
    /// <summary>非总监岗位的招聘案：寻访 → 面试 → CEO 审批 → 入职通知期。</summary>
    [Serializable]
    public class StaffRecruitCase
    {
        public string id;
        public string dept;
        public string jobTitle;
        public string rank;
        /// <summary>queued / sourcing / interview / pending / onboard / done / cancelled</summary>
        public string stage = "queued";
        public string personId = "";
        public string mailId = "";
        public long startUtcTicks;
        public long stageUtcTicks;
        public int sourceDays = 14;
        public int interviewDays = 10;
        public int onboardDays = 5;
    }

    [Serializable]
    public class StaffRecruitListSave
    {
        public StaffRecruitCase[] items;
    }
}

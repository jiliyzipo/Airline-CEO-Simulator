using System;

namespace AirlineCEO.Data
{
    [Serializable]
    public class CeoDossierSave
    {
        public string gender = "男";
        public string nationality = "中国";
        public int contractYears = 4;
        public int contractStartYear = 1;
        public float weeklyPayWan = 0.12f;
        public float bonusWan = 0.4f;
    }
}

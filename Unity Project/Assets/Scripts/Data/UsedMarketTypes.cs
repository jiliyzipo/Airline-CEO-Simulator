using System;

namespace AirlineCEO.Data
{
    [Serializable]
    public class UsedListing
    {
        public string id;
        public string typeId;
        public int age;
        public float discount;
        public float askPriceWan;
        public int remainingLife;
        public bool trap;
        public bool sold;
    }

    [Serializable]
    public class UsedMarketSave
    {
        public UsedListing[] items;
        public int year;
        public int quarter;
    }
}

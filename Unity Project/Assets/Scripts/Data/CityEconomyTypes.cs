using System;

namespace AirlineCEO.Data
{
    [Serializable]
    public class CityEconomyEntry
    {
        public string iata;
        public string continentZh;
        public string countryZh;
        public string cityZh;
        public float gdpUsdBn;
        public float popMillion;
        public float paxMillion;
        public float cargoWanTon;
        public float odIndex;
    }

    [Serializable]
    public class CityEconomyDelta
    {
        public string iata;
        public float demandDrift;
    }

    [Serializable]
    public class CityEconomySave
    {
        public CityEconomyDelta[] items;
        public int tableRevision;
    }

    public struct CityEconomyView
    {
        public string iata;
        public string continentZh;
        public string countryZh;
        public string cityZh;
        public string airportZh;
        public float gdpUsdBn;
        public float popMillion;
        public float paxMillion;
        public float cargoWanTon;
        public float odIndex;
        public float liveIndex;
        public float businessIndex;
        public float tourismIndex;
        public float demandDrift;
        public bool isHub;
        public bool served;
    }
}

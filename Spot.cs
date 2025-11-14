using System;

namespace ZVClusterApp.WinForms
{
    public class Spot
    {
        public DateTime Timestamp { get; set; }
        public string DXCall { get; set; } = string.Empty;
        public string Spotter { get; set; } = string.Empty;
        public string Band { get; set; } = string.Empty;
        public int FrequencyHz { get; set; }
        public string Mode { get; set; } = string.Empty;
        public string Information { get; set; } = string.Empty;

        // Enrichment from CTY.DAT
        public string Country { get; set; } = string.Empty;
        public double DxLat { get; set; } // deg
        public double DxLon { get; set; } // deg
        public int DistanceKm { get; set; } // rounded km (legacy)
        public int DistanceMiles { get; set; } // rounded statute miles
        public int BearingShort { get; set; } // deg 0..359
        public int BearingLong { get; set; }  // deg 0..359

        public string UniqueKey => $"{DXCall}_{FrequencyHz}";
    }
}

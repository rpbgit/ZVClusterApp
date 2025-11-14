using System.Drawing;
using System.Globalization;
using System.Diagnostics;

namespace ZVClusterApp.WinForms
{
    public static class Utils
    {
        private const bool LogGeo = false;
        private const bool LogGrid = false;

        public static string FrequencyToBand(int hz)
        {
            var khz = hz / 1000; // integer kHz

            // HF bands
            if (khz >= 1800 && khz <= 2000) return "160m";
            if (khz >= 3500 && khz <= 4000) return "80m";
            if (khz >= 5330 && khz <= 5406) return "60m"; // common 60m allocations (channelized/region dependent)
            if (khz >= 7000 && khz <= 7300) return "40m";
            if (khz >= 10100 && khz <= 10150) return "30m";
            if (khz >= 14000 && khz <= 14350) return "20m";
            if (khz >= 18068 && khz <= 18168) return "17m";
            if (khz >= 21000 && khz <= 21450) return "15m";
            if (khz >= 24890 && khz <= 24990) return "12m";
            if (khz >= 28000 && khz <= 29700) return "10m";

            // VHF
            if (khz >= 50000 && khz <= 54000) return "6m";

            // Fallback: show MHz text
            return (hz / 1000000.0).ToString("0.###") + "MHz";
        }

        public static Color ColorForBand(string band) => band.ToLowerInvariant() switch
        {
            "160m" => Color.FromArgb(0x4E, 0x34, 0x2E), // brown
            "80m"  => Color.FromArgb(0x9C, 0x27, 0xB0), // purple
            "60m"  => Color.FromArgb(0x00, 0x96, 0x88), // teal
            "40m"  => Color.FromArgb(0x21, 0x96, 0xF3), // blue
            "30m"  => Color.FromArgb(0x00, 0xBC, 0xD4), // cyan
            "20m"  => Color.FromArgb(0x4C, 0xAF, 0x50), // green
            "17m"  => Color.FromArgb(0x8B, 0xC3, 0x4A), // light green
            "15m"  => Color.FromArgb(0xF2, 0xA5, 0x11), // orange
            "12m"  => Color.FromArgb(0xFF, 0xC1, 0x07), // amber
            "10m"  => Color.FromArgb(0xE6, 0x49, 0x49), // red
            "6m"   => Color.FromArgb(0x3F, 0x51, 0xB5), // indigo
            _ => Color.Gray
        };

        // Great-circle distance (km) using haversine
        public static double GcDistanceKm(double lat1Deg, double lon1Deg, double lat2Deg, double lon2Deg)
        {
            const double R = 6371.0; // km
            double lat1 = ToRad(lat1Deg);
            double lat2 = ToRad(lat2Deg);
            double dLat = ToRad(lat2Deg - lat1Deg);
            double dLon = ToRad(lon2Deg - lon1Deg);

            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(lat1) * Math.Cos(lat2) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            var km = R * c;
            if (LogGeo) Debug.WriteLine($"[Geo] DistKm ({lat1Deg},{lon1Deg}) -> ({lat2Deg},{lon2Deg}) = {km:F1} km");
            return km;
        }

        // Convert km to statute miles (rounded)
        public static int KmToMilesRounded(double km)
        {
            const double milesPerKm = 0.621371;
            return (int)Math.Round(km * milesPerKm);
        }

        // Initial great-circle bearing (degrees) from point 1 to point 2, normalized [0..360)
        public static double GcInitialBearing(double lat1Deg, double lon1Deg, double lat2Deg, double lon2Deg)
        {
            double lat1 = ToRad(lat1Deg);
            double lat2 = ToRad(lat2Deg);
            double dLon = ToRad(lon2Deg - lon1Deg);

            double y = Math.Sin(dLon) * Math.Cos(lat2);
            double x = Math.Cos(lat1) * Math.Sin(lat2) - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon);
            double brng = Math.Atan2(y, x); // radians
            var deg = Normalize360(ToDeg(brng));
            if (LogGeo) Debug.WriteLine($"[Geo] Bearing ({lat1Deg},{lon1Deg}) -> ({lat2Deg},{lon2Deg}) = {deg:F1}°");
            return deg;
        }

        // Long-path bearing from point 1 to 2 (initial bearing + 180 normalized)
        public static double GcLongPathBearing(double lat1Deg, double lon1Deg, double lat2Deg, double lon2Deg)
        {
            return Normalize360(GcInitialBearing(lat1Deg, lon1Deg, lat2Deg, lon2Deg) + 180.0);
        }

        public static double Normalize360(double deg)
        {
            deg %= 360.0;
            if (deg < 0) deg += 360.0;
            return deg;
        }

        private static double ToRad(double d) => Math.PI / 180.0 * d;
        private static double ToDeg(double r) => 180.0 / Math.PI * r;

        // Maidenhead locator (grid) to center lat/lon. Returns false if invalid.
        public static bool TryGridToLatLon(string grid, out double lat, out double lon)
        {
            lat = lon = 0;
            if (string.IsNullOrWhiteSpace(grid) || grid.Length < 4) { Debug.WriteLine($"[Grid] Invalid grid square '{grid}' (len)"); return false; }
            var g = grid.Trim().ToUpperInvariant();

            int fieldLon = g[0] - 'A';
            int fieldLat = g[1] - 'A';
            if (fieldLon < 0 || fieldLon > 17 || fieldLat < 0 || fieldLat > 17) { Debug.WriteLine($"[Grid] Invalid field '{grid}'"); return false; }
            int squareLon = g[2] - '0';
            int squareLat = g[3] - '0';
            if (squareLon < 0 || squareLon > 9 || squareLat < 0 || squareLat > 9) { Debug.WriteLine($"[Grid] Invalid grid square '{grid}'"); return false; }

            lon = -180.0 + fieldLon * 20.0 + squareLon * 2.0;
            lat = -90.0 + fieldLat * 10.0 + squareLat * 1.0;

            double lonStep = 2.0, latStep = 1.0;

            if (g.Length >= 6)
            {
                int subLon = g[4] - 'A';
                int subLat = g[5] - 'A';
                if (subLon < 0 || subLon > 23 || subLat < 0 || subLat > 23) { Debug.WriteLine($"[Grid] Invalid subsquare '{grid}'"); return false; }
                lon += subLon * (1.0 / 12.0);
                lat += subLat * (1.0 / 24.0);
                lonStep = 1.0 / 12.0; latStep = 1.0 / 24.0;
            }

            lon += lonStep / 2.0;
            lat += latStep / 2.0;
            if (LogGrid) Debug.WriteLine($"[Grid] '{grid}' -> lat={lat:F4}, lon={lon:F4}");
            return true;
        }
    }
}

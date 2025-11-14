using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ZVClusterApp.WinForms
{
    // CTY.DAT loader and callsign -> country/lat/lon lookup
    public sealed class CtyDat
    {
        public sealed class Entry
        {
            public string Country { get; init; } = string.Empty;
            public double Lat { get; init; }   // degrees
            public double Lon { get; init; }   // degrees, East-positive
        }

        private readonly Dictionary<string, Entry> _prefixMap = new(StringComparer.OrdinalIgnoreCase);
        public bool IsLoaded => _prefixMap.Count > 0;
        public int PrefixCount => _prefixMap.Count;

        private static readonly object _cacheLock = new();
        private static CtyDat? _cached;

        public static CtyDat Load(string? explicitPath = null)
        {
            Debug.WriteLine($"[CTY] Load requested. explicitPath='{explicitPath}'");

            if (string.IsNullOrWhiteSpace(explicitPath))
            {
                lock (_cacheLock)
                {
                    if (_cached != null && _cached.IsLoaded)
                    {
                        Debug.WriteLine("[CTY] Using cached CTY database.");
                        return _cached;
                    }
                }
            }

            var db = new CtyDat();
            try
            {
                string? path = explicitPath;
                if (string.IsNullOrWhiteSpace(path))
                {
                    var exeDir = Application.StartupPath;
                    string[] probes = { Path.Combine(exeDir, "cty.dat") };
                    Debug.WriteLine($"[CTY] Probing: {string.Join(" | ", probes)}");
                    path = probes.FirstOrDefault(File.Exists);

                    if (string.IsNullOrWhiteSpace(path))
                    {
                        var target = Path.Combine(exeDir, "cty.dat"); // put it in the exe dir if we need to download it.

                        if (TryDownloadCtyDat(target))
                        {
                            path = target;
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    Debug.WriteLine("[CTY] CTY.DAT not found (and download failed). Operating without CTY support.");
                    lock (_cacheLock)
                    {
                        if (_cached != null && _cached.IsLoaded) return _cached;
                    }
                    return db;
                }

                Debug.WriteLine($"[CTY] Using file: {path}");
                var lines = File.ReadAllLines(path);
                var block = new List<string>();
                int recordCount = 0;
                foreach (var raw in lines)
                {
                    var line = raw.Trim();
                    if (string.IsNullOrEmpty(line)) continue;
                    block.Add(line);
                    if (line.EndsWith(";"))
                    {
                        db.ParseRecord(string.Join(" ", block));
                        block.Clear();
                        recordCount++;
                    }
                }
                if (block.Count > 0)
                {
                    db.ParseRecord(string.Join(" ", block));
                    recordCount++;
                }
                Debug.WriteLine($"[CTY] Load complete. Records={recordCount}, Prefixes={db._prefixMap.Count}");

                if (db.IsLoaded)
                {
                    lock (_cacheLock) { _cached = db; }
                }
                else
                {
                    lock (_cacheLock)
                    {
                        if (_cached != null && _cached.IsLoaded)
                        {
                            Debug.WriteLine("[CTY] Parse produced no prefixes; falling back to cached CTY database.");
                            return _cached;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CTY] Load failed: {ex.Message}");
                lock (_cacheLock)
                {
                    if (_cached != null && _cached.IsLoaded) return _cached;
                }
            }
            return db;
        }

        private static bool TryDownloadCtyDat(string targetPath)
        {
            try
            {
                string[] uris =
                {
                    "https://www.country-files.com/cty/cty.dat",
                };

                using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true })
                { Timeout = TimeSpan.FromSeconds(10) };

                foreach (var uri in uris)
                {
                    try
                    {
                        Debug.WriteLine($"[CTY] Attempting download: {uri}");
                        var bytes = http.GetByteArrayAsync(uri).GetAwaiter().GetResult();
                        if (bytes != null && bytes.Length > 1024)
                        {
                            File.WriteAllBytes(targetPath, bytes);
                            Debug.WriteLine($"[CTY] Downloaded {bytes.Length} bytes to '{targetPath}'");
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[CTY] Download failed from {uri}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CTY] Download attempt failed: {ex.Message}");
            }
            return false;
        }

        private static string CleanSpaces(string s) => Regex.Replace(s, "\\s+", " ").Trim();

        private static bool TryParseCoord(string s, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            double sign = 1;
            if (s.EndsWith("N", StringComparison.OrdinalIgnoreCase)) { s = s[..^1]; sign = 1; }
            else if (s.EndsWith("S", StringComparison.OrdinalIgnoreCase)) { s = s[..^1]; sign = -1; }
            else if (s.EndsWith("E", StringComparison.OrdinalIgnoreCase)) { s = s[..^1]; sign = 1; }
            else if (s.EndsWith("W", StringComparison.OrdinalIgnoreCase)) { s = s[..^1]; sign = -1; }
            s = s.Trim('+', ' ');
            if (double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
            {
                value = v * sign;
                return true;
            }
            return false;
        }

        private static IEnumerable<string> SplitCommaTokensIgnoringParens(string text)
        {
            var parts = new List<string>();
            int depth = 0;
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == '(') depth++;
                else if (ch == ')') depth = Math.Max(0, depth - 1);
                else if (ch == ',' && depth == 0)
                {
                    parts.Add(text.Substring(start, i - start).Trim());
                    start = i + 1;
                }
            }
            if (start < text.Length) parts.Add(text.Substring(start).Trim());
            return parts;
        }

        private void ParseRecord(string record)
        {
            try
            {
                int colon = record.IndexOf(':');
                if (colon <= 0) return;

                var countryName = CleanSpaces(record.Substring(0, colon));
                var rhs = record.Substring(colon + 1);
                // Expect colon-delimited metadata: CQ: ITU: Cont: Lat: Lon: UTC: MainPrefix: PrefixList;
                var cols = rhs.Split(':');
                if (cols.Length < 7) return;

                string latStr = cols[3].Trim();
                string lonStr = cols[4].Trim();
                string mainPrefix = cols[6].Trim();
                string remainder = cols.Length > 7 ? string.Join(":", cols.Skip(7)).Trim() : string.Empty;

                // Parse coords from CTY.DAT, which uses West-positive and East-negative longitudes.
                // Convert to East-positive convention used by our geo helpers by negating longitude.
                double lat = 0, lon = 0;
                if (!TryParseCoord(latStr, out lat))
                {
                    Debug.WriteLine($"[CTY] Warning: lat parse failed for '{countryName}' value='{latStr}'");
                }
                if (!TryParseCoord(lonStr, out lon))
                {
                    Debug.WriteLine($"[CTY] Warning: lon parse failed for '{countryName}' value='{lonStr}'");
                }
                lon = -lon; // Convert to East-positive

                var entry = new Entry { Country = countryName, Lat = lat, Lon = lon };

                // Add the main prefix as a key as well
                if (!string.IsNullOrWhiteSpace(mainPrefix))
                {
                    var key = mainPrefix.Trim().Trim('*');
                    key = key.TrimEnd(';').Trim();
                    if (key.Length > 0) SafeAddPrefix(key.ToUpperInvariant(), entry);
                }

                if (remainder.EndsWith(";")) remainder = remainder[..^1];

                foreach (var rawToken in SplitCommaTokensIgnoringParens(remainder))
                {
                    var token = rawToken.Trim();
                    if (string.IsNullOrEmpty(token)) continue;
                    token = token.TrimEnd(';').Trim();

                    var basePart = token;
                    int paren = token.IndexOf('(');
                    if (paren >= 0) basePart = token.Substring(0, paren).Trim();
                    int br = basePart.IndexOf('[');
                    if (br >= 0) basePart = basePart.Substring(0, br).Trim();
                    if (basePart.StartsWith("=")) basePart = basePart.Substring(1);
                    if (basePart.StartsWith("!")) continue; // explicit exclusion
                    basePart = basePart.Trim().Trim('*');

                    if (string.IsNullOrWhiteSpace(basePart)) continue;

                    // Expand simple numeric suffix ranges: e.g., K0-7
                    var m = Regex.Match(basePart, @"^([A-Z0-9/]+?)(\d)-(\d)$", RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        var stem = m.Groups[1].Value;
                        int a = m.Groups[2].Value[0] - '0';
                        int b = m.Groups[3].Value[0] - '0';
                        if (a > b) (a, b) = (b, a);
                        for (int d = a; d <= b; d++)
                        {
                            var pfx = (stem + d.ToString()).ToUpperInvariant();
                            SafeAddPrefix(pfx, entry);
                        }
                    }
                    else
                    {
                        var pfx = basePart.ToUpperInvariant();
                        SafeAddPrefix(pfx, entry);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CTY] ParseRecord error: {ex.Message}");
            }
        }

        private void SafeAddPrefix(string prefix, Entry e)
        {
            _prefixMap[prefix] = e;
        }

        public Entry? Lookup(string call)
        {
            if (string.IsNullOrWhiteSpace(call) || _prefixMap.Count == 0)
            {
                Debug.WriteLine($"[CTY] Lookup skipped. call='{call}', loaded={_prefixMap.Count}");
                return null;
            }

            string norm = call.Trim().ToUpperInvariant();
            Debug.WriteLine($"[CTY] Lookup '{norm}'...");

            if (TryLongestPrefixMatch(norm, out var matched, out var found) && found != null)
            {
                Debug.WriteLine($"[CTY] Match direct: prefix='{matched}', country='{found.Country}', lat={found.Lat}, lon={found.Lon}");
                return found;
            }

            var slash = norm.IndexOf('/');
            if (slash > 0)
            {
                var left = norm[..slash];
                if (TryLongestPrefixMatch(left, out matched, out found) && found != null)
                {
                    Debug.WriteLine($"[CTY] Match left side '{left}': prefix='{matched}', country='{found.Country}'");
                    return found;
                }
            }

            var lastSlash = norm.LastIndexOf('/');
            if (lastSlash >= 0 && lastSlash + 1 < norm.Length)
            {
                var right = norm[(lastSlash + 1)..];
                if (TryLongestPrefixMatch(right, out matched, out found) && found != null)
                {
                    Debug.WriteLine($"[CTY] Match right side '{right}': prefix='{matched}', country='{found.Country}'");
                    return found;
                }
            }

            var stripped = Regex.Replace(norm, @"(/AM|/MM|/P|/M|/QRP|/A)$", string.Empty, RegexOptions.IgnoreCase);
            if (!string.Equals(stripped, norm, StringComparison.Ordinal))
            {
                if (TryLongestPrefixMatch(stripped, out matched, out found) && found != null)
                {
                    Debug.WriteLine($"[CTY] Match stripped '{stripped}': prefix='{matched}', country='{found.Country}'");
                    return found;
                }
            }

            Debug.WriteLine($"[CTY] No match for '{norm}'.");
            return null;
        }

        private bool TryLongestPrefixMatch(string text, out string matchedPrefix, out Entry? entry)
        {
            for (int len = text.Length; len > 0; len--)
            {
                var key = text.Substring(0, len);
                if (_prefixMap.TryGetValue(key, out var e))
                {
                    matchedPrefix = key;
                    entry = e; return true;
                }
            }
            matchedPrefix = string.Empty; entry = null; return false;
        }
    }
}

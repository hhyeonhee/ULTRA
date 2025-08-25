using System.Collections.Generic;

namespace ULTRA.Helpers
{
    public static class CsvDictExtensions
    {
        public static string GetOrEmpty(this Dictionary<string, string> d, string key)
            => d != null && d.TryGetValue(key, out var v) ? v : "";

        public static string GetAnyOrEmpty(this Dictionary<string, string> d, params string[] keys)
        {
            if (d == null || keys == null) return "";
            foreach (var k in keys)
                if (d.TryGetValue(k, out var v))
                    return v;
            return "";
        }
    }
}


using System;

namespace AirlineCEO.UI.Navigation
{
    /// <summary>页面 ID 精确匹配：n11 不误伤 n110/n112/n113。</summary>
    public static class PageIdMatch
    {
        public static bool Is(string pageId, string code)
        {
            if (string.IsNullOrEmpty(pageId) || string.IsNullOrEmpty(code)) return false;
            if (pageId == code) return true;
            if (!pageId.StartsWith(code, StringComparison.Ordinal)) return false;
            if (pageId.Length == code.Length) return true;
            char next = pageId[code.Length];
            return next == '_' || next == '-' || char.IsLetter(next);
        }
    }
}

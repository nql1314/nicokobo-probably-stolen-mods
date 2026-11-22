using System.Text.RegularExpressions;

namespace NicoItemManager
{
    internal static class ItemTooltipText
    {
        static readonly Regex Breaks = new Regex(@"<br\s*/?>", RegexOptions.IgnoreCase);
        static readonly Regex Formatting = new Regex(
            @"</?(?:b|i|u|s|color|size|font|font-weight|alpha|align|cspace|mspace|indent|line-height|line-indent|margin|mark|nobr|noparse|pos|rotate|space|style|voffset|width|allcaps|lowercase|uppercase|smallcaps|sub|sup)(?:=[^<>]*|\s+[^<>]*)?>|<#[0-9a-f]{6,8}>",
            RegexOptions.IgnoreCase);

        // Native nakedText can still contain formatting supplied inside a text slice.
        internal static string Plain(string text) => Formatting.Replace(Breaks.Replace(text ?? "", "\n"), "");
    }
}

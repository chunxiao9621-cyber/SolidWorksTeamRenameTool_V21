using System.Collections.Generic;
using System.Linq;

namespace SolidWorksTeamRenameTool
{
    internal sealed class FilterSettings
    {
        public bool SkipEnglishStart { get; set; }
        public bool SkipChineseStart { get; set; }
        public bool SkipNumberStart { get; set; }
        public bool SkipSymbolStart { get; set; }
        public bool SkipDuplicateReferences { get; set; }
        public bool SkipReadonlyFiles { get; set; }
        public bool SkipPathKeywords { get; set; }
        public List<string> PathKeywords { get; private set; }

        public FilterSettings()
        {
            SkipEnglishStart = true;
            SkipDuplicateReferences = true;
            SkipReadonlyFiles = true;
            SkipPathKeywords = true;
            PathKeywords = new List<string> { "\u6807\u51c6\u4ef6", "\u5916\u8d2d\u4ef6" };
        }

        public FilterSettings Clone()
        {
            return new FilterSettings
            {
                SkipEnglishStart = SkipEnglishStart,
                SkipChineseStart = SkipChineseStart,
                SkipNumberStart = SkipNumberStart,
                SkipSymbolStart = SkipSymbolStart,
                SkipDuplicateReferences = SkipDuplicateReferences,
                SkipReadonlyFiles = SkipReadonlyFiles,
                SkipPathKeywords = SkipPathKeywords,
                PathKeywords = PathKeywords.ToList()
            };
        }
    }
}

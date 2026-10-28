using System.Collections.Generic;

namespace BetterProject.Core
{
    public static class BreadcrumbCollapse
    {
        /// <summary>
        /// How many leading segments to hide so the rest (plus an ellipsis when any are hidden) fits in
        /// <paramref name="available"/>. The last segment is never hidden.
        /// </summary>
        public static int HiddenCount(IReadOnlyList<float> widths, float available, float ellipsisWidth)
        {
            int n = widths.Count;
            if (n <= 1) return 0;
            float total = 0;
            for (int i = 0; i < n; i++) total += widths[i];
            if (total <= available) return 0;
            int hidden = 0;
            float remaining = total;
            while (hidden < n - 1)
            {
                remaining -= widths[hidden];
                hidden++;
                if (remaining + ellipsisWidth <= available) break;
            }
            return hidden;
        }
    }
}

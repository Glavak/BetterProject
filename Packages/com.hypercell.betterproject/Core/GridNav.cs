namespace BetterProject.Core
{
    public enum NavDir { Left, Right, Up, Down, Home, End }

    public static class GridNav
    {
        public static int Move(int index, int count, int columns, NavDir dir)
        {
            if (count <= 0) return -1;
            if (index < 0) return 0;
            if (columns < 1) columns = 1;
            if (index > count - 1) index = count - 1;
            switch (dir)
            {
                case NavDir.Left: return index > 0 ? index - 1 : 0;
                case NavDir.Right: return index < count - 1 ? index + 1 : count - 1;
                case NavDir.Up: return index - columns < 0 ? index : index - columns;
                case NavDir.Down:
                {
                    int next = index + columns;
                    if (next < count) return next;
                    bool laterRow = index / columns < (count - 1) / columns;
                    return laterRow ? count - 1 : index;
                }
                case NavDir.Home: return 0;
                case NavDir.End: return count - 1;
                default: return index;
            }
        }
    }
}

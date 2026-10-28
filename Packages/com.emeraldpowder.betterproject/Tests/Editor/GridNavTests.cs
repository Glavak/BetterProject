using NUnit.Framework;

namespace BetterProject.Core.Tests
{
    public class GridNavTests
    {
        [TestCase(0, 7, 3, NavDir.Down, 3)]
        [TestCase(5, 7, 3, NavDir.Down, 6)] // later row exists (row 2) -> last item
        [TestCase(6, 7, 3, NavDir.Down, 6)] // true last row: stays
        [TestCase(4, 7, 3, NavDir.Down, 6)]
        [TestCase(1, 7, 3, NavDir.Up, 1)]
        [TestCase(4, 7, 3, NavDir.Up, 1)]
        [TestCase(6, 7, 3, NavDir.Right, 6)]
        [TestCase(0, 7, 3, NavDir.Left, 0)]
        [TestCase(-1, 7, 3, NavDir.Down, 0)]
        [TestCase(3, 0, 3, NavDir.Down, -1)]
        [TestCase(2, 7, 1, NavDir.Down, 3)]
        [TestCase(2, 7, 3, NavDir.End, 6)]
        [TestCase(4, 7, 3, NavDir.Home, 0)]
        [TestCase(2, 7, 3, NavDir.Right, 3)]
        public void GridNav_Move(int i, int count, int cols, NavDir d, int expected)
            => Assert.AreEqual(expected, GridNav.Move(i, count, cols, d));
    }
}

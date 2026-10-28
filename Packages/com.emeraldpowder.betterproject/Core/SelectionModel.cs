using System.Collections.Generic;

namespace BetterProject.Core
{
    public sealed class SelectionModel
    {
        readonly List<int> _selected = new List<int>();

        public IReadOnlyList<int> Selected => _selected;
        public int Anchor { get; private set; } = -1;

        public void Click(int id, bool toggle, bool range, IReadOnlyList<int> order)
        {
            if (range && order != null)
            {
                int a = IndexOf(order, Anchor), b = IndexOf(order, id);
                if (a >= 0 && b >= 0)
                {
                    _selected.Clear();
                    int lo = a < b ? a : b, hi = a < b ? b : a;
                    for (int i = lo; i <= hi; i++) _selected.Add(order[i]);
                    return;
                }
                // anchor missing: fall through to plain click
                toggle = false;
            }
            if (toggle)
            {
                if (!_selected.Remove(id)) _selected.Add(id);
            }
            else
            {
                _selected.Clear();
                _selected.Add(id);
            }
            Anchor = id;
        }

        public void Set(IEnumerable<int> ids)
        {
            _selected.Clear();
            foreach (var id in ids) if (!_selected.Contains(id)) _selected.Add(id);
        }

        public void Retain(IReadOnlyList<int> order)
        {
            var keep = new HashSet<int>(order);
            _selected.RemoveAll(id => !keep.Contains(id));
        }

        static int IndexOf(IReadOnlyList<int> order, int id)
        {
            for (int i = 0; i < order.Count; i++) if (order[i] == id) return i;
            return -1;
        }
    }
}

using UnityEngine;
using UnityEngine.UIElements;

namespace BetterProject.Editor
{
    /// <summary>A five-pointed star drawn with Painter2D: outlined, or filled when <see cref="Filled"/>.</summary>
    class StarIcon : VisualElement
    {
        bool _filled;

        public bool Filled
        {
            get => _filled;
            set
            {
                if (_filled == value) return;
                _filled = value;
                MarkDirtyRepaint();
            }
        }

        public StarIcon()
        {
            pickingMode = PickingMode.Ignore;
            style.width = 16;
            style.height = 16;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            var center = r.center + new Vector2(0, 0.5f); // the points' bounding box sits a little above the centre
            float outer = Mathf.Min(r.width, r.height) * 0.5f - 1f;
            float inner = outer * 0.42f;
            var p = ctx.painter2D;
            p.lineJoin = LineJoin.Round;
            p.BeginPath();
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.PI * (-0.5f + i * 0.2f);
                float rad = i % 2 == 0 ? outer : inner;
                var pt = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad;
                if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
            p.ClosePath();
            if (_filled)
            {
                p.fillColor = resolvedStyle.color;
                p.Fill();
            }
            else
            {
                p.strokeColor = resolvedStyle.color;
                p.lineWidth = 1.2f;
                p.Stroke();
            }
        }
    }
}

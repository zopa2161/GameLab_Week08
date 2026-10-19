using System.Collections.Generic;

using UnityEngine;

namespace RuneCode
{
    internal static class RuneMesh
    {
        private const float EXPLOSION_SEGMENT_ANGLE = 2f;
        private const int SLASH_TRAIL_SEGMENTS = 20;
        private const float SLASH_TRAIL_ALPHA = 0.65f;
        private const float SLASH_TRAIL_SECONDS = 0.12f;
        private const float SLASH_TRAIL_LIFETIME_RATIO = 0.4f;

        private const double BEAM_CLOSED_DISTANCE_SQUARED = 0.000001;
        internal const int BEAM_GLOW_BANDS = 16;
        internal const float BEAM_GLOW_WIDTH = 3f;
        internal const float BEAM_GLOW_PULSE_SPEED = 3f;

        /// <summary>현재 칼날과 회전 진행으로 최근 부채꼴 구간의 잔상·직사각형 칼날·밝은 날을 재사용 목록에 채운다. 오래된 잔상과 종료 직전 잔상은 투명해지며 scale로 크기를 변환한다.</summary>
        internal static void BuildSlash(SimulationSpellEntity spell, float scale, List<Vector3> vertices,
            List<Color> colors, List<int> triangles)
        {
            vertices.Clear(); colors.Clear(); triangles.Clear();
            IReadOnlyList<SimVector> path = spell.SlashPath;
            if (path.Count < 2) return;
            SimVector delta = path[1] - path[0];
            float length = (float)delta.Length * scale;
            if (length <= 0) return;
            Vector2 direction = new Vector2((float)delta.X, -(float)delta.Y).normalized;
            float angle = Mathf.Atan2(direction.y, direction.x);
            float progress = Mathf.Clamp01((float)spell.SlashProgress);
            float lifetime = (float)spell.Lifetime;
            float trailSeconds = Mathf.Min(SLASH_TRAIL_SECONDS, lifetime * SLASH_TRAIL_LIFETIME_RATIO);
            float trailProgress = trailSeconds / lifetime;
            float visibleProgress = Mathf.Min(progress, trailProgress);
            float span = (float)spell.ConeAngle * visibleProgress * Mathf.Deg2Rad;
            float endFade = Mathf.Clamp01((1 - progress) / trailProgress);
            float width = (float)spell.BoxWidth * scale;
            Color tint = ElementColor(spell.Element);
            Color bright = Color.Lerp(tint, Color.white, 0.85f);
            for (int index = 0; index <= SLASH_TRAIL_SEGMENTS; index++)
            {
                float fraction = index / (float)SLASH_TRAIL_SEGMENTS;
                float sampleAngle = angle - span * (1 - fraction);
                Vector2 radial = new Vector2(Mathf.Cos(sampleAngle), Mathf.Sin(sampleAngle));
                float freshness = 1 - visibleProgress * (1 - fraction) / trailProgress;
                float alpha = SLASH_TRAIL_ALPHA * freshness * freshness * endFade;
                Color outer = tint; outer.a = alpha;
                Color inner = tint; inner.a = alpha * 0.35f;
                Color edge = bright; edge.a = alpha;
                vertices.Add(Vector3.zero); colors.Add(inner);
                vertices.Add(radial * Mathf.Max(0, length - width * 0.18f)); colors.Add(outer);
                vertices.Add(radial * length); colors.Add(edge);
                if (index == 0) continue;
                int previous = (index - 1) * 3;
                AddSlashQuad(triangles, previous, previous + 3, previous + 4, previous + 1);
                AddSlashQuad(triangles, previous + 1, previous + 4, previous + 5, previous + 2);
            }
            Vector2 normal = new Vector2(-direction.y, direction.x);
            int blade = vertices.Count;
            vertices.Add(-normal * width * 0.5f); colors.Add(tint);
            vertices.Add(direction * length - normal * width * 0.5f); colors.Add(tint);
            vertices.Add(direction * length + normal * width * 0.5f); colors.Add(bright);
            vertices.Add(normal * width * 0.5f); colors.Add(bright);
            AddSlashQuad(triangles, blade, blade + 1, blade + 2, blade + 3);
            int core = vertices.Count;
            vertices.Add(-normal * width * 0.12f); colors.Add(bright);
            vertices.Add(direction * length - normal * width * 0.12f); colors.Add(Color.white);
            vertices.Add(direction * length + normal * width * 0.12f); colors.Add(Color.white);
            vertices.Add(normal * width * 0.12f); colors.Add(bright);
            AddSlashQuad(triangles, core, core + 1, core + 2, core + 3);
        }

        /// <summary>공유 참격 메시의 사각 구간을 네 정점 인덱스로 받아 삼각형 두 개를 추가한다.</summary>
        private static void AddSlashQuad(List<int> triangles, int first, int second, int third, int fourth)
        {
            triangles.Add(first); triangles.Add(second); triangles.Add(third);
            triangles.Add(first); triangles.Add(third); triangles.Add(fourth);
        }

        /// <summary>빔 가로 위치(-1~1), 맥동 밝기와 충전 진행률로 기본 빔에서 부드러운 발광까지 보간한 색상을 반환한다.</summary>
        internal static Color BeamGlowColor(Color tint, float offset, float brightness, float chargeProgress = 1f)
        {
            float distanceSquared = offset * offset;
            Color color = Color.Lerp(tint, Color.white, 0.8f * Mathf.Exp(-24f * distanceSquared));
            float edge = Mathf.Exp(-5f);
            color.a = tint.a * Mathf.Clamp01((Mathf.Exp(-5f * distanceSquared) - edge) / (1f - edge)) * brightness;
            Color baseColor = tint;
            baseColor.a = Mathf.Abs(offset) <= 1f / BEAM_GLOW_WIDTH ? tint.a : 0;
            return Color.Lerp(baseColor, color, chargeProgress);
        }

        /// <summary>경로 꼭짓점의 앞뒤 방향을 합쳐 인접 선분이 공유할 폭 방향을 반환한다. 닫힌 경로의 첫·끝 방향을 맞추고 급격한 꺾임의 돌출을 제한한다.</summary>
        internal static Vector2 BeamJoinNormal(IReadOnlyList<SimVector> path, int index)
        {
            if (path.Count < 2) return Vector2.zero;
            int last = path.Count - 1;
            bool isClosed = last > 1 && (path[0] - path[last]).LengthSquared <= BEAM_CLOSED_DISTANCE_SQUARED;
            int previous = index > 0 ? index - 1 : isClosed ? last - 1 : 0;
            int next = index < last ? index + 1 : isClosed ? 1 : last;
            SimVector incoming = path[index] - path[previous];
            SimVector outgoing = path[next] - path[index];
            Vector2 before = new Vector2((float)incoming.X, -(float)incoming.Y).normalized;
            Vector2 after = new Vector2((float)outgoing.X, -(float)outgoing.Y).normalized;
            if (before.sqrMagnitude == 0) before = after;
            if (after.sqrMagnitude == 0) after = before;
            Vector2 tangent = (before + after).normalized;
            if (tangent.sqrMagnitude == 0) tangent = after;
            Vector2 normal = new Vector2(-tangent.y, tangent.x);
            Vector2 outgoingNormal = new Vector2(-after.y, after.x);
            return normal / Mathf.Max(0.5f, Vector2.Dot(normal, outgoingNormal));
        }

        /// <summary>경로·화면 원점·배율·폭으로 꼭짓점을 공유하는 연속 빔 띠를 추가한다. 충전 진행률과 밝기를 발광 색상에 반영한다.</summary>
        internal static void BeamPath(UnityEngine.UI.VertexHelper mesh, IReadOnlyList<SimVector> path, Vector2 origin, float scale,
            float width, Color tint, float brightness, float chargeProgress)
        {
            bool isEmpowered = chargeProgress > 0;
            int bands = isEmpowered ? BEAM_GLOW_BANDS : 1;
            int rowSize = bands + 1;
            float halfWidth = width * scale * 0.5f * (isEmpowered ? BEAM_GLOW_WIDTH : 1f);
            int firstVertex = mesh.currentVertCount;
            for (int index = 0; index < path.Count; index++)
            {
                Vector2 point = origin + new Vector2((float)path[index].X, -(float)path[index].Y) * scale;
                Vector2 normal = BeamJoinNormal(path, index) * halfWidth;
                for (int band = 0; band <= bands; band++)
                {
                    float offset = -1f + 2f * band / bands;
                    Color color = isEmpowered ? BeamGlowColor(tint, offset, brightness, chargeProgress) : tint;
                    mesh.AddVert(point + normal * offset, color, Vector2.zero);
                    if (index == 0 || band == 0) continue;
                    int current = firstVertex + index * rowSize + band;
                    int previous = current - rowSize;
                    mesh.AddTriangle(previous - 1, previous, current);
                    mesh.AddTriangle(previous - 1, current, current - 1);
                }
            }
        }

        /// <summary>전투와 도크에서 같은 각도 간격으로 호를 표시할 선분 수를 반환한다.</summary>
        internal static int ExplosionSegmentCount(double angle) => Mathf.Max(1, Mathf.CeilToInt((float)angle / EXPLOSION_SEGMENT_ANGLE));

        /// <summary>사각형의 위치와 색상으로 UI 메시의 네 꼭짓점과 삼각형을 추가한다.</summary>
        internal static void Rect(UnityEngine.UI.VertexHelper mesh, Rect rect, Color color)
        {
            int index = mesh.currentVertCount;
            mesh.AddVert(new Vector3(rect.xMin, rect.yMin), color, Vector2.zero);
            mesh.AddVert(new Vector3(rect.xMin, rect.yMax), color, Vector2.zero);
            mesh.AddVert(new Vector3(rect.xMax, rect.yMax), color, Vector2.zero);
            mesh.AddVert(new Vector3(rect.xMax, rect.yMin), color, Vector2.zero);
            mesh.AddTriangle(index, index + 1, index + 2);
            mesh.AddTriangle(index, index + 2, index + 3);
        }

        /// <summary>
        /// 두 점 사이를 지그재그 번개 모양으로 나눈 꺾임점을 points에 채운다. 양 끝은 고정하고 안쪽 점은 진행 방향에 수직으로
        /// 구간 길이 대비 amplitude 비율만큼 흔든다. seed가 같으면 같은 모양이 나온다.
        /// </summary>
        internal static void ArcPoints(Vector2 from, Vector2 to, int seed, float amplitude, Vector2[] points)
        {
            Vector2 delta = to - from;
            Vector2 normal = new Vector2(-delta.y, delta.x).normalized * delta.magnitude * amplitude;
            int last = points.Length - 1;
            for (int i = 0; i <= last; i++)
            {
                // 끝점은 고정하고 안쪽 점은 seed로 정한 -1~1 의사 난수만큼 흔든다.
                float offset = i == 0 || i == last ? 0f : Mathf.Repeat(Mathf.Sin((seed * 31 + i) * 12.9898f) * 43758.5453f, 2f) - 1f;
                points[i] = from + delta * ((float)i / last) + normal * offset;
            }
        }

        /// <summary>두 점 사이에 지정 두께와 색상의 선분 메시를 추가한다.</summary>
        internal static void Line(UnityEngine.UI.VertexHelper mesh, Vector2 from, Vector2 to, float width, Color color)
        {
            Vector2 perpendicular = new Vector2(-(to - from).y, (to - from).x).normalized * width * 0.5f;
            int index = mesh.currentVertCount;
            mesh.AddVert(from + perpendicular, color, Vector2.zero);
            mesh.AddVert(to + perpendicular, color, Vector2.zero);
            mesh.AddVert(to - perpendicular, color, Vector2.zero);
            mesh.AddVert(from - perpendicular, color, Vector2.zero);
            mesh.AddTriangle(index, index + 1, index + 2);
            mesh.AddTriangle(index, index + 2, index + 3);
        }

        /// <summary>중심, 반경, 변 수와 색상으로 원 또는 다각형 메시를 추가한다.</summary>
        internal static void Polygon(UnityEngine.UI.VertexHelper mesh, Vector2 center, float radius, Color color, int sides = 20, float rotation = 0)
        {
            int index = mesh.currentVertCount;
            mesh.AddVert(center, color, Vector2.zero);
            for (int i = 0; i <= sides; i++)
            {
                float angle = rotation + i * Mathf.PI * 2 / sides;
                mesh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, color, Vector2.zero);
                if (i > 0) mesh.AddTriangle(index, index + i, index + i + 1);
            }
        }

        /// <summary>중심, 가로·세로 반경과 회전(라디안)으로 채워진 타원 메시를 추가한다.</summary>
        internal static void Ellipse(UnityEngine.UI.VertexHelper mesh, Vector2 center, float radiusX, float radiusY,
            float rotation, Color color, int segments = 16)
        {
            int index = mesh.currentVertCount;
            mesh.AddVert(center, color, Vector2.zero);
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2 / segments;
                Vector2 offset = new Vector2(Mathf.Cos(angle) * radiusX, Mathf.Sin(angle) * radiusY);
                Vector2 rotated = new Vector2(offset.x * Mathf.Cos(rotation) - offset.y * Mathf.Sin(rotation),
                    offset.x * Mathf.Sin(rotation) + offset.y * Mathf.Cos(rotation));
                mesh.AddVert(center + rotated, color, Vector2.zero);
                if (i > 0) mesh.AddTriangle(index, index + i, index + i + 1);
            }
        }

        /// <summary>중심, 반 변 길이와 회전(라디안)으로 채워진 정사각형 메시를 추가한다. 회전 0이면 화면 축에 맞춰 선다.</summary>
        internal static void Square(UnityEngine.UI.VertexHelper mesh, Vector2 center, float halfSize, float rotation, Color color)
        {
            // 꼭짓점이 대각선 방향에 오도록 외접 반경과 45도 보정으로 4각형을 그린다.
            Polygon(mesh, center, halfSize * Mathf.Sqrt(2), color, 4, rotation + Mathf.PI / 4);
        }

        /// <summary>중심, 반 변 길이와 회전(라디안)으로 지정 두께의 정사각형 윤곽선을 추가한다.</summary>
        internal static void SquareOutline(UnityEngine.UI.VertexHelper mesh, Vector2 center, float halfSize, float rotation, float width, Color color)
        {
            Vector2 axisX = new Vector2(Mathf.Cos(rotation), Mathf.Sin(rotation)) * halfSize;
            Vector2 axisY = new Vector2(-axisX.y, axisX.x);
            Vector2[] corners = { center - axisX - axisY, center + axisX - axisY, center + axisX + axisY, center - axisX + axisY };
            for (int i = 0; i < corners.Length; i++)
            {
                Vector2 from = corners[i];
                Vector2 to = corners[(i + 1) % corners.Length];

                // 선 두께의 절반만큼 양 끝을 늘려 모서리 이음새가 비지 않게 한다.
                Vector2 extension = (to - from).normalized * width * 0.5f;
                Line(mesh, from - extension, to + extension, width, color);
            }
        }

        /// <summary>중심, 반 폭·반 길이와 회전(라디안)으로 채워진 직사각형 메시를 추가한다.</summary>
        internal static void Rectangle(UnityEngine.UI.VertexHelper mesh, Vector2 center, float halfWidth, float halfLength,
            float rotation, Color color)
        {
            Vector2[] corners = RectangleCorners(center, halfWidth, halfLength, rotation);
            int index = mesh.currentVertCount;
            foreach (Vector2 corner in corners) mesh.AddVert(corner, color, Vector2.zero);
            mesh.AddTriangle(index, index + 1, index + 2);
            mesh.AddTriangle(index, index + 2, index + 3);
        }

        /// <summary>중심, 반 폭·반 길이와 회전(라디안)으로 지정 두께의 직사각형 윤곽선을 추가한다.</summary>
        internal static void RectangleOutline(UnityEngine.UI.VertexHelper mesh, Vector2 center, float halfWidth,
            float halfLength, float rotation, float width, Color color)
        {
            Vector2[] corners = RectangleCorners(center, halfWidth, halfLength, rotation);
            for (int i = 0; i < corners.Length; i++)
            {
                Vector2 from = corners[i];
                Vector2 to = corners[(i + 1) % corners.Length];
                Vector2 extension = (to - from).normalized * width * 0.5f;
                Line(mesh, from - extension, to + extension, width, color);
            }
        }

        /// <summary>중심·반 폭·반 길이와 회전으로 직사각형 꼭짓점을 반환한다.</summary>
        private static Vector2[] RectangleCorners(Vector2 center, float halfWidth, float halfLength, float rotation)
        {
            Vector2 lengthAxis = new Vector2(Mathf.Cos(rotation), Mathf.Sin(rotation)) * halfLength;
            Vector2 widthAxis = new Vector2(-Mathf.Sin(rotation), Mathf.Cos(rotation)) * halfWidth;
            return new[] { center - lengthAxis - widthAxis, center + lengthAxis - widthAxis,
                center + lengthAxis + widthAxis, center - lengthAxis + widthAxis };
        }

        /// <summary>꼭짓점, 반경, 중심 방향(라디안)과 전체 각도(라디안)로 채워진 부채꼴 메시를 추가한다.</summary>
        internal static void Sector(UnityEngine.UI.VertexHelper mesh, Vector2 apex, float radius, float rotation, float angle, Color color, int segments = 16)
        {
            int index = mesh.currentVertCount;
            mesh.AddVert(apex, color, Vector2.zero);
            for (int i = 0; i <= segments; i++)
            {
                float current = rotation - angle * 0.5f + angle * i / segments;
                mesh.AddVert(apex + new Vector2(Mathf.Cos(current), Mathf.Sin(current)) * radius, color, Vector2.zero);
                if (i > 0) mesh.AddTriangle(index, index + i, index + i + 1);
            }
        }

        /// <summary>꼭짓점, 반경, 중심 방향(라디안)과 전체 각도(라디안)로 부채꼴 윤곽선(두 변과 호)을 추가한다.</summary>
        internal static void SectorOutline(UnityEngine.UI.VertexHelper mesh, Vector2 apex, float radius, float rotation, float angle, float width, Color color, int segments = 16)
        {
            Vector2 previous = apex;
            for (int i = 0; i <= segments; i++)
            {
                float current = rotation - angle * 0.5f + angle * i / segments;
                Vector2 point = apex + new Vector2(Mathf.Cos(current), Mathf.Sin(current)) * radius;
                Line(mesh, previous, point, width, color);
                previous = point;
            }
            Line(mesh, previous, apex, width, color);
        }

        /// <summary>중심과 반경으로 지정 색상의 원형 윤곽선을 추가한다.</summary>
        internal static void Ring(UnityEngine.UI.VertexHelper mesh, Vector2 center, float radius, float width, Color color, int sides = 24)
        {
            for (int i = 0; i < sides; i++)
            {
                float first = i * Mathf.PI * 2 / sides;
                float second = (i + 1) * Mathf.PI * 2 / sides;
                Line(mesh, center + new Vector2(Mathf.Cos(first), Mathf.Sin(first)) * radius,
                    center + new Vector2(Mathf.Cos(second), Mathf.Sin(second)) * radius, width, color);
            }
        }

        /// <summary>룬 계열을 구별할 수 있는 화면 색상을 반환한다.</summary>
        internal static Color CategoryColor(string category)
        {
            switch (category)
            {
                case SpellGrammar.CATEGORY_CORE: return new Color(0.98f, 0.79f, 0.37f);
                case SpellGrammar.CATEGORY_LEGACY_FORM:
                case SpellGrammar.CATEGORY_BEHAVIOR: return new Color(0.22f, 0.82f, 0.96f);
                case SpellGrammar.CATEGORY_SHAPE: return new Color(0.95f, 0.88f, 0.55f);
                case SpellGrammar.CATEGORY_ELEMENT: return new Color(0.30f, 0.86f, 0.54f);
                case "method": return new Color(0.42f, 0.70f, 1f);
                case SpellGrammar.CATEGORY_MODIFIER: return new Color(0.64f, 0.46f, 0.97f);
                case SpellGrammar.CATEGORY_FLOW: return new Color(0.30f, 0.91f, 0.65f);
                default: return new Color(0.97f, 0.62f, 0.28f);
            }
        }

        /// <summary>피해 속성의 시각적 구분에 사용하는 색상을 반환한다.</summary>
        internal static Color ElementColor(string element)
        {
            switch (element)
            {
                case "fire": return new Color(1f, 0.40f, 0.25f);
                case "ice": return new Color(0.34f, 0.83f, 1f);
                case "arc": return new Color(0.72f, 0.52f, 1f);
                default: return new Color(0.81f, 0.96f, 0.78f);
            }
        }

        /// <summary>등급 문자(C·B·A·S)에 대응하는 보상 등급 표시 색상을 반환한다. 정의되지 않은 등급은 흰색을 반환한다.</summary>
        internal static Color GradeColor(string grade)
        {
            switch (grade)
            {
                case "C": return new Color(0.75f, 0.78f, 0.80f, 1f);
                case "B": return new Color(0.24f, 0.84f, 0.94f, 1f);
                case "A": return new Color(0.66f, 0.40f, 1f, 1f);
                case "S": return new Color(1f, 0.765f, 0.365f, 1f);
                default: return Color.white;
            }
        }
    }
}

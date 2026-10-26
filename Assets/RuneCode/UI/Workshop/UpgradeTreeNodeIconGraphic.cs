using UnityEngine;

using UnityEngine.UI;

namespace RuneCode
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UpgradeTreeNodeIconGraphic : MaskableGraphic
    {
        private const float ICON_LINE_WIDTH = 2.2f;

        private UpgradeEffectType _effectType;
        private string _runeCategory;
        private string _runeId;

        /// <summary>강화 효과와 룬 ID를 받아 전용 벡터 아이콘을 갱신한다. 색상은 Graphic의 Inspector 설정을 사용한다.</summary>
        public void Configure(UpgradeEffectType effectType, string runeCategory, string runeId = null)
        {
            _effectType = effectType;
            _runeCategory = runeCategory;
            _runeId = runeId;
            SetVerticesDirty();
        }

        /// <summary>강화 효과 또는 개별 룬의 주제를 40 단위 정사각형 안에 그려 현재 Rect 크기로 맞춘다.</summary>
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect bounds = GetPixelAdjustedRect();
            float scale = Mathf.Min(bounds.width, bounds.height) / 40f;
            Vector2 center = bounds.center;
            Color tint = color;
            switch (_effectType)
            {
                case UpgradeEffectType.EnergyRegen:
                    // 메인보드의 중앙 칩과 회로 배선.
                    Path(mesh, center, scale, tint, -14,-14, -14,14, 14,14, 14,-14, -14,-14);
                    Path(mesh, center, scale, tint, -5,-5, -5,5, 5,5, 5,-5, -5,-5);
                    Path(mesh, center, scale, tint, -14,8, -9,8, -9,0, -5,0);
                    Path(mesh, center, scale, tint, 5,0, 10,0, 10,-8, 14,-8);
                    Path(mesh, center, scale, tint, 0,5, 0,14);
                    Path(mesh, center, scale, tint, 0,-5, 0,-14);
                    break;
                case UpgradeEffectType.MaxEnergy:
                    Path(mesh, center, scale, tint, -13,-10, -13,10, 11,10, 11,-10, -13,-10);
                    Path(mesh, center, scale, tint, 11,4, 15,4, 15,-4, 11,-4);
                    Path(mesh, center, scale, tint, 2,7, -4,0, 3,0, -3,-7);
                    break;
                case UpgradeEffectType.CastSpeed:
                    DrawChip(mesh, center, scale, tint);
                    Path(mesh, center, scale, tint, 2,6, -3,0, 3,0, -2,-6);
                    break;
                case UpgradeEffectType.Damage:
                    // GPU 보드, 냉각 팬, 슬롯 접점.
                    Path(mesh, center, scale, tint, -15,-8, -15,11, 14,11, 14,-8, -15,-8);
                    Ring(mesh, center, scale, tint, -2,2, 6);
                    for (int i = 0; i < 4; i++)
                    {
                        float a = i * Mathf.PI * 0.5f;
                        Path(mesh, center, scale, tint, -2,2, -2+4*Mathf.Cos(a),2+4*Mathf.Sin(a));
                    }
                    Path(mesh, center, scale, tint, 9,-3, 9,7);
                    Path(mesh, center, scale, tint, -9,-8, -9,-12, 7,-12, 7,-8);
                    break;
                case UpgradeEffectType.ScrapGain:
                    Path(mesh, center, scale, tint, -13,3, -7,9, -1,3, -7,-3, -13,3);
                    Path(mesh, center, scale, tint, -3,-7, 3,-1, 9,-7, 3,-13, -3,-7);
                    Plus(mesh, center + new Vector2(10,10)*scale, scale, tint, 4);
                    break;
                case UpgradeEffectType.ScrapPickupRange:
                    // U자 자석과 끌려오는 조각.
                    Path(mesh, center, scale, tint, -12,11, -12,-5, -7,-12, 4,-12, 9,-5, 9,11);
                    Path(mesh, center, scale, tint, -6,11, -6,-3, -3,-6, 0,-6, 3,-3, 3,11);
                    Path(mesh, center, scale, tint, -12,5, -6,5);
                    Path(mesh, center, scale, tint, 3,5, 9,5);
                    Path(mesh, center, scale, tint, -3,13, 0,16, 3,13, 0,10, -3,13);
                    break;
                case UpgradeEffectType.ScrapHeal:
                    Path(mesh, center, scale, tint, -13,0, -6,7, 1,0, -6,-7, -13,0);
                    Plus(mesh, center + new Vector2(8,5)*scale, scale, tint, 5);
                    break;
                case UpgradeEffectType.MaxHp:
                    Path(mesh, center, scale, tint, 0,-14, -14,1, -14,8, -7,13, 0,7, 7,13, 14,8, 14,1, 0,-14);
                    break;
                case UpgradeEffectType.MoveSpeed:
                    Path(mesh, center, scale, tint, -5,13, 5,13, 5,2, 13,-4, 14,-10, -7,-10, -10,-5, -5,0, -5,13);
                    Path(mesh, center, scale, tint, -15,6, -9,6);
                    Path(mesh, center, scale, tint, -16,0, -11,0);
                    break;
                case UpgradeEffectType.KillEnergy:
                    Path(mesh, center, scale, tint, -12,-10, -12,5, -8,10, 0,10, 4,5, 4,-10, -12,-10);
                    Path(mesh, center, scale, tint, -10,3, -7,0);
                    Path(mesh, center, scale, tint, -7,3, -10,0);
                    Path(mesh, center, scale, tint, -4,3, -1,0);
                    Path(mesh, center, scale, tint, -1,3, -4,0);
                    Path(mesh, center, scale, tint, 14,14, 7,3, 14,3, 8,-10);
                    break;
                case UpgradeEffectType.None:
                    // 시작 노드는 메인보드 칩과 중심 심볼로 표시한다.
                    DrawChip(mesh, center, scale, tint);
                    Dot(mesh, center, scale, tint, 0, 0, 3);
                    break;
                case UpgradeEffectType.NearDamage:
                    // 중심 대상과 안쪽으로 향하는 화살.
                    Ring(mesh, center, scale, tint, 0, 0, 12);
                    Dot(mesh, center, scale, tint, 0, 0, 3);
                    Path(mesh, center, scale, tint, 16, 0, 6, 0);
                    Path(mesh, center, scale, tint, 6, 0, 11, 4);
                    Path(mesh, center, scale, tint, 6, 0, 11, -4);
                    break;
                case UpgradeEffectType.FarDamage:
                    // 중심 대상과 바깥으로 향하는 화살과 거리 호.
                    Dot(mesh, center, scale, tint, 0, 0, 3);
                    Path(mesh, center, scale, tint, -4, 0, 12, 0);
                    Path(mesh, center, scale, tint, 12, 0, 7, 4);
                    Path(mesh, center, scale, tint, 12, 0, 7, -4);
                    Arc(mesh, center, scale, tint, 0, 0, 15, -45, 45);
                    break;
                case UpgradeEffectType.FullManaDamage:
                    // 가득 찬 전력 게이지.
                    Path(mesh, center, scale, tint, -14, -7, -14, 7, 12, 7, 12, -7, -14, -7);
                    Path(mesh, center, scale, tint, 12, -3, 16, -3, 16, 3, 12, 3);
                    Path(mesh, center, scale, tint, -10, -3, -10, 3, 8, 3, 8, -3, -10, -3);
                    break;
                case UpgradeEffectType.PostDashDamage:
                    // 대시 잔상 화살과 피해 십자.
                    Path(mesh, center, scale, tint, -14, 8, -7, 0, -14, -8);
                    Path(mesh, center, scale, tint, -4, 8, 3, 0, -4, -8);
                    Plus(mesh, center + new Vector2(10, 0) * scale, scale, tint, 4);
                    break;
                case UpgradeEffectType.ElementDamage:
                    // 속성 룬 삼각형과 피해 십자.
                    Path(mesh, center, scale, tint, 0, 13, -11, -9, 11, -9, 0, 13);
                    Plus(mesh, center + new Vector2(9, -4) * scale, scale, tint, 4);
                    break;
                case UpgradeEffectType.ElementCostReduction:
                    // 속성 룬 삼각형과 비용 절감 표식.
                    Path(mesh, center, scale, tint, -2, 14, -13, -6, 9, -6, -2, 14);
                    Path(mesh, center, scale, tint, 3, -12, 14, -12);
                    break;
                case UpgradeEffectType.StatusDuration:
                    // 지속시간을 나타내는 시계.
                    Ring(mesh, center, scale, tint, 0, 0, 12);
                    Path(mesh, center, scale, tint, 0, 0, 0, 7);
                    Path(mesh, center, scale, tint, 0, 0, 5, 2);
                    break;
                case UpgradeEffectType.ArcEnhance:
                    // 전격의 번개 모양.
                    Path(mesh, center, scale, tint, 3, 16, -11, -2, -1, -2, -4, -16, 12, 4, 2, 4, 3, 16);
                    break;
                case UpgradeEffectType.IceEnhance:
                    // 냉기의 여섯 갈 눈송이.
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * Mathf.PI / 3f;
                        Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                        Vector2 n = new Vector2(-d.y, d.x);
                        Path(mesh, center, scale, tint, 0, 0, d.x * 14, d.y * 14);
                        Vector2 p = d * 8;
                        Vector2 l = d * 4 + n * 4;
                        Vector2 r = d * 4 - n * 4;
                        Path(mesh, center, scale, tint, l.x, l.y, p.x, p.y, r.x, r.y);
                    }
                    break;
                case UpgradeEffectType.FireEnhance:
                    // 화염의 불쏘시개 모양.
                    Path(mesh, center, scale, tint, 0, 16, -2, 5, -8, 10, -13, 0, -11, -9, -5, -14, 5, -14, 12, -8, 13, 0, 7, 9, 7, 1, 0, 16);
                    Path(mesh, center, scale, tint, 0, -11, -4, -5, 0, 2, 4, -5, 0, -11);
                    break;
                case UpgradeEffectType.ElementPick:
                    // 세 속성 가운데 하나를 고른 표시.
                    Ring(mesh, center, scale, tint, 0, 0, 13);
                    Dot(mesh, center, scale, tint, 0, 7, 3);
                    Dot(mesh, center, scale, tint, 6, -5, 3);
                    Dot(mesh, center, scale, tint, -6, -5, 5);
                    break;
                case UpgradeEffectType.ElementSlot:
                    // 두 개의 속성 슬롯이 겹친 모양.
                    Path(mesh, center, scale, tint, -15, -4, -15, 12, 1, 12, 1, -4, -15, -4);
                    Path(mesh, center, scale, tint, -1, -12, -1, 4, 15, 4, 15, -12, -1, -12);
                    break;
                case UpgradeEffectType.StatusStackStart:
                    // 상태 이상이 겹쳐 쌓이는 층 모양.
                    Path(mesh, center, scale, tint, -10, 7, 0, 13, 10, 7, 0, 1, -10, 7);
                    Path(mesh, center, scale, tint, -10, 0, 0, 6, 10, 0);
                    Path(mesh, center, scale, tint, -10, -7, 0, -1, 10, -7);
                    break;
                case UpgradeEffectType.DashCooldownReduction:
                    // 쿨타임을 나타내는 시계와 감소 표식.
                    Ring(mesh, center, scale, tint, -1, 0, 11);
                    Path(mesh, center, scale, tint, -1, 0, -1, -6);
                    Path(mesh, center, scale, tint, -1, 0, 3, 0);
                    Path(mesh, center, scale, tint, 7, 13, 15, 13);
                    break;
                case UpgradeEffectType.DashInvulnerability:
                    // 무적을 나타내는 방패.
                    Path(mesh, center, scale, tint, 0, 14, -11, 9, -11, -3, 0, -14, 11, -3, 11, 9, 0, 14);
                    break;
                case UpgradeEffectType.Evasion:
                    // 적을 비껴가는 곡선 경로.
                    Dot(mesh, center, scale, tint, -8, 0, 4);
                    Arc(mesh, center, scale, tint, -8, 0, 10, -60, 60);
                    Path(mesh, center, scale, tint, -3, 8.7f, -7.5f, 9.5f);
                    Path(mesh, center, scale, tint, -3, 8.7f, -1.5f, 13f);
                    break;
                case UpgradeEffectType.DashInvulnerabilityUnlock:
                    // 무적 방패에 잠금 해제 구멍을 겹친 표시.
                    Path(mesh, center, scale, tint, 0, 14, -11, 9, -11, -3, 0, -14, 11, -3, 11, 9, 0, 14);
                    Dot(mesh, center, scale, tint, 0, 4, 3);
                    Path(mesh, center, scale, tint, 0, 2, 0, -6);
                    break;
                case UpgradeEffectType.DashTeleport:
                    // 점선으로 이어지는 순간이동 화살.
                    Path(mesh, center, scale, tint, -16, 0, -12, 0);
                    Path(mesh, center, scale, tint, -9, 0, -5, 0);
                    Path(mesh, center, scale, tint, -2, 0, 2, 0);
                    Path(mesh, center, scale, tint, 7, -5, 14, 0, 7, 5);
                    break;
                case UpgradeEffectType.DashCollisionGuard:
                    // 대시 잔상과 방패.
                    Path(mesh, center, scale, tint, -15, 6, -11, 0, -15, -6);
                    Path(mesh, center, scale, tint, 2, 12, -6, 8, -6, -1, 2, -11, 10, -1, 10, 8, 2, 12);
                    break;
                case UpgradeEffectType.DashEndStart:
                    // 대시 화살이 시작 삼각형으로 이어지는 모양.
                    Path(mesh, center, scale, tint, -15, 8, -9, 0, -15, -8);
                    Path(mesh, center, scale, tint, -6, 8, 0, 0, -6, -8);
                    Path(mesh, center, scale, tint, 4, -8, 14, 0, 4, 8, 4, -8);
                    break;
                case UpgradeEffectType.PlayerAura:
                    // 캐릭터와 주위 파동 장판.
                    Dot(mesh, center, scale, tint, 0, 0, 3);
                    Ring(mesh, center, scale, tint, 0, 0, 8);
                    Arc(mesh, center, scale, tint, 0, 0, 14, 25, 155);
                    Arc(mesh, center, scale, tint, 0, 0, 14, 205, 335);
                    break;
                case UpgradeEffectType.FlyingSlash:
                    Path(mesh, center, scale, tint, -11, -11, 11, 11, 3, 9, -11, -11);
                    Arc(mesh, center, scale, tint, 0, 0, 14, 180, 320);
                    Path(mesh, center, scale, tint, 8, -10, 13, -8, 12, -14);
                    break;
                case UpgradeEffectType.ReturningLaunch:
                    Path(mesh, center, scale, tint, -14, 8, 12, 8, 6, 13);
                    Arc(mesh, center, scale, tint, 0, 0, 13, -155, 25);
                    Path(mesh, center, scale, tint, -8, 1, -13, 6, -14, 0);
                    break;
                case UpgradeEffectType.ReturningBurst:
                    Arc(mesh, center, scale, tint, 0, 0, 14, 25, 155);
                    Arc(mesh, center, scale, tint, 0, 0, 14, 205, 335);
                    Path(mesh, center, scale, tint, -14, 0, -4, 0, -8, 4);
                    Path(mesh, center, scale, tint, 14, 0, 4, 0, 8, -4);
                    break;
                case UpgradeEffectType.StationaryRegen:
                    // 정지 표식 두 줄과 회복 화살.
                    Path(mesh, center, scale, tint, -8, 10, -8, -10);
                    Path(mesh, center, scale, tint, -2, 10, -2, -10);
                    Path(mesh, center, scale, tint, 8, -8, 8, 8);
                    Path(mesh, center, scale, tint, 8, 8, 4, 3);
                    Path(mesh, center, scale, tint, 8, 8, 12, 3);
                    break;
                case UpgradeEffectType.SingleBehavior:
                    // 하나의 행동 칸과 자기 반복 화살.
                    Path(mesh, center, scale, tint, -14, -6, -14, 6, -3, 6, -3, -6, -14, -6);
                    Ring(mesh, center, scale, tint, 6, 0, 7);
                    Path(mesh, center, scale, tint, 13, 0, 9, 4);
                    Path(mesh, center, scale, tint, 13, 0, 9, -4);
                    break;
                case UpgradeEffectType.RightClickStart:
                    // 마우스 오른쪽 버튼 표시.
                    Path(mesh, center, scale, tint, -6, 13, -6, -6, 0, -13, 6, -6, 6, 13, -6, 13);
                    Path(mesh, center, scale, tint, 0, -13, 0, 0);
                    Path(mesh, center, scale, tint, -6, 0, 6, 0);
                    Dot(mesh, center, scale, tint, 3, -7, 2);
                    break;
                default:
                    DrawRune(mesh, center, scale, tint);
                    break;
            }
        }

        /// <summary>룬 ID별로 다른 실루엣을 그린다. 아직 등록되지 않은 룬만 분류별 대체 아이콘을 사용한다.</summary>
        private void DrawRune(VertexHelper mesh, Vector2 c, float s, Color t)
        {
            switch (_runeId)
            {
                case "element.fire":
                    Path(mesh,c,s,t, 0,16, -2,5, -8,10, -13,0, -11,-9, -5,-14, 5,-14, 12,-8, 13,0, 7,9, 7,1, 0,16);
                    Path(mesh,c,s,t, 0,-11, -4,-5, 0,2, 4,-5, 0,-11);
                    break;
                case "element.ice":
                    for (int i=0;i<6;i++)
                    {
                        float a=i*Mathf.PI/3;
                        Vector2 d=new Vector2(Mathf.Cos(a),Mathf.Sin(a));
                        Vector2 n=new Vector2(-d.y,d.x);
                        Path(mesh,c,s,t, 0,0, d.x*15,d.y*15);
                        Vector2 p=d*9;
                        Vector2 l=d*5+n*4;
                        Vector2 r=d*5-n*4;
                        Path(mesh,c,s,t, l.x,l.y, p.x,p.y, r.x,r.y);
                    }
                    break;
                case "element.lightning":
                    Path(mesh,c,s,t, 3,16, -11,-2, -1,-2, -4,-16, 12,4, 2,4, 3,16);
                    break;
                case "element.neutral":
                    Ring(mesh,c,s,t,0,0,13); Dot(mesh,c,s,t,0,0,3);
                    break;
                case "shape.sphere":
                    Ring(mesh,c,s,t,0,0,13);
                    Arc(mesh,c,s,t,0,0,7,-75,75);
                    Path(mesh,c,s,t,-12,0,12,0);
                    break;
                case "shape.box":
                    // 현재 사각 형상은 빔으로 표시하므로 평행 광선으로 표현한다.
                    Path(mesh,c,s,t,-14,-10,-14,10);
                    Path(mesh,c,s,t,-9,7,14,7);
                    Path(mesh,c,s,t,-9,0,17,0);
                    Path(mesh,c,s,t,-9,-7,14,-7);
                    break;
                case "shape.cone":
                    Path(mesh,c,s,t, 14,-12, -14,0, 14,12);
                    Arc(mesh,c,s,t,-14,0,30,-24,24);
                    Path(mesh,c,s,t,-7,0,9,0);
                    break;
                case "behavior.launch":
                    Path(mesh,c,s,t,-14,0,14,0,6,8);
                    Path(mesh,c,s,t,14,0,6,-8);
                    Path(mesh,c,s,t,-14,7,-5,7);
                    Path(mesh,c,s,t,-14,-7,-5,-7);
                    break;
                case "behavior.beam":
                    Path(mesh,c,s,t,-16,-2,16,-2);
                    Path(mesh,c,s,t,-16,2,16,2);
                    Path(mesh,c,s,t,-13,-7,-13,7);
                    break;
                case "behavior.burst":
                    Arc(mesh,c,s,t,0,0,14,0,360);
                    Path(mesh,c,s,t,-3,0,3,0);
                    break;
                case "mod.orbit":
                    Ring(mesh,c,s,t,0,0,12); Dot(mesh,c,s,t,0,0,4);
                    Dot(mesh,c,s,t,10,7,4);
                    Path(mesh,c,s,t,-15,-2,-11,-7,-6,-4);
                    break;
                case "behavior.persist":
                    Arc(mesh,c,s,t,0,-5,14,190,350);
                    Path(mesh,c,s,t,-12,-7,12,-7);
                    Path(mesh,c,s,t,-7,-2,-10,4,-7,12);
                    Path(mesh,c,s,t,1,-2,-2,4,1,14);
                    Path(mesh,c,s,t,9,-2,6,4,9,10);
                    break;
                case "mod.amplify":
                    Path(mesh,c,s,t,-13,-11,0,2,13,-11);
                    Path(mesh,c,s,t,-13,1,0,14,13,1);
                    break;
                case "mod.multi":
                    for(int y=-10;y<=10;y+=10)
                    { Path(mesh,c,s,t,-14,0,-4,y,12,y,7,y+4); Path(mesh,c,s,t,12,y,7,y-4); }
                    break;
                case "mod.pierce":
                    Path(mesh,c,s,t,-6,-12,-6,12);
                    Path(mesh,c,s,t,5,-12,5,12);
                    Path(mesh,c,s,t,-15,0,16,0,10,6);
                    Path(mesh,c,s,t,16,0,10,-6);
                    break;
                case "mod.homing":
                    Arc(mesh,c,s,t,-2,-3,11,0,220);
                    Path(mesh,c,s,t,9,-3,4,2);
                    Path(mesh,c,s,t,9,-3,14,2);
                    Dot(mesh,c,s,t,9,-13,3);
                    break;
                case "mod.expand":
                    Path(mesh,c,s,t,-12,4,-12,12,-4,12);
                    Path(mesh,c,s,t,12,-4,12,-12,4,-12);
                    Path(mesh,c,s,t,-12,12,-3,3);
                    Path(mesh,c,s,t,12,-12,3,-3);
                    break;
                case "mod.speed":
                    Path(mesh,c,s,t,-11,12,0,0,-11,-12);
                    Path(mesh,c,s,t,1,12,12,0,1,-12);
                    break;
                case "mod.duration":
                    Path(mesh,c,s,t,-10,14,10,14,10,8,-7,-8,-7,-14);
                    Path(mesh,c,s,t,-10,14,-10,8,7,-8,7,-14);
                    Path(mesh,c,s,t,-10,-14,10,-14);
                    break;
                default:
                    int sides = _runeCategory == "element" ? 3 : _runeCategory == "flow" ? 6 : 4;
                    RuneMesh.Ring(mesh,c,13*s,ICON_LINE_WIDTH*s,t,sides);
                    Plus(mesh,c,s,t,4);
                    break;
            }
        }

        /// <summary>CPU의 중앙 칩과 네 방향 연결 핀을 지정 위치·배율·색상으로 그린다.</summary>
        private static void DrawChip(VertexHelper mesh, Vector2 c, float s, Color t)
        {
            Path(mesh,c,s,t,-9,-9,-9,9,9,9,9,-9,-9,-9);
            for(int p=-5;p<=5;p+=5)
            {
                Path(mesh,c,s,t,p,9,p,14);
                Path(mesh,c,s,t,p,-9,p,-14);
                Path(mesh,c,s,t,9,p,14,p);
                Path(mesh,c,s,t,-9,p,-14,p);
            }
        }

        /// <summary>정규화된 XY 좌표 쌍을 순서대로 연결해 일정 두께의 윤곽선을 추가한다.</summary>
        private static void Path(VertexHelper mesh, Vector2 c, float s, Color t, params float[] points)
        {
            for(int i=2;i<points.Length;i+=2)
                RuneMesh.Line(mesh,c+new Vector2(points[i-2],points[i-1])*s,
                    c+new Vector2(points[i],points[i+1])*s,ICON_LINE_WIDTH*s,t);
        }

        /// <summary>중심과 반경을 지정한 원형 테두리를 아이콘 배율로 그린다.</summary>
        private static void Ring(VertexHelper mesh, Vector2 c, float s, Color t, float x, float y, float radius)
        {
            RuneMesh.Ring(mesh,c+new Vector2(x,y)*s,radius*s,ICON_LINE_WIDTH*s,t,32);
        }

        /// <summary>중심과 반경을 지정한 채워진 원을 아이콘 배율로 그린다.</summary>
        private static void Dot(VertexHelper mesh, Vector2 c, float s, Color t, float x, float y, float radius)
        {
            RuneMesh.Polygon(mesh,c+new Vector2(x,y)*s,radius*s,t,20);
        }

        /// <summary>지정 중심에 크기와 색상을 반영한 십자 표식을 추가한다.</summary>
        private static void Plus(VertexHelper mesh, Vector2 c, float s, Color t, float radius)
        {
            Path(mesh,c,s,t,-radius,0,radius,0);
            Path(mesh,c,s,t,0,-radius,0,radius);
        }

        /// <summary>도 단위 시작·끝 각도와 중심·반경을 사용해 열린 원호를 선분으로 그린다.</summary>
        private static void Arc(VertexHelper mesh, Vector2 c, float s, Color t, float x, float y, float radius, float from, float to)
        {
            const int SEGMENTS = 20;
            Vector2 origin=c+new Vector2(x,y)*s;
            float angle=from*Mathf.Deg2Rad;
            Vector2 previous=origin+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius*s;
            for(int i=1;i<=SEGMENTS;i++)
            {
                angle=Mathf.Lerp(from,to,(float)i/SEGMENTS)*Mathf.Deg2Rad;
                Vector2 next=origin+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius*s;
                RuneMesh.Line(mesh,previous,next,ICON_LINE_WIDTH*s,t);
                previous=next;
            }
        }
    }
}

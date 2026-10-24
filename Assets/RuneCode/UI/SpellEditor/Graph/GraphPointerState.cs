using System.Collections.Generic;

using UnityEngine;

namespace RuneCode
{
    /// <summary>
    /// 그래프 편집 화면의 선택·실행 하이라이트·포인터 조작 상태다. RuneGraphCanvas가 바꾸고 GraphMeshPainter가 읽는다.
    /// </summary>
    internal sealed class GraphPointerState
    {
        private readonly HashSet<string> _selected = new HashSet<string>();
        private readonly Dictionary<string, float> _executedUntil = new Dictionary<string, float>();
        private readonly Dictionary<string, bool[]> _connectablePorts = new Dictionary<string, bool[]>();
        private readonly Dictionary<string, HashSet<string>> _equippableSlots = new Dictionary<string, HashSet<string>>();
        private readonly Dictionary<string, Vector2> _dragOrigins = new Dictionary<string, Vector2>();

        /// <summary>선택된 노드 ID 집합이다.</summary>
        public HashSet<string> Selected => _selected;

        /// <summary>노드 ID별 실행 하이라이트 종료 시각(unscaled)이다.</summary>
        public Dictionary<string, float> ExecutedUntil => _executedUntil;

        /// <summary>연결 드래그 중 노드 ID별 포트 순번의 연결 가능 여부다.</summary>
        public Dictionary<string, bool[]> ConnectablePorts => _connectablePorts;

        /// <summary>속성·모디를 들고 있는 동안 노드 ID별로 장착 가능한 슬롯 키 집합이다.</summary>
        public Dictionary<string, HashSet<string>> EquippableSlots => _equippableSlots;

        /// <summary>노드 드래그 시작 시점의 선택 노드 그래프 좌표다.</summary>
        public Dictionary<string, Vector2> DragOrigins => _dragOrigins;

        /// <summary>현재 포인터의 캔버스 로컬 좌표다.</summary>
        public Vector2 Pointer { get; set; }

        /// <summary>현재 조작을 시작한 포인터의 캔버스 로컬 좌표다.</summary>
        public Vector2 DragStart { get; set; }

        /// <summary>화면 이동을 시작할 때의 화면 이동량이다.</summary>
        public Vector2 PanStart { get; set; }
        public string SnapFromNode { get; set; }
        public string SnapFromPort { get; set; }
        public string SnapToNode { get; set; }
        public string SnapInsertNode { get; set; }
        public Vector2 SnapOffset { get; set; }
        public bool IsSnapConnected { get; set; }

        /// <summary>가까운 퍼즐 결합 후보와 이동 보정량을 비운다.</summary>
        public void ClearSnap()
        {
            SnapFromNode = null;
            SnapFromPort = null;
            SnapToNode = null;
            SnapInsertNode = null;
            SnapOffset = Vector2.zero;
            IsSnapConnected = false;
        }

        /// <summary>화면 이동 중인지 나타낸다.</summary>
        public bool IsPanning { get; set; }

        /// <summary>영역 선택 중인지 나타낸다.</summary>
        public bool IsBoxSelecting { get; set; }

        /// <summary>노드 드래그 중인지 나타낸다.</summary>
        public bool IsNodeDragging { get; set; }

        /// <summary>연결 드래그를 시작한 노드 ID다. 연결 중이 아니면 null이다.</summary>
        public string SourceNode { get; set; }

        /// <summary>연결 드래그를 시작한 포트 ID다.</summary>
        public string SourcePort { get; set; }

        /// <summary>연결 드래그를 출력 포트에서 시작했는지 나타낸다.</summary>
        public bool IsSourceOutput { get; set; }

        /// <summary>연결 드래그 중인지 반환한다.</summary>
        public bool IsConnecting => SourceNode != null;

        /// <summary>조작 시작점과 현재 포인터를 포함하는 영역 선택 사각형을 반환한다.</summary>
        public Rect SelectionRect => Rect.MinMaxRect(Mathf.Min(DragStart.x, Pointer.x), Mathf.Min(DragStart.y, Pointer.y),
            Mathf.Max(DragStart.x, Pointer.x), Mathf.Max(DragStart.y, Pointer.y));

        /// <summary>연결·화면 이동·노드 드래그·영역 선택 상태와 연결 가능 캐시를 초기화한다. 선택과 하이라이트는 유지한다.</summary>
        public void ResetPointer()
        {
            ClearSnap();
            SourceNode = null;
            IsPanning = false;
            IsNodeDragging = false;
            IsBoxSelecting = false;
            _connectablePorts.Clear();
        }
    }
}

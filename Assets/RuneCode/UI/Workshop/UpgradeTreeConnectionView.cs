using UnityEngine;

namespace RuneCode
{
    /// <summary>프리팹에 배치된 두 노드의 중심을 편집·실행 중에 연결하고 학습 상태에 맞춰 선을 표시한다.</summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform), typeof(UiStateVisual))]
    public sealed class UpgradeTreeConnectionView : MonoBehaviour
    {
        [Header("연결할 노드")]
        [SerializeField] private UpgradeTreeNodeView _source;
        [SerializeField] private UpgradeTreeNodeView _target;

        [Header("선 표시")]
        [SerializeField] private UiStateVisual _visual;
        [SerializeField, Min(0.1f)] private float _lineWidth = 2.5f;

        public string SourceNodeId => _source == null ? null : _source.NodeId;
        public string TargetNodeId => _target == null ? null : _target.NodeId;

#if UNITY_EDITOR
        void OnEnable()
        {
            if (!Application.IsPlaying(gameObject)) UnityEditor.EditorApplication.update += RefreshLayout;
        }

        void OnDisable()
        {
            UnityEditor.EditorApplication.update -= RefreshLayout;
        }
#endif

        void LateUpdate()
        {
            RefreshLayout();
        }

        /// <summary>연결된 노드의 실제 RectTransform 중심으로 선 위치·길이·각도를 갱신한다. 참조가 없는 원본 프리팹은 건너뛴다.</summary>
        public void RefreshLayout()
        {
            if (_source == null || _target == null || transform.parent == null) return;
            RectTransform sourceRect = (RectTransform)_source.transform;
            RectTransform targetRect = (RectTransform)_target.transform;
            RectTransform lineRect = (RectTransform)transform;
            Vector2 start = transform.parent.InverseTransformPoint(sourceRect.TransformPoint(sourceRect.rect.center));
            Vector2 end = transform.parent.InverseTransformPoint(targetRect.TransformPoint(targetRect.rect.center));
            Vector2 delta = end - start;
            Vector3 midpoint = (start + end) * 0.5f;
            Vector2 size = new Vector2(delta.magnitude, _lineWidth);
            Quaternion rotation = Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            if (lineRect.localPosition != midpoint) lineRect.localPosition = midpoint;
            if (lineRect.sizeDelta != size) lineRect.sizeDelta = size;
            if (Quaternion.Angle(lineRect.localRotation, rotation) > 0.001f) lineRect.localRotation = rotation;
        }

        /// <summary>양 끝 노드가 모두 학습됐는지 받아 연결선의 강조 상태를 바꾼다.</summary>
        public void SetLearned(bool isLearned)
        {
            _visual.Show(isLearned ? 1 : 0);
        }

        /// <summary>런타임 연결선 생성 시 양 끝 노드를 지정하고 선 위치를 즉시 갱신한다.</summary>
        public void Configure(UpgradeTreeNodeView source, UpgradeTreeNodeView target)
        {
            _source = source;
            _target = target;
            RefreshLayout();
        }
    }
}

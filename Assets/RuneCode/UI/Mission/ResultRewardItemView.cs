using System;
using System.Collections.Generic;

using UnityEngine;

using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RuneCode
{
    /// <summary>결과 보상 아이콘에 표시할 분류, 수량, 이름과 설명을 보관한다.</summary>
    public sealed class ResultRewardContent
    {
        private readonly bool _isModifier;
        private readonly int _count;
        private readonly string _title;
        private readonly string _description;
        private readonly string _runeId;
        private readonly string _grade;

        public bool IsModifier => _isModifier;
        public int Count => _count;
        public string Title => _title;
        public string Description => _description;
        public string RuneId => _runeId;
        public string Grade => _grade;

        /// <summary>중앙 보상 표시용 아이콘의 종류와 문구를 저장한다.</summary>
        public ResultRewardContent(bool isModifier, int count, string title, string description, string runeId = null, string grade = null)
        {
            _isModifier = isModifier;
            _count = count;
            _title = title;
            _description = description;
            _runeId = runeId;
            _grade = grade;
        }
    }

    /// <summary>결과 패널의 보상 아이콘·수량을 표시하고 포인터 호버를 상위 View에 전달한다.</summary>
    public sealed class ResultRewardItemView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private const float BURST_SECONDS = 0.65f;
        private const float MIN_CARD_WIDTH = 96f;
        private const float CARD_TEXT_PADDING = 28f;
        private const float FLASH_SECONDS = 0.16f;
        private const float S_REVEAL_DELAY = 0.12f;
        private static readonly Color _fragmentBackground = new Color(0.25f, 0.20f, 0.12f, 1f);
        private static readonly Color _fragmentIcon = new Color(0.98f, 0.79f, 0.37f, 1f);
        private static readonly Color _countBadgeBackground = new Color(0.035f, 0.05f, 0.08f, 0.96f);

        [Header("아이콘 및 수량")]
        [SerializeField] private Image _background;
        [SerializeField] private UpgradeTreeNodeIconGraphic _icon;
        [SerializeField] private Image _countBadge;
        [SerializeField] private TextMeshProUGUI _countLabel;

        private bool _isModifier;
        private string _title;
        private string _description;
        private Action<ResultRewardItemView, bool> _hoverAction;
        private readonly List<Image> _particles = new List<Image>();
        private CanvasGroup _visibility;
        private string _grade;
        private float _burstStarted = -1f;
        private Color _burstColor;
        private readonly List<Image> _glows = new List<Image>();
        private readonly List<Image> _sparkles = new List<Image>();
        private RectTransform _paletteRect;
        private Image _paletteBackground;
        private Image _flash;
        private int _gradeRank;
        private float _effectSeconds;
        private float _preferredWidth = 72f;

        /// <summary>보상 분류와 툴팁에 필요한 이름·설명을 반환한다.</summary>
        public bool IsModifier => _isModifier;
        public string Title => _title;
        public string Description => _description;
        public float PreferredWidth => _preferredWidth;
        public bool IsEffectPlaying => _burstStarted >= 0;

        /// <summary>보상 종류와 x수량을 표시하고 포인터 진입·이탈을 상위 결과 View에 연결한다.</summary>
        public void SetContent(ResultRewardContent content, Action<ResultRewardItemView, bool> hoverAction)
        {
            _isModifier = content.IsModifier;
            _title = content.Title;
            _description = content.Description;
            _hoverAction = hoverAction;
            _grade = content.Grade;
            _visibility = gameObject.AddComponent<CanvasGroup>();
            if (_isModifier)
            {
                SetPaletteContent(content);
                return;
            }
            _background.color = _fragmentBackground;
            _icon.Configure(UpgradeEffectType.ScrapGain, null);
            _icon.color = _fragmentIcon;
            _countBadge.color = _countBadgeBackground;
            _countLabel.text = "x" + content.Count;
        }

        /// <summary>기존 아이콘을 숨기고 팔레트의 실제 분류별 행 프리팹에 이름·등급만 표시한다.</summary>
        private void SetPaletteContent(ResultRewardContent content)
        {
            RuneDefinition rune = GameData.Runes.Get(content.RuneId);
            string category = rune.Category == SpellGrammar.CATEGORY_BEHAVIOR ? "Behavior"
                : rune.Category == SpellGrammar.CATEGORY_ELEMENT ? "Element" : "Modifier";
            UiRow prefab = Resources.Load<UiRow>("RuneCode/UI/Elements/RunePalette" + category + "Row");
            UiRow row = Instantiate(prefab, transform, false);
            string name = string.IsNullOrEmpty(content.Grade) ? rune.Name : rune.Name + " " + content.Grade;
            string label = ModifierGradeText.Colorize(name, content.Grade);
            row.Configure(label, SpellPalettePresenter.ROW_HEIGHT, () => { }, 12);
            row.Label.font = _countLabel.font;
            row.Label.enableAutoSizing = false;
            row.Label.textWrappingMode = TextWrappingModes.NoWrap;
            _preferredWidth = Mathf.Max(MIN_CARD_WIDTH,
                Mathf.Ceil(row.Label.GetPreferredValues(label, Mathf.Infinity, Mathf.Infinity).x) + CARD_TEXT_PADDING);
            row.Button.navigation = new Navigation { mode = Navigation.Mode.None };
            RectTransform rect = (RectTransform)row.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(_preferredWidth, SpellPalettePresenter.ROW_HEIGHT);
            _paletteRect = rect;
            _paletteBackground = (Image)row.Button.targetGraphic;
            _background.enabled = false;
            _icon.gameObject.SetActive(false);
            _countBadge.gameObject.SetActive(false);
            _countLabel.gameObject.SetActive(false);
        }

        /// <summary>레이아웃 영역을 유지하며 미공개 보상과 포인터 입력을 숨긴다.</summary>
        public void Conceal()
        {
            _visibility.alpha = 0;
            _visibility.blocksRaycasts = false;
        }

        /// <summary>룬을 순간 발광과 함께 공개한다. A·S는 후광과 반짝임을 강화하고 S는 짧은 지연 뒤 두 번 발광한다.</summary>
        public void Reveal(bool playEffect, Color gradeColor)
        {
            _visibility.alpha = 1;
            _visibility.blocksRaycasts = true;
            if (!playEffect || !_isModifier) return;
            int rank = Mathf.Max(0, Array.IndexOf(new[] { "C", "B", "A", "S" }, _grade));
            _gradeRank = rank;
            _effectSeconds = rank == 3 ? 1.2f : rank == 2 ? 0.9f : BURST_SECONDS;
            _burstColor = gradeColor;
            _burstStarted = Time.unscaledTime;
            if (rank == 3) _visibility.alpha = 0;
            _flash = CreateHighlight("RuneFlash", _paletteRect.sizeDelta, false);
            if (rank >= 2)
            {
                for (int index = 0; index < (rank == 3 ? 5 : 3); index++)
                    _glows.Add(CreateHighlight("GradeGlow", _paletteRect.sizeDelta + Vector2.one * (8f + index * 7f), true));
                for (int index = 0; index < (rank == 3 ? 16 : 8); index++)
                {
                    Image sparkle = CreateHighlight("GradeSparkle", new Vector2(2f, 11f), false);
                    sparkle.sprite = null;
                    _sparkles.Add(sparkle);
                }
            }
            if (string.IsNullOrEmpty(_grade)) return;
            for (int index = 0; index < 6 + rank * 6; index++)
            {
                var particle = new GameObject("GradeParticle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
                particle.transform.SetParent(transform, false);
                particle.raycastTarget = false;
                particle.rectTransform.sizeDelta = Vector2.one * (3f + rank);
                _particles.Add(particle);
            }
        }

        void Update()
        {
            if (_burstStarted < 0) return;
            float elapsed = Time.unscaledTime - _burstStarted - (_gradeRank == 3 ? S_REVEAL_DELAY : 0);
            _visibility.alpha = elapsed < 0 ? 0 : 1;
            float flash = Mathf.Clamp01(1f - elapsed / FLASH_SECONDS);
            if (_gradeRank == 3 && elapsed >= 0.24f)
                flash = Mathf.Clamp01(1f - (elapsed - 0.24f) / FLASH_SECONDS) * 0.75f;
            Color flashTint = Color.Lerp(_burstColor, Color.white, 0.8f);
            flashTint.a = flash * (_gradeRank >= 2 ? 0.9f : 0.65f);
            _flash.color = flashTint;
            float highlight = Mathf.Clamp01(elapsed / _effectSeconds);
            for (int index = 0; index < _glows.Count; index++)
            {
                Color tint = _burstColor;
                tint.a = (1f - highlight) * (_gradeRank == 3 ? 0.2f : 0.14f) / (index + 1);
                _glows[index].color = tint;
            }
            for (int index = 0; index < _sparkles.Count; index++)
            {
                float angle = (index / 2) * Mathf.PI * 4 / _sparkles.Count;
                _sparkles[index].rectTransform.anchoredPosition = new Vector2(Mathf.Cos(angle) * (_preferredWidth * 0.5f + 6f),
                    Mathf.Sin(angle) * (SpellPalettePresenter.ROW_HEIGHT * 0.5f + 6f));
                _sparkles[index].rectTransform.localRotation = Quaternion.Euler(0, 0, 45f + index % 2 * 90f);
                Color tint = Color.Lerp(_burstColor, Color.white, 0.7f);
                tint.a = Mathf.Max(0, Mathf.Sin(Mathf.Max(0, elapsed) * 16f + index / 2f)) * (1f - highlight);
                _sparkles[index].color = tint;
            }
            float progress = Mathf.Clamp01(elapsed / BURST_SECONDS);
            for (int index = 0; index < _particles.Count; index++)
            {
                Image particle = _particles[index];
                float angle = index * Mathf.PI * 2 / _particles.Count;
                float radius = 15f + progress * (20f + _particles.Count);
                float verticalRadius = Mathf.Min(radius,
                    (((RectTransform)transform).rect.height - particle.rectTransform.sizeDelta.y) * 0.5f);
                particle.rectTransform.anchoredPosition = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * verticalRadius);
                particle.rectTransform.localRotation = Quaternion.Euler(0, 0, index * 37f + progress * 90f);
                Color tint = _burstColor;
                tint.a = 1f - progress;
                particle.color = tint;
            }
            if (elapsed < _effectSeconds) return;
            CompleteEffect();
        }

        /// <summary>팔레트 배경 모양을 사용하는 비입력용 발광 이미지를 카드 앞 또는 뒤에 만든다.</summary>
        private Image CreateHighlight(string label, Vector2 size, bool isBehind)
        {
            var image = new GameObject(label, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(transform, false);
            image.sprite = _paletteBackground.sprite;
            image.type = _paletteBackground.type;
            image.raycastTarget = false;
            image.color = Color.clear;
            image.rectTransform.sizeDelta = size;
            if (isBehind) image.transform.SetAsFirstSibling();
            return image;
        }

        /// <summary>재생 중인 등급 파티클을 제거하고 공개 효과를 완료 상태로 만든다.</summary>
        public void CompleteEffect()
        {
            foreach (Image particle in _particles) Destroy(particle.gameObject);
            _particles.Clear();
            foreach (Image glow in _glows) Destroy(glow.gameObject);
            _glows.Clear();
            foreach (Image sparkle in _sparkles) Destroy(sparkle.gameObject);
            _sparkles.Clear();
            if (_flash != null) Destroy(_flash.gameObject);
            _visibility.alpha = 1;
            _burstStarted = -1;
        }

        /// <summary>포인터가 아이콘에 들어오면 보상 종류와 툴팁 정보를 상위 View로 전달한다.</summary>
        public void OnPointerEnter(PointerEventData eventData)
        {
            _hoverAction?.Invoke(this, true);
        }

        /// <summary>포인터가 아이콘을 벗어나면 상위 View에 툴팁 종료를 알린다.</summary>
        public void OnPointerExit(PointerEventData eventData)
        {
            _hoverAction?.Invoke(this, false);
        }
    }
}

using UnityEngine;

using TMPro;
using UnityEngine.UI;

namespace RuneCode
{
    /// <summary>각 음량 초안과 퍼센트를 표시하고 저장할 때만 런타임과 기기 공통 설정에 적용하는 팝업이다.</summary>
    public sealed class SoundSettingsPopup : UiPopup
    {
        [Header("음량")]
        [SerializeField] private Slider _masterVolumeSlider;
        [SerializeField] private Slider _BGMVolumeSlider;
        [SerializeField] private Slider _SFXVolumeSlider;

        [Header("음량 수치")]
        [SerializeField] private TextMeshProUGUI _masterVolumeLabel;
        [SerializeField] private TextMeshProUGUI _bgmVolumeLabel;
        [SerializeField] private TextMeshProUGUI _sfxVolumeLabel;

        [Header("버튼")]
        [SerializeField] private Button _saveButton;

        void Awake()
        {
            _saveButton.onClick.AddListener(Save);
            _masterVolumeSlider.onValueChanged.AddListener(_ => RefreshVolumeLabels());
            _BGMVolumeSlider.onValueChanged.AddListener(_ => RefreshVolumeLabels());
            _SFXVolumeSlider.onValueChanged.AddListener(_ => RefreshVolumeLabels());
        }

        /// <summary>팝업을 열 때 현재 런타임 음량으로 저장 전 초안과 퍼센트 표시를 다시 초기화한다.</summary>
        protected override void OnShown()
        {
            _masterVolumeSlider.SetValueWithoutNotify(SoundManager.Instance.MasterVolume);
            _BGMVolumeSlider.SetValueWithoutNotify(SoundManager.Instance.BGMVolume);
            _SFXVolumeSlider.SetValueWithoutNotify(SoundManager.Instance.SFXVolume);
            RefreshVolumeLabels();
        }

        /// <summary>각 슬라이더의 현재 초안을 0~100의 정수 퍼센트로 표시한다.</summary>
        private void RefreshVolumeLabels()
        {
            _masterVolumeLabel.text = Mathf.RoundToInt(_masterVolumeSlider.value * 100f) + "%";
            _bgmVolumeLabel.text = Mathf.RoundToInt(_BGMVolumeSlider.value * 100f) + "%";
            _sfxVolumeLabel.text = Mathf.RoundToInt(_SFXVolumeSlider.value * 100f) + "%";
        }

        /// <summary>슬라이더 값을 한 번 읽어 FMOD 마스터 버스와 기기 공통 환경설정에 확정한다.</summary>
        private void Save()
        {
            float masterVolume = _masterVolumeSlider.value;
            float bgmVolume = _BGMVolumeSlider.value;
            float sfxVolume = _SFXVolumeSlider.value;
            SoundManager.Instance.SetVolumes(masterVolume, bgmVolume, sfxVolume);
            SaveStore.SetVolumes(masterVolume, bgmVolume, sfxVolume);


            SoundManager.Instance.PlayUnlockSkillTree();
        }
    }
}

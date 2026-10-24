using UnityEngine;

using UnityEngine.UI;

namespace RuneCode
{
    /// <summary>마스터 음량 초안을 편집하고 저장할 때만 런타임과 기기 공통 설정에 적용하는 팝업이다.</summary>
    public sealed class SoundSettingsPopup : UiPopup
    {
        [Header("음량")]
        [SerializeField] private Slider _masterVolumeSlider;
        [SerializeField] private Slider _BGMVolumeSlider;
        [SerializeField] private Slider _SFXVolumeSlider;


        [Header("버튼")]
        [SerializeField] private Button _saveButton;

        void Awake()
        {
            _saveButton.onClick.AddListener(Save);
        }

        /// <summary>팝업을 열 때 현재 런타임 마스터 음량으로 저장 전 초안을 다시 초기화한다.</summary>
        protected override void OnShown()
        {
            _masterVolumeSlider.SetValueWithoutNotify(SoundManager.Instance.MasterVolume);
            _BGMVolumeSlider.SetValueWithoutNotify(SoundManager.Instance.BGMVolume);
            _SFXVolumeSlider.SetValueWithoutNotify(SoundManager.Instance.SFXVolume);
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

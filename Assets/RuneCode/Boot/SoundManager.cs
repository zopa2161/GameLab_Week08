using UnityEngine;

using FMODUnity;
using FMOD.Studio;

using RuneCode;

public class SoundManager : MonoBehaviour
{
    public float masterVolume = 1f;
    public float sfxVolume = 1f;
    public float musicVolume = 1f;

    private Bus masterBus;
    private Bus sfxBus;
    private Bus musicBus;

    private const string MUSIC_STATE = "event:/BGM/MainMusic";


    private const string ENEMY_HIT_SOUND = "event:/SFX/Enemy/EnemyHit";

    private const string ROUND_CLEAR_SOUND = "event:/SFX/GameEvent/RoundClear";
    private const string ROUND_DEFEAT_SOUND = "event:/SFX/GameEvent/RoundDefeat";

    private const string SHOT_LAUNCH_SOUND = "event:/SFX/Player/Behavior/ShotLaunch";
    private const string SHOT_BEAM_SOUND = "event:/SFX/Player/Behavior/ShotBeam";
    private const string SHOT_PERSIST_SOUND = "event:/SFX/Player/Behavior/ShotPersist";
    private const string SHOT_BURST_SOUND = "event:/SFX/Player/Behavior/ShotBurst";
    private const string PLAYER_HIT_SOUND = "event:/SFX/Player/Hit";
    private const string TAKE_BIG_SCRAP_SOUND = "event:/SFX/Player/TakeBigScrap";
    private const string TAKE_SCRAP_SOUND = "event:/SFX/Player/TakeScrap";
    private const string BURN_SOUND = "event:/SFX/Player/Elemental/Burn";
    private const string CHAIN_SOUND = "event:/SFX/Player/Elemental/Chain";
    private const string FREEZE_SOUND = "event:/SFX/Player/Elemental/Freeze";


    private const string BUTTON_CLICK_SOUND = "event:/SFX/UI/ClickButton";
    private const string BUTTON_SLOT_SOUND = "event:/SFX/UI/HoverButton";
    private const string DISABLE_SOUND = "event:/SFX/UI/Disable";
    private const string SELECT_SLOT_ITEM_SOUND = "event:/SFX/UI/SelectSlotItem";
    private const string UNLOCK_SKILL_TREE_SOUND = "event:/SFX/UI/UnlockSkillTree";
    private const string UPGRADE_SKILL_TREE_SOUND = "event:/SFX/UI/UpgradeSkillTree";

    private EventInstance _musicInstance;
    private EventInstance _shotBeamInstance;

    public static SoundManager Instance { get; private set; }

    public float MasterVolume => masterVolume;
    public float BGMVolume => musicVolume;
    public float SFXVolume => sfxVolume;

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        masterBus = RuntimeManager.GetBus("bus:/");
        sfxBus = RuntimeManager.GetBus("bus:/SFX");
        musicBus = RuntimeManager.GetBus("bus:/BGM");

        SetVolumes(SaveStore.MasterVolume, SaveStore.BGMVolume, SaveStore.SFXVolume);

        _musicInstance = RuntimeManager.CreateInstance(MUSIC_STATE);
        SetMusicState(0);

    }

    /// <summary>확정한 음량을 0부터 1로 제한하고 FMOD 마스터 버스와 현재 음량에 한 번 적용한다.</summary>
    public void SetVolumes(float masterVolume, float bgmVolume, float sfxVolume)
    {
        this.masterVolume = Mathf.Clamp01(masterVolume);
        masterBus.setVolume(this.masterVolume);
        this.musicVolume = Mathf.Clamp01(bgmVolume);
        musicBus.setVolume(this.musicVolume);
        this.sfxVolume = Mathf.Clamp01(sfxVolume);
        sfxBus.setVolume(this.sfxVolume);
    }

    void OnDestroy()
    {
        if (Instance != this) return;
        StopShotBeam();
        Instance = null;
    }

    /// <summary>적 피격 효과음을 한 번 재생한다.</summary>
    public void PlayEnemyHit()
    {
        RuntimeManager.PlayOneShot(ENEMY_HIT_SOUND);
    }

    /// <summary>라운드 승리 효과음을 한 번 재생한다.</summary>
    public void PlayRoundClear()
    {
        RuntimeManager.PlayOneShot(ROUND_CLEAR_SOUND);
    }

    /// <summary>라운드 패배 효과음을 한 번 재생한다.</summary>
    public void PlayRoundDefeat()
    {
        RuntimeManager.PlayOneShot(ROUND_DEFEAT_SOUND);
    }

    /// <summary>발사 행동의 시전 효과음을 한 번 재생한다.</summary>
    public void PlayShotLaunch()
    {
        RuntimeManager.PlayOneShot(SHOT_LAUNCH_SOUND);
    }

    /// <summary>빔 행동의 시전 효과음을 한 번 재생한다.</summary>
    public void PlayShotBeam()
    {
        RuntimeManager.PlayOneShot(SHOT_BEAM_SOUND);
    }

    /// <summary>잔류 행동의 시전 효과음을 한 번 재생한다.</summary>
    public void PlayShotPersist()
    {
        RuntimeManager.PlayOneShot(SHOT_PERSIST_SOUND);
    }

    /// <summary>폭발 행동의 시전 효과음을 한 번 재생한다.</summary>
    public void PlayShotBurst()
    {
        RuntimeManager.PlayOneShot(SHOT_BURST_SOUND);
    }

    /// <summary>플레이어 피격 효과음을 한 번 재생한다.</summary>
    public void PlayPlayerHit()
    {
        RuntimeManager.PlayOneShot(PLAYER_HIT_SOUND);
    }

    /// <summary>큰 스크랩 획득 효과음을 한 번 재생한다.</summary>
    public void PlayTakeBigScrap()
    {
        RuntimeManager.PlayOneShot(TAKE_BIG_SCRAP_SOUND);
    }

    /// <summary>일반 스크랩 획득 효과음을 한 번 재생한다.</summary>
    public void PlayTakeScrap()
    {
        RuntimeManager.PlayOneShot(TAKE_SCRAP_SOUND);
    }

    /// <summary>UI 버튼 클릭 효과음을 한 번 재생한다.</summary>
    public void PlayClickButton()
    {
        RuntimeManager.PlayOneShot(BUTTON_CLICK_SOUND);
    }

    /// <summary>UI 버튼 호버 효과음을 한 번 재생한다.</summary>
    public void PlaySlot()
    {
        RuntimeManager.PlayOneShot(BUTTON_SLOT_SOUND);
    }

    /// <summary>잠긴 스킬 트리를 선택한 효과음을 한 번 재생한다.</summary>
    public void PlayDisable()
    {
        RuntimeManager.PlayOneShot(DISABLE_SOUND);
    }

    /// <summary>슬롯 아이템 선택 효과음을 한 번 재생한다.</summary>
    public void PlaySelectSlotItem()
    {
        RuntimeManager.PlayOneShot(SELECT_SLOT_ITEM_SOUND);
    }

    /// <summary>스킬 트리 해금 효과음을 한 번 재생한다.</summary>
    public void PlayUnlockSkillTree()
    {
        RuntimeManager.PlayOneShot(UNLOCK_SKILL_TREE_SOUND);
    }

    /// <summary>스킬 트리 강화 효과음을 한 번 재생한다.</summary>
    public void PlayUpgradeSkillTree()
    {
        RuntimeManager.PlayOneShot(UPGRADE_SKILL_TREE_SOUND);
    }

    public void PlayBurn()
    {
        //RuntimeManager.PlayOneShot(BURN_SOUND);
    }

    public void PlayChain()
    {
        RuntimeManager.PlayOneShot(CHAIN_SOUND);
    }

    public void PlayFreeze()
    {
        RuntimeManager.PlayOneShot(FREEZE_SOUND);
    }

    public void StartShotBeam()
    {
        if (_shotBeamInstance.isValid())
            return;

        _shotBeamInstance = RuntimeManager.CreateInstance(SHOT_BEAM_SOUND);
        _shotBeamInstance.start();
    }

    public void StopShotBeam()
    {
        if (!_shotBeamInstance.isValid())
            return;

        _shotBeamInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        _shotBeamInstance.release();
        _shotBeamInstance = default;
    }

    /// <summary>
    /// 지정한 인덱스에 해당하는 음악 상태로 전환합니다.
    /// </summary>
    /// <remarks>index가 유효한 범위(0 ≤ index < 컬렉션 크기)를 벗어나면 ArgumentOutOfRangeException이 발생할 수 있습니다.</remarks>
    /// <param name="index">0 = 기본 음악, 1 = 전투 음악</param>
    public void SetMusicState(int index)
    {
        _musicInstance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);

        switch (index)
        {
            case 0:
                _musicInstance.setParameterByName("GameState", 0);
                //RuntimeManager.StudioSystem.setParameterByName("GameState", 0);
                break;
            case 1:
                _musicInstance.setParameterByName("GameState", 1);
                //RuntimeManager.StudioSystem.setParameterByName("GameState", 1);
                break;
            default:
                throw new System.ArgumentOutOfRangeException(nameof(index), "음악 인덱스 오류");
        }
        _musicInstance.start();
    }


}

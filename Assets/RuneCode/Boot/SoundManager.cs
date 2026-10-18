using UnityEngine;

using FMODUnity;

public class SoundManager : MonoBehaviour
{
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

    public static SoundManager Instance { get; private set; }

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
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
        //RuntimeManager.PlayOneShot(BUTTON_CLICK_SOUND);
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
        PlaySelectSlotItem();
        //RuntimeManager.PlayOneShot(UPGRADE_SKILL_TREE_SOUND);
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
}

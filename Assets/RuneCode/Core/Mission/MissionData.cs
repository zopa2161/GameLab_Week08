using System;
using System.Collections.Generic;

using UnityEngine;

namespace RuneCode
{
    [Serializable]
    public sealed class EnemyDefinition
    {
        public const string MOVEMENT_CHASE = "chase";
        public const string MOVEMENT_TURRET = "turret";
        public const string MOVEMENT_ANCHOR = "anchor";
        public const string MOVEMENT_DASH_CHASE = "dashChase";
        public const string MOVEMENT_KEEP_DISTANCE = "keepDistance";
        public const string MOVEMENT_SNIPER = "sniper";
        public const string MOVEMENT_INTERCEPTOR = "interceptor";
        public const string MOVEMENT_TURN_CHASE = "turnChase";
        public const string ATTACK_NONE = "none";
        public const string ATTACK_CONTACT = "contact";
        public const string ATTACK_BURST = "burst";
        public const string ATTACK_DASH_CONTACT = "dashContact";
        public const string ATTACK_SNIPER = "sniper";
        public const string ATTACK_INTERCEPTOR = "interceptor";
        public const string TRAIT_AEGIS_SHIELD = "aegisShield";
        public const string TRAIT_RELAY_AURA = "relayAura";
        public const string TRAIT_DEATH_EXPLOSION = "deathExplosion";
        public const string TRAIT_CIRCLE_SHIELD = "circleShield";
        public const string TRAIT_CARRIER = "carrier";

        private static readonly string[] MOVEMENT_TYPES = { MOVEMENT_CHASE, MOVEMENT_TURRET, MOVEMENT_ANCHOR, MOVEMENT_DASH_CHASE, MOVEMENT_KEEP_DISTANCE, MOVEMENT_SNIPER, MOVEMENT_INTERCEPTOR, MOVEMENT_TURN_CHASE };
        private static readonly string[] ATTACK_TYPES = { ATTACK_NONE, ATTACK_CONTACT, ATTACK_BURST, ATTACK_DASH_CONTACT, ATTACK_SNIPER, ATTACK_INTERCEPTOR };
        private static readonly string[] TRAIT_TYPES = { TRAIT_AEGIS_SHIELD, TRAIT_RELAY_AURA, TRAIT_DEATH_EXPLOSION, TRAIT_CIRCLE_SHIELD, TRAIT_CARRIER };

        [Header("적 설정")]
        [SerializeField] private string _id;
        [SerializeField] private double _hp;
        [SerializeField] private double _speed;
        [SerializeField] private double _radius;
        [SerializeField] private double _damage;
        [SerializeField] private double _attackInterval;
        [SerializeField] private double _warningSeconds;
        [SerializeField] private double _attackRange;
        [SerializeField] private double _dashSpeed;
        [SerializeField] private double _dashSeconds;
        [SerializeField] private double _projectileSpeed;
        [SerializeField] private double _projectileRadius;
        [SerializeField] private double _projectileLifetime;
        [SerializeField] private double _spreadDegrees;
        [SerializeField] private double _burstInterval;
        [SerializeField] private int _reward;
        [SerializeField] private string _splitInto;
        [SerializeField] private int _splitCount;
        [SerializeField] private double _splitDelay;
        [SerializeField] private string _movement;
        [SerializeField] private string[] _traits;
        [SerializeField] private string _attack;
        [SerializeField] private string _dropTable;
        [SerializeField] private int _sides;
        [SerializeField] private bool _isHidden;

        [Header("추가 적 행동")]
        [SerializeField] private double _explosionRadius;
        [SerializeField] private double _explosionDamage;
        [SerializeField] private double _shieldRadius;
        [SerializeField] private double _aimSeconds;
        [SerializeField] private double _lockSeconds;
        [SerializeField] private double _moveSeconds;
        [SerializeField] private double _stopSeconds;
        [SerializeField] private double _preferredDistance;
        [SerializeField] private double _turnSeconds;
        public double ExplosionRadius => _explosionRadius;
        public double ExplosionDamage => _explosionDamage;
        public double ShieldRadius => _shieldRadius;
        public double AimSeconds => _aimSeconds;
        public double LockSeconds => _lockSeconds;
        public double MoveSeconds => _moveSeconds;
        public double StopSeconds => _stopSeconds;
        public double PreferredDistance => _preferredDistance;
        /// <summary>180도 방향 전환에 걸리는 시간(초)이다. 데이터에 기재하지 않으면(0) 즉시 방향을 바꾼다.</summary>
        public double TurnSeconds => _turnSeconds;

        [Header("캐리어 생성")]
        [SerializeField] private string[] _carrierTargets;
        [SerializeField] private double _carrierInterval;
        [SerializeField] private int _carrierCount;
        [SerializeField] private double _carrierDistance;
        [SerializeField] private int _carrierMaxAlive;
        public IReadOnlyList<string> CarrierTargets => _carrierTargets ?? Array.Empty<string>();
        public double CarrierInterval => _carrierInterval;
        public int CarrierCount => _carrierCount;
        public double CarrierDistance => _carrierDistance;
        public int CarrierMaxAlive => _carrierMaxAlive;
        public string Id => _id;
        public double Hp => _hp;
        public double Speed => _speed;
        public double Radius => _radius;
        public double Damage => _damage;
        public double AttackInterval => _attackInterval;
        public double WarningSeconds => _warningSeconds;
        public double AttackRange => _attackRange;
        public double DashSpeed => _dashSpeed;
        public double DashSeconds => _dashSeconds;
        public double ProjectileSpeed => _projectileSpeed;
        public double ProjectileRadius => _projectileRadius;
        public double ProjectileLifetime => _projectileLifetime;
        public double SpreadDegrees => _spreadDegrees;
        public double BurstInterval => _burstInterval;
        public int Reward => _reward;
        public string SplitInto => _splitInto;
        public int SplitCount => _splitCount;
        public double SplitDelay => _splitDelay;
        public bool CanSplit => !string.IsNullOrEmpty(_splitInto) && _splitCount > 0;
        public string Movement => _movement;
        public string DropTable => _dropTable;
        public int Sides => _sides;
        /// <summary>소환·분열로만 등장해 편집 도구의 적 선택 목록에서 숨길 적인지 반환한다.</summary>
        public bool IsHidden => _isHidden;
        public string Attack => string.IsNullOrEmpty(_attack) ? ATTACK_CONTACT : _attack;

        /// <summary>이 적이 지정 특수 능력을 정의에 포함하는지 반환한다.</summary>
        public bool HasTrait(string trait)
        {
            if (_traits == null) return false;
            for (int i = 0; i < _traits.Length; i++) if (_traits[i] == trait) return true;
            return false;
        }

        /// <summary>이동·공격 방식과 특수 능력 값이 정의된 어휘 안에 있는지 검사하고 모르는 값에는 데이터 오류를 발생시킨다.</summary>
        public void Validate()
        {
            if (Array.IndexOf(MOVEMENT_TYPES, _movement) < 0) throw new FormatException("알 수 없는 적 이동 방식입니다: " + _id + ", " + _movement);
            if (Array.IndexOf(ATTACK_TYPES, Attack) < 0) throw new FormatException("알 수 없는 적 공격 방식입니다: " + _id + ", " + _attack);
            ValidateAdditionalSettings();
            if (_traits == null) return;
            foreach (string trait in _traits)
                if (Array.IndexOf(TRAIT_TYPES, trait) < 0) throw new FormatException("알 수 없는 적 특수 능력입니다: " + _id + ", " + trait);
        }

        /// <summary>새 적 행동의 수치와 이동·공격 조합을 검증하고 잘못된 정의를 거부한다.</summary>
        private void ValidateAdditionalSettings()
        {
            if (HasTrait(TRAIT_DEATH_EXPLOSION) && (!IsPositive(_explosionRadius) || !IsPositive(_explosionDamage)))
                throw new FormatException("사망 폭발 설정 오류입니다: " + _id);
            if (HasTrait(TRAIT_CIRCLE_SHIELD) && (!IsPositive(_shieldRadius) || _shieldRadius <= _radius))
                throw new FormatException("원형 보호막은 몸체보다 커야 합니다: " + _id);
            if (HasTrait(TRAIT_CARRIER) && (_carrierTargets == null || _carrierTargets.Length == 0 || !IsPositive(_carrierInterval)
                || _carrierCount < 1 || !IsPositive(_carrierDistance) || _carrierMaxAlive < 1))
                throw new FormatException("캐리어 생성 설정 오류입니다: " + _id);
            if (Attack == ATTACK_SNIPER && (_movement != MOVEMENT_SNIPER || !IsPositive(_aimSeconds) || !IsPositive(_lockSeconds)
                || !IsPositive(_attackInterval) || !IsPositive(_attackRange) || _attackInterval <= _aimSeconds + _lockSeconds))
                throw new FormatException("저격 설정 오류입니다: " + _id);
            if (Attack == ATTACK_INTERCEPTOR && (_movement != MOVEMENT_INTERCEPTOR || !IsPositive(_moveSeconds) || !IsPositive(_stopSeconds)
                || !IsPositive(_preferredDistance) || !IsPositive(_speed)))
                throw new FormatException("인터셉터 설정 오류입니다: " + _id);
            if ((Attack == ATTACK_SNIPER || Attack == ATTACK_INTERCEPTOR)
                && (!IsPositive(_projectileSpeed) || !IsPositive(_projectileRadius) || !IsPositive(_projectileLifetime)))
                throw new FormatException("추가 적 탄환 설정 오류입니다: " + _id);
            if (double.IsNaN(_turnSeconds) || double.IsInfinity(_turnSeconds) || _turnSeconds < 0)
                throw new FormatException("방향 전환 시간 설정 오류입니다: " + _id);
            if (_movement == MOVEMENT_TURN_CHASE && !IsPositive(_turnSeconds))
                throw new FormatException("방향 전환 추적은 방향 전환 시간이 양수여야 합니다: " + _id);
            if ((_movement == MOVEMENT_SNIPER && Attack != ATTACK_SNIPER) || (_movement == MOVEMENT_INTERCEPTOR && Attack != ATTACK_INTERCEPTOR))
                throw new FormatException("새 적의 이동·공격 조합 오류입니다: " + _id);
        }

        /// <summary>수치가 유한한 양수인지 반환한다.</summary>
        private static bool IsPositive(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
    }

    [Serializable]
    public sealed class EnemyCatalog
    {
        [Header("적 종류")]
        [SerializeField] private EnemyDefinition[] _enemies;
        public IReadOnlyList<EnemyDefinition> Enemies => _enemies;

        /// <summary>적 ID에 해당하는 설정을 반환하고 누락된 ID에는 데이터 오류를 발생시킨다.</summary>
        public EnemyDefinition Get(string id)
        { for (int i = 0; i < _enemies.Length; i++) if (_enemies[i].Id == id) return _enemies[i]; throw new FormatException("등록되지 않은 적: " + id); }

        /// <summary>JSON 적 설정을 읽고 배열 구성, 고유 ID, 체력, 크기, 보상 범위, 행동 어휘, 분열 대상과 순환 분열, 캐리어 생성 대상을 검증한다.</summary>
        public static EnemyCatalog FromJson(string json)
        {
            var catalog = JsonUtility.FromJson<EnemyCatalog>(json);
            if (catalog == null || catalog._enemies == null || catalog._enemies.Length == 0) throw new FormatException("적 설정이 비어 있습니다.");
            var ids = new HashSet<string>();
            foreach (EnemyDefinition enemy in catalog._enemies)
                if (!ids.Add(enemy.Id) || enemy.Hp <= 0 || enemy.Radius <= 0 || enemy.Reward < 0) throw new FormatException("적 설정 값이 유효하지 않습니다.");
            foreach (EnemyDefinition enemy in catalog._enemies) enemy.Validate();
            foreach (EnemyDefinition enemy in catalog._enemies)
                if (enemy.CanSplit && (enemy.SplitInto == enemy.Id || enemy.SplitCount < 0 || enemy.SplitDelay < 0 || !ids.Contains(enemy.SplitInto))) throw new FormatException("적 분열 설정이 유효하지 않습니다: " + enemy.Id);
            foreach (EnemyDefinition enemy in catalog._enemies)
                for (string current = enemy.CanSplit ? enemy.SplitInto : null; !string.IsNullOrEmpty(current); current = catalog.Get(current).CanSplit ? catalog.Get(current).SplitInto : null)
                    if (current == enemy.Id) throw new FormatException("순환 분열 설정입니다: " + enemy.Id);
            foreach (EnemyDefinition enemy in catalog._enemies)
                if (enemy.HasTrait(EnemyDefinition.TRAIT_CARRIER))
                    foreach (string target in enemy.CarrierTargets)
                        if (!ids.Contains(target) || catalog.Get(target).HasTrait(EnemyDefinition.TRAIT_CARRIER) || target.StartsWith("boss.", StringComparison.Ordinal))
                            throw new FormatException("캐리어의 생성 대상은 등록된 생성형·보스가 아닌 일반 적이어야 합니다: " + enemy.Id + ", " + target);
            return catalog;
        }
    }

    [Serializable]
    public sealed class SectorDefinition
    {
        [Header("시험 도크 맵")]
        [SerializeField] private int _tileSize;
        [SerializeField] private double _playerRadius;
        [SerializeField] private string[] _benchTiles;
        public int TileSize => _tileSize;
        public double PlayerRadius => _playerRadius;
        public string[] BenchTiles => _benchTiles;

        /// <summary>타일 크기·플레이어 반경과 시험 도크 맵 타일(벽 경계, 시작점)을 검증하여 반환한다.</summary>
        public static SectorDefinition FromJson(string json)
        {
            var sector = JsonUtility.FromJson<SectorDefinition>(json);
            if (sector == null || sector._benchTiles == null || sector._tileSize <= 0 || sector._playerRadius <= 0) throw new FormatException("섹터 구성 오류입니다.");
            new MissionMap(sector._benchTiles, sector._tileSize);
            return sector;
        }
    }

    [Serializable]
    public sealed class BossReinforcement
    {
        [Header("보스 증원")]
        [SerializeField] private string _enemyId;
        [SerializeField] private int _count;
        [SerializeField] private double _interval;
        [SerializeField] private string _formation;
        public string EnemyId => _enemyId;
        public int Count => _count;
        public double Interval => _interval;
        public string Formation => _formation;
    }

    [Serializable]
    public sealed class BossHazard
    {
        [Header("장판 세트")]
        [SerializeField] private double _interval;
        [SerializeField] private int _count;
        [SerializeField] private double _setInterval;
        [Header("장판 판정")]
        [SerializeField] private double _warning;
        [SerializeField] private double _lifetime;
        [SerializeField] private double _radius;
        [SerializeField] private double _damage;
        public double Interval => _interval;
        public int Count => _count;
        public double SetInterval => _setInterval;
        public double Warning => _warning;
        public double Lifetime => _lifetime;
        public double Radius => _radius;
        public double Damage => _damage;

        /// <summary>세트 개수와 간격, 장판 예고·지속 시간·크기·피해가 양수인지 검증한다.</summary>
        internal void Validate(string enemyId)
        {
            if (_count < 1 || _interval <= 0 || _setInterval <= 0 || _warning <= 0 || _lifetime <= 0 || _radius <= 0 || _damage <= 0)
                throw new FormatException("보스 장판 설정 오류입니다: " + enemyId);
        }
    }

    [Serializable]
    public sealed class BossDefinition
    {
        public const string GOVERNOR_PATTERN = "governor";
        public const string MINI_GOVERNOR_PATTERN = "mini_governor";
        public const string HYDRA_PATTERN = "hydra";
        public const string LEGOLAS_PATTERN = "legolas";

        [Header("보스 패턴")]
        [SerializeField] private string _enemyId;
        [SerializeField] private string _pattern;
        [SerializeField] private double _phaseTwoThreshold;
        [SerializeField] private double _phaseThreeThreshold;
        [SerializeField] private double _patchSeconds;
        [SerializeField] private double _radialInterval;
        [SerializeField] private int _radialCount;
        [SerializeField] private double _aimInterval;
        [Header("장판 세트")]
        [SerializeField] private BossHazard _hazardPhaseTwo;
        [SerializeField] private BossHazard _hazardPhaseThree;
        [SerializeField] private double _phaseThreeMultiplier;
        [SerializeField] private double _preferredDistance;
        [SerializeField] private BossReinforcement _reinforcement;
        [SerializeField] private string _dropTable;
        [Header("Hydra")]
        [SerializeField] private HydraDefinition _hydra;
        [Header("레골라스")]
        [SerializeField] private LegolasDefinition _legolas;
        public string EnemyId => _enemyId;
        public string Pattern => _pattern;
        public double PhaseTwoThreshold => _phaseTwoThreshold;
        public double PhaseThreeThreshold => _phaseThreeThreshold;
        public double PatchSeconds => _patchSeconds;
        public double RadialInterval => _radialInterval;
        public int RadialCount => _radialCount;
        public double AimInterval => _aimInterval;
        public double PhaseThreeMultiplier => _phaseThreeMultiplier;
        public double PreferredDistance => _preferredDistance;
        public BossReinforcement Reinforcement => _reinforcement;
        public string DropTable => _dropTable;
        public bool IsGovernorPattern => _pattern == GOVERNOR_PATTERN || IsMiniGovernorPattern;
        public bool IsMiniGovernorPattern => _pattern == MINI_GOVERNOR_PATTERN;
        public bool IsMiniBoss => IsMiniGovernorPattern || IsLegolasPattern;
        public bool IsHydraPattern => _pattern == HYDRA_PATTERN;
        public HydraDefinition Hydra => _hydra;
        public bool IsLegolasPattern => _pattern == LEGOLAS_PATTERN;
        public LegolasDefinition Legolas => _legolas;

        /// <summary>페이즈에 대응하는 장판 세트 설정을 반환하고 2페이즈가 아니면 3페이즈 설정을 반환한다.</summary>
        public BossHazard GetHazard(int phase)
        { return phase == 2 ? _hazardPhaseTwo : _hazardPhaseThree; }

        /// <summary>패턴 이름, 페이즈 문턱, 공격 주기, 장판 세트 설정과 증원의 적·진형 참조를 검증하고 모르는 값이나 증원에 쓸 수 없는 맵형 진형에는 데이터 오류를 발생시킨다.</summary>
        public void Validate(EnemyCatalog enemies, FormationCatalog formations)
        {
            if (IsMiniGovernorPattern)
            {
                if (!IsPositive(_phaseTwoThreshold) || _phaseTwoThreshold >= 1 || !IsPositive(_patchSeconds)
                    || _radialCount < 1 || !IsPositive(_radialInterval) || !IsPositive(_aimInterval)
                    || _aimInterval >= _radialInterval || !IsPositive(_preferredDistance)
                    || !IsPositive(enemies.Get(_enemyId).WarningSeconds))
                    throw new FormatException("미니 거버너의 페이즈·사격·예고 설정 오류입니다: " + _enemyId);
                return;
            }
            if (IsLegolasPattern)
            {
                if (_legolas == null || enemies.Get(_enemyId).Attack != EnemyDefinition.ATTACK_SNIPER)
                    throw new FormatException("레골라스의 저격 또는 패턴 설정이 없습니다: " + _enemyId);
                _legolas.Validate();
                return;
            }
            if (IsHydraPattern)
            {
                if (_hydra == null) throw new FormatException("Hydra 설정이 없습니다: " + _enemyId);
                _hydra.Validate();
                return;
            }
            if (_pattern != GOVERNOR_PATTERN) throw new FormatException("알 수 없는 보스 패턴입니다: " + _enemyId + ", " + _pattern);
            if (_phaseTwoThreshold <= _phaseThreeThreshold || _patchSeconds <= 0 || _radialCount < 1 || _radialInterval <= 0) throw new FormatException("보스 패턴 설정 오류입니다: " + _enemyId);
            if (_hazardPhaseTwo == null || _hazardPhaseThree == null) throw new FormatException("보스 장판 설정이 없습니다: " + _enemyId);
            _hazardPhaseTwo.Validate(_enemyId);
            _hazardPhaseThree.Validate(_enemyId);
            if (_reinforcement == null || _reinforcement.Count < 1 || _reinforcement.Interval <= 0) throw new FormatException("보스 증원 설정 오류입니다: " + _enemyId);
            enemies.Get(_reinforcement.EnemyId);
            // 증원은 보스 주변 원형 슬롯에 즉시 생성하므로 예고를 거치는 맵형 진형은 쓸 수 없다.
            if (SpawnFormations.Get(formations.Get(_reinforcement.Formation).Shape).IsArena)
                throw new FormatException("보스 증원에는 맵형 진형을 쓸 수 없습니다: " + _enemyId + ", " + _reinforcement.Formation);
        }

        /// <summary>보스 패턴 수치가 유한한 양수인지 반환한다.</summary>
        private static bool IsPositive(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
    }

    [Serializable]
    public sealed class BossCatalog
    {
        [Header("보스 목록")]
        [SerializeField] private BossDefinition[] _bosses;
        public IReadOnlyList<BossDefinition> Bosses => _bosses;

        /// <summary>보스 적 ID에 해당하는 설정을 반환하고 누락된 ID에는 데이터 오류를 발생시킨다.</summary>
        public BossDefinition Get(string enemyId)
        { for (int i = 0; i < _bosses.Length; i++) if (_bosses[i].EnemyId == enemyId) return _bosses[i]; throw new FormatException("등록되지 않은 보스: " + enemyId); }

        /// <summary>보스 적 ID의 존재 여부를 반환하고 존재하면 설정을 내보낸다.</summary>
        public bool TryGet(string enemyId, out BossDefinition boss)
        {
            boss = null;
            for (int i = 0; i < _bosses.Length; i++)
                if (_bosses[i].EnemyId == enemyId) { boss = _bosses[i]; return true; }
            return false;
        }

        /// <summary>JSON 보스 설정을 읽고 배열 구성, ID 중복, 적·진형 참조와 패턴 값을 검증하여 반환한다.</summary>
        public static BossCatalog FromJson(string json, EnemyCatalog enemies, FormationCatalog formations)
        {
            var catalog = JsonUtility.FromJson<BossCatalog>(json);
            if (catalog == null || catalog._bosses == null || catalog._bosses.Length == 0) throw new FormatException("보스 설정이 비어 있습니다.");
            var ids = new HashSet<string>();
            foreach (BossDefinition boss in catalog._bosses)
            {
                if (string.IsNullOrEmpty(boss.EnemyId) || !ids.Add(boss.EnemyId)) throw new FormatException("보스 ID가 비었거나 중복입니다: " + boss.EnemyId);
                enemies.Get(boss.EnemyId);
                boss.Validate(enemies, formations);
            }
            return catalog;
        }
    }

    public sealed class MissionMap
    {
        private const int MIN_SIZE = 3;

        private readonly string[] _tiles;
        private readonly int _tileSize;
        private readonly Dictionary<char, SimVector> _points = new Dictionary<char, SimVector>();
        public string[] Tiles => _tiles;
        public int TileSize => _tileSize;
        public int Width => _tiles[0].Length * _tileSize;
        public int Height => _tiles.Length * _tileSize;
        public SimVector PlayerStart => _points['P'];

        /// <summary>직사각형 ASCII 맵의 벽과 명명된 시작, 문, 터미널 및 스폰 좌표를 읽는다. 외곽은 모두 벽이어야 한다.</summary>
        public MissionMap(string[] tiles, int tileSize)
        {
            if (tiles == null || tiles.Length < MIN_SIZE || tiles[0] == null || tiles[0].Length < MIN_SIZE) throw new FormatException("맵은 3×3 이상이어야 합니다.");
            _tiles = (string[])tiles.Clone(); _tileSize = tileSize;
            int width = _tiles[0].Length;
            for (int y = 0; y < _tiles.Length; y++)
            {
                if (_tiles[y] == null || _tiles[y].Length != width) throw new FormatException("맵의 모든 줄은 너비가 같아야 합니다.");
                for (int x = 0; x < _tiles[y].Length; x++)
                {
                    char cell = _tiles[y][x];
                    if ((x == 0 || x == width - 1 || y == 0 || y == _tiles.Length - 1) && cell != '#') throw new FormatException("맵 경계는 벽이어야 합니다.");
                    if (cell != '#' && cell != '.') _points[cell] = new SimVector((x + 0.5) * tileSize, (y + 0.5) * tileSize);
                }
            }
            if (!_points.ContainsKey('P')) throw new FormatException("플레이어 시작점이 없습니다.");
        }

        /// <summary>외곽에만 벽이 있는 빈 맵을 지정 칸 수로 만들고 가운데 칸을 플레이어 시작점으로 둔다.</summary>
        public static MissionMap CreateOpen(int columns, int rows, int tileSize)
        {
            var tiles = new string[rows];
            var wall = new string('#', columns);
            var floor = "#" + new string('.', columns - 2) + "#";
            for (int y = 0; y < rows; y++) tiles[y] = y == 0 || y == rows - 1 ? wall : floor;
            char[] center = tiles[rows / 2].ToCharArray(); center[columns / 2] = 'P'; tiles[rows / 2] = new string(center);
            return new MissionMap(tiles, tileSize);
        }

        /// <summary>문자 스폰이나 터미널이 방 안에 존재하는지 반환한다.</summary>
        public bool HasPoint(char point) => _points.ContainsKey(point);

        /// <summary>ASCII 문자에 연결된 중심 좌표를 반환한다.</summary>
        public SimVector GetPoint(char point) => _points[point];

        /// <summary>월드 좌표가 벽 또는 방 외부에 속하는지 반환한다.</summary>
        public bool IsWall(SimVector position)
        { int x = (int)Math.Floor(position.X / _tileSize); int y = (int)Math.Floor(position.Y / _tileSize); return x < 0 || y < 0 || y >= _tiles.Length || x >= _tiles[0].Length || _tiles[y][x] == '#'; }

        /// <summary>원형 개체가 벽 타일과 겹치는지 정확한 원과 사각형 충돌로 확인한다.</summary>
        public bool CanOccupy(SimVector position, double radius)
        {
            int left = (int)Math.Floor((position.X - radius) / _tileSize);
            int right = (int)Math.Floor((position.X + radius) / _tileSize);
            int top = (int)Math.Floor((position.Y - radius) / _tileSize);
            int bottom = (int)Math.Floor((position.Y + radius) / _tileSize);
            for (int y = top; y <= bottom; y++)
                for (int x = left; x <= right; x++)
                {
                    if (y < 0 || x < 0 || y >= _tiles.Length || x >= _tiles[0].Length) return false;
                    if (_tiles[y][x] != '#') continue;
                    double nearestX = Math.Max(x * _tileSize, Math.Min(position.X, (x + 1) * _tileSize));
                    double nearestY = Math.Max(y * _tileSize, Math.Min(position.Y, (y + 1) * _tileSize));
                    double dx = position.X - nearestX; double dy = position.Y - nearestY;
                    if (dx * dx + dy * dy < radius * radius) return false;
                }
            return true;
        }

        /// <summary>원형 개체의 벽 접촉 위치·반경과 입사 방향으로 가장 정면에 닿은 벽의 단위 법선을 반환한다.</summary>
        internal SimVector GetWallContactNormal(SimVector position, double radius, SimVector direction)
        {
            const double CONTACT_TOLERANCE = 0.001;
            double searchRadius = radius + CONTACT_TOLERANCE;
            int left = (int)Math.Floor((position.X - searchRadius) / _tileSize);
            int right = (int)Math.Floor((position.X + searchRadius) / _tileSize);
            int top = (int)Math.Floor((position.Y - searchRadius) / _tileSize);
            int bottom = (int)Math.Floor((position.Y + searchRadius) / _tileSize);
            SimVector normal = direction * -1;
            double bestApproach = 0;
            for (int y = top; y <= bottom; y++)
                for (int x = left; x <= right; x++)
                {
                    if (y >= 0 && x >= 0 && y < _tiles.Length && x < _tiles[0].Length && _tiles[y][x] != '#') continue;
                    double nearestX = Math.Max(x * _tileSize, Math.Min(position.X, (x + 1) * _tileSize));
                    double nearestY = Math.Max(y * _tileSize, Math.Min(position.Y, (y + 1) * _tileSize));
                    SimVector offset = position - new SimVector(nearestX, nearestY);
                    if (offset.LengthSquared > searchRadius * searchRadius || offset.LengthSquared <= RuneSimulation.COLLISION_EPSILON) continue;
                    SimVector candidate = offset.Normalized();
                    double approach = -SimVector.Dot(direction, candidate);
                    if (approach <= bestApproach) continue;
                    bestApproach = approach;
                    normal = candidate;
                }
            return normal;
        }

        /// <summary>이동 경로를 작은 구간으로 확인하고 벽을 따라 미끄러진 최종 이동 가능 좌표를 반환한다.</summary>
        public SimVector Move(SimVector origin, SimVector displacement, double radius)
        {
            int pieces = Math.Max(1, (int)Math.Ceiling(displacement.Length / (_tileSize * 0.25)));
            SimVector result = origin; SimVector step = displacement / pieces;
            for (int i = 0; i < pieces; i++)
            {
                SimVector target = result + step;
                if (CanOccupy(target, radius)) { result = target; continue; }
                SimVector horizontal = new SimVector(target.X, result.Y);
                if (CanOccupy(horizontal, radius)) result = horizontal;
                SimVector vertical = new SimVector(result.X, target.Y);
                if (CanOccupy(vertical, radius)) result = vertical;
            }
            return result;
        }

        /// <summary>점멸 목적지가 벽이면 가장 가까운 이동 가능한 바닥 타일 중심을 반환한다.</summary>
        public SimVector NearestFree(SimVector target, double radius)
        {
            if (CanOccupy(target, radius)) return target;
            SimVector result = PlayerStart; double best = double.MaxValue;
            for (int y = 1; y < _tiles.Length - 1; y++)
                for (int x = 1; x < _tiles[y].Length - 1; x++)
                { if (_tiles[y][x] == '#') continue; var candidate = new SimVector((x + 0.5) * _tileSize, (y + 0.5) * _tileSize); double distance = (candidate - target).LengthSquared; if (distance < best && CanOccupy(candidate, radius)) { result = candidate; best = distance; } }
            return result;
        }
    }
}

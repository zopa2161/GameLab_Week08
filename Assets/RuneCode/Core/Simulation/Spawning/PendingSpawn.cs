namespace RuneCode
{
    internal readonly struct PendingSpawn
    {
        private readonly int _tick;
        private readonly string _enemyId;
        private readonly SimVector _position;
        private readonly EliteSpawnDefinition _elite;
        private readonly bool _isKillTarget;
        private readonly SimVector _warningCenter;
        private readonly double _warningRadius;
        public int Tick => _tick;
        public string EnemyId => _enemyId;
        public SimVector Position => _position;
        public EliteSpawnDefinition Elite => _elite;
        public bool IsKillTarget => _isKillTarget;
        public SimVector WarningCenter => _warningCenter;
        public double WarningRadius => _warningRadius;

        /// <summary>맵형 스폰의 생성 틱, 적 ID, 생성 위치, 엘리트 정의, 처치 집계 여부와 예고 표시 중심·반경을 보관한다.</summary>
        public PendingSpawn(int tick, string enemyId, SimVector position, EliteSpawnDefinition elite, bool isKillTarget, SimVector warningCenter, double warningRadius)
        {
            _tick = tick; _enemyId = enemyId; _position = position; _elite = elite; _isKillTarget = isKillTarget;
            _warningCenter = warningCenter; _warningRadius = warningRadius;
        }
    }
}

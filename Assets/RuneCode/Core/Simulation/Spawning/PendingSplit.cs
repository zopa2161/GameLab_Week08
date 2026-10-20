namespace RuneCode
{
    internal readonly struct PendingSplit
    {
        private readonly int _tick;
        private readonly string _enemyId;
        private readonly SimVector _position;
        private readonly EliteSpawnDefinition _elite;
        private readonly int _carrierOwnerId;
        public int Tick => _tick;
        public string EnemyId => _enemyId;
        public SimVector Position => _position;
        public EliteSpawnDefinition Elite => _elite;
        public int CarrierOwnerId => _carrierOwnerId;

        /// <summary>분열 자식의 생성 틱, 적 ID, 생성 위치와 부모에게서 물려받은 엘리트 정의를 보관한다.</summary>
        public PendingSplit(int tick, string enemyId, SimVector position, EliteSpawnDefinition elite, int carrierOwnerId = 0) { _tick = tick; _enemyId = enemyId; _position = position; _elite = elite; _carrierOwnerId = carrierOwnerId; }
    }
}

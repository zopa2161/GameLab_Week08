namespace RuneCode
{
    internal readonly struct PendingEnemyShot
    {
        private readonly int _tick;
        private readonly SimulationEnemy _owner;
        private readonly SimVector _direction;
        private readonly double _speedMultiplier;
        public int Tick => _tick;
        public SimulationEnemy Owner => _owner;
        public SimVector Direction => _direction;
        public double SpeedMultiplier => _speedMultiplier;

        /// <summary>연사 탄환의 발사 틱, 소유 적, 방향 및 속도 배율을 보관한다.</summary>
        public PendingEnemyShot(int tick, SimulationEnemy owner, SimVector direction, double speedMultiplier) { _tick = tick; _owner = owner; _direction = direction; _speedMultiplier = speedMultiplier; }
    }
}

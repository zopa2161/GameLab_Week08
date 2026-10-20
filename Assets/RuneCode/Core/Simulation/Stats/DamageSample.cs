namespace RuneCode
{
    internal readonly struct DamageSample
    {
        private readonly int _tick;
        private readonly double _amount;
        public int Tick => _tick;
        public double Amount => _amount;
        /// <summary>피해 틱과 실제 적용량을 5초 롤링 DPS 계산용으로 보관한다.</summary>
        public DamageSample(int tick, double amount) { _tick = tick; _amount = amount; }
    }
}

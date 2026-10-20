namespace RuneCode
{
    /// <summary>시드 기반 mulberry32 연산으로 결정적 난수를 만드는 시뮬레이션 전용 보조다.</summary>
    internal sealed class SimRandom
    {
        private readonly int _seed;
        private uint _state;

        /// <summary>시드를 저장하고 난수 상태를 시드 값으로 초기화한다.</summary>
        internal SimRandom(int seed) { _seed = seed; _state = (uint)seed; }

        /// <summary>현재 난수 상태를 반환한다. 상태 해시 기록에 쓴다.</summary>
        internal uint State => _state;

        /// <summary>시드 기반 mulberry32 연산으로 0 이상 1 미만의 결정적 난수를 반환한다.</summary>
        internal double Next()
        {
            unchecked { _state += 0x6D2B79F5; uint value = _state; value = (value ^ (value >> 15)) * (value | 1); value ^= value + ((value ^ (value >> 7)) * (value | 61)); return (value ^ (value >> 14)) / 4294967296.0; }
        }

        /// <summary>난수 상태를 시드 초기값으로 되돌린다. 시험 도크 초기화에 쓴다.</summary>
        internal void Reset() { _state = (uint)_seed; }
    }
}

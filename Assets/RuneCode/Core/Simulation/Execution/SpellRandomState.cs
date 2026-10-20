namespace RuneCode
{
    /// <summary>같은 루트 시전에서 첫 Noise 효과 시 정한 원소를 공유한다.</summary>
    internal sealed class SpellRandomState
    {
        private string _noiseElement;
        public string NoiseElement => _noiseElement;

        /// <summary>선택된 원소를 공유 상태에 저장한다.</summary>
        public SpellRandomState(string noiseElement = null) { _noiseElement = noiseElement; }

        /// <summary>아직 선택하지 않은 경우 첫 효과 시점의 원소를 기록한다.</summary>
        public void SetNoiseElement(string noiseElement)
        {
            if (_noiseElement == null) _noiseElement = noiseElement;
        }
    }
}

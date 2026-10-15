using System;
using System.Collections.Generic;
using System.Text;

namespace RuneCode
{
    public sealed class AdaptationNet
    {
        private static readonly string[] _tagNames = { "fire", "ice", "arc", "raw", "bolt", "burst", "orbit", "zone", "beam" };
        private readonly BalanceData _balance;
        private readonly double[] _values = new double[_tagNames.Length];
        private readonly double[] _lastUsed = new double[_tagNames.Length];
        private readonly double[] _damage = new double[_tagNames.Length];
        private readonly bool[] _locked = new bool[_tagNames.Length];
        private bool _isEnabled = true;
        public IReadOnlyList<string> Tags => _tagNames;
        public bool Enabled => _isEnabled;

        /// <summary>학습과 피해 감쇠의 적용 여부를 시험 도크 설정에 맞게 변경한다.</summary>
        public void SetEnabled(bool isEnabled) { _isEnabled = isEnabled; }

        /// <summary>학습 증가량, 상한 및 감쇠 설정을 사용하는 미션 공유 적응 네트워크를 생성한다.</summary>
        public AdaptationNet(BalanceData balance) { _balance = balance; }

        /// <summary>지정된 속성 또는 형태 태그의 현재 학습값을 반환한다.</summary>
        public double GetValue(string tag) { int index = Array.IndexOf(_tagNames, tag); return index < 0 ? 0 : _values[index]; }

        /// <summary>지정된 태그가 보스 패치로 고정되었는지 반환한다.</summary>
        public bool IsLocked(string tag) { int index = Array.IndexOf(_tagNames, tag); return index >= 0 && _locked[index]; }

        /// <summary>속성과 형태의 학습값에 따른 피해 배율을 반환하며 비활성 도크에서는 1을 반환한다.</summary>
        public double GetMultiplier(string element, string form) => _isEnabled ? (1 - GetValue(element)) * (1 - GetValue(form)) : 1;

        /// <summary>실제 피해의 속성과 형태를 기록하고 가중치, 노이즈 및 릴레이 효과를 반영하여 학습값을 증가시킨다.</summary>
        public void Learn(string element, string form, double time, double damage, bool isDot, bool noise, bool relayAlive)
        {
            int elementIndex = Array.IndexOf(_tagNames, element);
            int formIndex = Array.IndexOf(_tagNames, form);
            if (elementIndex >= 0) _damage[elementIndex] += damage;
            if (formIndex >= 0) _damage[formIndex] += damage;
            if (!_isEnabled) return;
            double weight = isDot ? _balance.Adaptation.DotWeight : 1;
            if (noise) weight *= _balance.Adaptation.NoiseMultiplier;
            if (relayAlive) weight *= _balance.Adaptation.RelayMultiplier;
            Increase(elementIndex, _balance.Adaptation.ElementLearning * weight, _balance.Adaptation.ElementCap, time);
            Increase(formIndex, _balance.Adaptation.FormLearning * weight, _balance.Adaptation.FormCap, time);
        }

        /// <summary>미사용 유예 시간이 지난 태그의 학습값을 고정 틱만큼 감쇠시키며 잠긴 태그는 유지한다.</summary>
        public void Advance(double previousTime, double time)
        {
            if (!_isEnabled) return;
            for (int i = 0; i < _values.Length; i++)
            {
                if (_locked[i]) continue;
                double decayStart = Math.Max(previousTime, _lastUsed[i] + _balance.Adaptation.DecayDelay);
                if (time > decayStart) _values[i] = Math.Max(0, _values[i] - (time - decayStart) * _balance.Adaptation.DecayPerSecond);
            }
        }

        /// <summary>보스전에서 누적 피해가 가장 큰 속성과 형태를 상한에 고정하고 잠긴 태그 이름을 반환한다.</summary>
        public string[] LockTopDamageTags()
        {
            int elementIndex = HighestDamage(0, 4);
            int formIndex = HighestDamage(4, _tagNames.Length);
            _values[elementIndex] = _balance.Adaptation.ElementCap;
            _values[formIndex] = _balance.Adaptation.FormCap;
            _locked[elementIndex] = true;
            _locked[formIndex] = true;
            return new[] { _tagNames[elementIndex], _tagNames[formIndex] };
        }

        /// <summary>보스 진입 시 기존 학습값은 유지하면서 패치 대상 선정용 피해 누적값을 초기화한다.</summary>
        public void ResetDamageReport() { Array.Clear(_damage, 0, _damage.Length); }

        /// <summary>학습값이 높은 순서의 태그 목록을 반환한다.</summary>
        public string[] GetReport(int count = 3)
        {
            var indices = new List<int>();
            for (int i = 0; i < _values.Length; i++) indices.Add(i);
            indices.Sort((a, b) => _values[b] != _values[a] ? _values[b].CompareTo(_values[a]) : a.CompareTo(b));
            var result = new string[Math.Min(count, indices.Count)];
            for (int i = 0; i < result.Length; i++) result[i] = _tagNames[indices[i]];
            return result;
        }

        /// <summary>학습값, 사용 시간, 패치 잠금 및 피해 기록을 초기화한다.</summary>
        public void Clear() { Array.Clear(_values, 0, _values.Length); Array.Clear(_lastUsed, 0, _lastUsed.Length); Array.Clear(_locked, 0, _locked.Length); ResetDamageReport(); }

        /// <summary>다른 시뮬레이션의 학습도와 고정 상태를 복사하여 터미널 시험 도크에 전달한다.</summary>
        public void CopyFrom(AdaptationNet other)
        { for (int i = 0; i < _values.Length; i++) { _values[i] = other._values[i]; _locked[i] = other._locked[i]; _lastUsed[i] = 0; } }

        /// <summary>학습값, 최근 사용 시점, 패치 후보 피해 및 잠금을 결정성 해시 버퍼에 기록한다.</summary>
        internal void WriteState(StringBuilder state)
        { state.Append(_isEnabled); for (int i = 0; i < _values.Length; i++) state.Append(FormattableString.Invariant($"|{_values[i]:R}|{_lastUsed[i]:R}|{_damage[i]:R}|{_locked[i]}")); }

        /// <summary>태그의 학습값과 마지막 사용 시간을 상한 및 잠금 상태에 맞게 갱신한다.</summary>
        private void Increase(int index, double amount, double cap, double time)
        { if (index < 0) return; _lastUsed[index] = time; if (!_locked[index]) _values[index] = Math.Min(cap, _values[index] + amount); }

        /// <summary>태그 범위 내에서 누적 피해가 가장 높은 태그 인덱스를 반환한다.</summary>
        private int HighestDamage(int first, int last)
        { int result = first; for (int i = first + 1; i < last; i++) if (_damage[i] > _damage[result]) result = i; return result; }
    }
}


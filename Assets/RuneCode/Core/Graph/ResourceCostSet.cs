using System;
using System.Collections.Generic;
using System.Globalization;

namespace RuneCode
{
    /// <summary>자원 ID와 그 자원의 비용 또는 보유량을 함께 나타낸다.</summary>
    public readonly struct ResourceAmount
    {
        private readonly string _resource;
        private readonly double _amount;

        public string Resource => _resource;
        public double Amount => _amount;

        /// <summary>자원 ID와 수치로 불변 자원량을 만든다.</summary>
        public ResourceAmount(string resource, double amount)
        {
            if (string.IsNullOrWhiteSpace(resource)) throw new ArgumentException("자원 ID가 비어 있습니다.", nameof(resource));
            _resource = resource;
            _amount = amount;
        }
    }

    /// <summary>자원별 비용을 합산하고 비교하는 불변 묶음이다.</summary>
    public sealed class ResourceCostSet
    {
        private static readonly ResourceCostSet _empty = new ResourceCostSet(Array.Empty<ResourceAmount>());
        private readonly IReadOnlyList<ResourceAmount> _amounts;

        public static ResourceCostSet Empty => _empty;
        public IReadOnlyList<ResourceAmount> Amounts => _amounts;

        /// <summary>중복 자원을 합쳐 비용 목록을 만든다.</summary>
        public ResourceCostSet(IEnumerable<ResourceAmount> amounts)
        {
            var values = new List<ResourceAmount>();
            var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
            if (amounts != null)
            {
                foreach (ResourceAmount amount in amounts)
                {
                    if (amount.Amount == 0) continue;
                    if (indexes.TryGetValue(amount.Resource, out int index))
                    {
                        ResourceAmount previous = values[index];
                        values[index] = new ResourceAmount(amount.Resource, previous.Amount + amount.Amount);
                    }
                    else
                    {
                        indexes.Add(amount.Resource, values.Count);
                        values.Add(amount);
                    }
                }
            }
            _amounts = values;
        }

        /// <summary>자원 ID에 해당하는 비용을 반환하고 목록에 없으면 0을 반환한다.</summary>
        public double GetAmount(string resource)
        {
            foreach (ResourceAmount amount in _amounts)
                if (amount.Resource == resource) return amount.Amount;
            return 0;
        }

        /// <summary>지정 자원의 비용만 amount로 교체하고 다른 자원 비용은 유지한 새 묶음을 반환한다.</summary>
        public ResourceCostSet WithAmount(string resource, double amount)
        {
            if (GetAmount(resource) == amount) return this;
            var values = new List<ResourceAmount>(_amounts.Count + 1);
            foreach (ResourceAmount current in _amounts)
                if (current.Resource != resource) values.Add(current);
            if (amount != 0d) values.Add(new ResourceAmount(resource, amount));
            return new ResourceCostSet(values);
        }

        /// <summary>비용 목록을 현재 언어의 자원 이름과 한 자리 소수 수치로 표시한다.</summary>
        public string Format(Func<string, string> getResourceName)
        {
            var values = new List<string>(_amounts.Count);
            foreach (ResourceAmount amount in _amounts)
            {
                string label = getResourceName == null ? amount.Resource : getResourceName(amount.Resource);
                values.Add(label + " " + amount.Amount.ToString("0.#", CultureInfo.CurrentCulture));
            }
            return string.Join(" · ", values);
        }

        /// <summary>다른 비용 묶음을 자원 ID별로 더해 새 불변 묶음을 반환한다.</summary>
        public ResourceCostSet Add(ResourceCostSet other)
        {
            if (other == null || other._amounts.Count == 0) return this;
            if (_amounts.Count == 0) return other;
            var values = new List<ResourceAmount>(_amounts.Count + other._amounts.Count);
            values.AddRange(_amounts);
            values.AddRange(other._amounts);
            return new ResourceCostSet(values);
        }

        /// <summary>모든 자원 비용에 같은 배율을 적용해 새 불변 묶음을 반환한다.</summary>
        public ResourceCostSet Multiply(double multiplier)
        {
            if (multiplier == 1 || _amounts.Count == 0) return this;
            var values = new List<ResourceAmount>(_amounts.Count);
            foreach (ResourceAmount amount in _amounts)
                // 노드 비용 배율은 float 데이터라 기존 float 곱셈과 같은 값이 되도록 float 정밀도로 맞춘다.
                values.Add(new ResourceAmount(amount.Resource, (float)(amount.Amount * multiplier)));
            return new ResourceCostSet(values);
        }

        /// <summary>자원별 최대값을 골라 두 실행 경로 중 높은 비용 묶음을 반환한다.</summary>
        public static ResourceCostSet Max(ResourceCostSet left, ResourceCostSet right)
        {
            left = left ?? Empty;
            right = right ?? Empty;
            var resources = new List<string>();
            foreach (ResourceAmount amount in left._amounts) resources.Add(amount.Resource);
            foreach (ResourceAmount amount in right._amounts)
                if (!resources.Contains(amount.Resource)) resources.Add(amount.Resource);
            var values = new List<ResourceAmount>(resources.Count);
            foreach (string resource in resources)
                values.Add(new ResourceAmount(resource, Math.Max(left.GetAmount(resource), right.GetAmount(resource))));
            return new ResourceCostSet(values);
        }

        /// <summary>비용이 발생하는 각 자원을 산정 불가 상태로 만들어 새 묶음을 반환한다.</summary>
        public ResourceCostSet MarkUnbounded()
        {
            if (_amounts.Count == 0) return this;
            var values = new List<ResourceAmount>(_amounts.Count);
            foreach (ResourceAmount amount in _amounts)
                values.Add(new ResourceAmount(amount.Resource, double.PositiveInfinity));
            return new ResourceCostSet(values);
        }
    }
}

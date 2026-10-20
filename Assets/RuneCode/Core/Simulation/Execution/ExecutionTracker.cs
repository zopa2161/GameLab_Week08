using System;

namespace RuneCode
{
    /// <summary>분기와 예약된 후속 노드의 대기 수를 세고 모두 끝나면 콜백을 한 번 호출한다.</summary>
    internal sealed class ExecutionTracker
    {
        private readonly RootSpellExecution _rootCast;
        private readonly Action _onCompleted;
        private int _pending;
        private int _dispatchDepth;
        private bool _isCompleted;
        public RootSpellExecution RootCast => _rootCast;
        public int Pending => _pending;

        /// <summary>루트 시전 범위와 완료 작업을 추적기에 연결한다.</summary>
        public ExecutionTracker(RootSpellExecution rootCast, Action onCompleted)
        {
            _rootCast = rootCast;
            _onCompleted = onCompleted;
        }

        /// <summary>동일 출력의 병렬 노드를 모두 등록할 때까지 완료 확인을 보류한다.</summary>
        public void BeginDispatch() { _dispatchDepth++; }

        /// <summary>노드 목록 등록을 끝내고 비어 있는 실행 분기를 완료한다.</summary>
        public void EndDispatch() { _dispatchDepth--; TryComplete(); }

        /// <summary>실행 중인 노드 또는 대기 예약 하나를 추적한다.</summary>
        public void Acquire() { _pending++; }

        /// <summary>노드 실행을 끝내고 마지막 대기 작업이면 완료 콜백을 호출한다.</summary>
        public void Release() { _pending--; TryComplete(); }

        /// <summary>실행 대기 수와 분기 등록이 모두 끝난 경우 완료 작업을 한 번 실행한다.</summary>
        private void TryComplete()
        {
            if (_isCompleted || _pending != 0 || _dispatchDepth != 0) return;
            _isCompleted = true;
            _onCompleted?.Invoke();
        }
    }
}

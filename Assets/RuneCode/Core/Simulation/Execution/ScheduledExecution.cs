namespace RuneCode
{
    internal readonly struct ScheduledExecution
    {
        private readonly int _tick;
        private readonly SpellAction _action;
        private readonly SpellContext _context;
        private readonly bool _isBranchStart;
        private readonly int _wave;
        private readonly ExecutionTracker _tracker;
        public int Tick => _tick;
        public SpellAction Action => _action;
        public SpellContext Context => _context;
        public ExecutionTracker Tracker => _tracker;
        public bool IsBranchStart => _isBranchStart;

        /// <summary>폭발에 장착한 다중이 다시 생성할 회차 번호다. 0이면 회차 예약이 아니다.</summary>
        public int Wave => _wave;

        /// <summary>목표 틱, 완료할 노드, 문맥과 실행 추적기를 저장한다.</summary>
        public ScheduledExecution(int tick, SpellAction action, SpellContext context, ExecutionTracker tracker)
        { _tick = tick; _action = action; _context = context; _isBranchStart = false; _wave = 0; _tracker = tracker; }

        /// <summary>Stores a root branch scheduled to start after the other root branches.</summary>
        public ScheduledExecution(int tick, SpellAction action, SpellContext context, ExecutionTracker tracker, bool isBranchStart)
        { _tick = tick; _action = action; _context = context; _isBranchStart = isBranchStart; _wave = 0; _tracker = tracker; }

        /// <summary>목표 틱, 다시 생성할 노드, 문맥, 실행 추적기와 회차 번호를 폭발 회차 예약 데이터에 보관한다.</summary>
        public ScheduledExecution(int tick, SpellAction action, SpellContext context, ExecutionTracker tracker, int wave)
        { _tick = tick; _action = action; _context = context; _isBranchStart = false; _wave = wave; _tracker = tracker; }
    }
}

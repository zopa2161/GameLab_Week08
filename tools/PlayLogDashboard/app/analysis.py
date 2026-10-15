"""JSONL 입력을 정규화하고 세션별 분석 표와 해석 보고서를 생성한다."""

import hashlib
import json
import math

import pandas as pd


CLICK_EVENTS = {"editor_click": "편집", "combat_click": "전투"}
TEXT_COLUMNS = ["participant_id", "test_session_id", "condition", "app_version",
                "event_name", "context", "combat_id", "spell_build_id"]
NUMBER_COLUMNS = ["elapsed_ms", "editing_ms", "event_seq", "graph_revision_id",
                  "data.nodes", "data.edges", "data.ram", "data.seconds",
                  "data.damage_dealt", "data.damage_taken", "data.kills",
                  "data.resource_failures", "data.clicks", "data.gap_ms"]


def load_events(files):
    """<summary>파일명·바이트 목록을 읽어 중복 제거한 이벤트 표와 품질 진단 표를 반환한다.</summary>"""
    records, issues = [], []
    seen_files, seen_events = set(), {}
    duplicate_files = duplicate_events = conflicts = 0
    for name, content in files:
        digest = hashlib.sha256(content).hexdigest()
        if digest in seen_files:
            duplicate_files += 1
            continue
        seen_files.add(digest)
        try:
            lines = content.decode("utf-8-sig").splitlines()
        except UnicodeDecodeError:
            issues.append({"파일": name, "줄": 0, "내용": "UTF-8 파일이 아닙니다."})
            continue
        for line_number, line in enumerate(lines, 1):
            if not line.strip():
                continue
            try:
                event = json.loads(line)
                if not isinstance(event, dict) or not isinstance(event.get("event_name"), str):
                    raise ValueError("event_name 문자열이 있는 JSON 객체가 필요합니다.")
                if not isinstance(event.get("data", {}), dict):
                    raise ValueError("data는 JSON 객체여야 합니다.")
                if not event["event_name"].strip():
                    raise ValueError("event_name이 비어 있습니다.")
                for field in TEXT_COLUMNS:
                    if event.get(field) is not None and not isinstance(event[field], (str, int)):
                        raise ValueError(f"{field}의 값 형식이 잘못되었습니다.")
            except (ValueError, TypeError) as error:
                issues.append({"파일": name, "줄": line_number, "내용": str(error)[:180]})
                continue
            raw = json.dumps(event, sort_keys=True, ensure_ascii=False)
            session = str(event.get("test_session_id") or f"파일-{digest[:12]}")
            participant = str(event.get("participant_id") or "미기록")
            seq = event.get("event_seq")
            valid_seq = isinstance(seq, (int, float)) and not isinstance(seq, bool) and math.isfinite(seq)
            key = (participant, session, seq) if valid_seq else (participant, session, raw)
            if key in seen_events:
                if seen_events[key] == raw:
                    duplicate_events += 1
                else:
                    conflicts += 1
                    issues.append({"파일": name, "줄": line_number,
                                   "내용": "동일 세션·event_seq에 다른 내용이 있어 첫 이벤트만 사용합니다."})
                continue
            seen_events[key] = raw
            event["test_session_id"] = session
            event["participant_id"] = participant
            event["_session"] = json.dumps([participant, session], ensure_ascii=False)
            event["_source"] = name
            event["_line"] = line_number
            records.append(event)
    diagnostics = {"중복 파일": duplicate_files, "중복 이벤트": duplicate_events,
                   "충돌 이벤트": conflicts, "입력 파일": len(files), "채택 이벤트": len(records)}
    if not records:
        return pd.DataFrame(), pd.DataFrame(issues), diagnostics
    frame = pd.json_normalize(records)
    for column in TEXT_COLUMNS:
        frame[column] = frame.get(column, pd.Series(index=frame.index, dtype="object")).fillna("미기록").astype(str)
    for column in NUMBER_COLUMNS:
        frame[column] = pd.to_numeric(frame.get(column, pd.Series(index=frame.index, dtype=float)), errors="coerce")
        frame[column] = frame[column].where(frame[column].ge(0) & frame[column].lt(float("inf")))
    for column in ["data.target", "data.button", "data.action", "data.result", "data.reason"]:
        frame[column] = frame.get(column, pd.Series(index=frame.index, dtype="object")).fillna("미기록").astype(str)
    for column in ["is_debug", "is_practice", "data.ok", "data.is_first_completion", "data.cleared"]:
        values = frame.get(column, pd.Series(index=frame.index, dtype="object"))
        frame[column] = values.map(lambda value: value if isinstance(value, bool) else None)
    frame["경과 시간(초)"] = frame["elapsed_ms"] / 1000
    frame["편집 시간(초)"] = frame["editing_ms"] / 1000
    return frame, pd.DataFrame(issues), diagnostics


def safe_rate(count, duration):
    """<summary>건수와 초 단위 노출 시간을 받아 분당 비율을 반환하며 0·누락 시간은 결측으로 둔다.</summary>"""
    return count * 60 / duration if pd.notna(duration) and duration > 0 else float("nan")


def summarize(frame):
    """<summary>선택된 이벤트를 세션별 클릭·편집·컴파일·완성·전투 지표로 집계한다.</summary>"""
    rows = []
    for session_key, group in frame.groupby("_session", sort=False):
        group = group.sort_values(["event_seq", "elapsed_ms"], kind="stable", na_position="last")
        first = group.iloc[0]
        elapsed = group["경과 시간(초)"].max()
        editing = group["편집 시간(초)"].max()
        clicks = group[group.event_name.isin(CLICK_EVENTS)]
        editor_count = int(clicks.event_name.eq("editor_click").sum())
        combat_count = int(clicks.event_name.eq("combat_click").sum())
        actions = group[group.event_name.eq("editor_action_result")]
        known_actions = actions[actions["data.result"].isin(["success", "rejected"])]
        rejected = int(known_actions["data.result"].eq("rejected").sum())
        compiles = group[group.event_name.eq("compile_result")]
        known_compiles = compiles[compiles["data.ok"].notna()]
        failures = int(known_compiles["data.ok"].eq(False).sum())
        ends = group[group.event_name.eq("combat_end")]
        completion = group[group.event_name.eq("spell_complete")]
        first_completion = completion.sort_values("elapsed_ms").head(1)
        combat_seconds = ends["data.seconds"].sum(min_count=1)
        notes = []
        if not group.event_name.eq("test_start").any():
            notes.append("시작 누락: 부분 로그 가능")
        if not group.event_name.eq("test_end").any():
            notes.append("종료 누락: 관측 구간만 집계")
        if group.elapsed_ms.isna().any() or group.editing_ms.isna().any():
            notes.append("일부 시간 누락")
        if group.condition.nunique() > 1 or group.app_version.nunique() > 1:
            notes.append("세션 내 조건/버전 혼합")
        if group.event_seq.notna().any():
            sequence = group.event_seq.dropna().sort_values()
            if sequence.diff().gt(1).any():
                notes.append("이벤트 번호 간격 존재")
        rows.append({
            "_session": session_key, "참가자": first.participant_id,
            "세션 ID": first.test_session_id, "조건": first.condition, "버전": first.app_version,
            "세션": f"{first.participant_id} · {first.condition} · {first.test_session_id[:8]}"
                    f"-{hashlib.sha256(session_key.encode()).hexdigest()[:4]}",
            "관측 경과(초)": elapsed, "관측 편집(초)": editing,
            "편집 클릭": editor_count, "전투 클릭": combat_count,
            "전체 클릭": editor_count + combat_count,
            "편집 클릭/분": safe_rate(editor_count, editing),
            "전투 종료": len(ends), "완료 전투 시간(초)": combat_seconds,
            "편집 행동": len(actions), "판정된 편집 행동": len(known_actions), "거절 행동": rejected,
            "거절률(%)": rejected / len(known_actions) * 100 if len(known_actions) else float("nan"),
            "컴파일": len(compiles), "판정된 컴파일": len(known_compiles), "컴파일 실패": failures,
            "컴파일 실패율(%)": failures / len(known_compiles) * 100 if len(known_compiles) else float("nan"),
            "주문 완성 이벤트": len(completion),
            "고유 완성 빌드": completion.get("data.build_key", pd.Series(dtype=str)).nunique(),
            "첫 완성 경과(초)": first_completion["경과 시간(초)"].min(),
            "첫 완성 편집(초)": first_completion["편집 시간(초)"].min(),
            "품질 메모": "; ".join(notes) or "시작·종료 기록 있음",
        })
    return pd.DataFrame(rows)


def click_timeline(frame, sessions, axis, bin_seconds, event_names):
    """<summary>세션별 관측 구간을 일정 초로 나누고 클릭이 없는 구간도 0건으로 반환한다.</summary>"""
    rows = []
    for _, session in sessions.iterrows():
        group = frame[frame._session.eq(session["_session"])]
        maximum = group[axis].max()
        if pd.isna(maximum):
            continue
        selected = group[group.event_name.isin(event_names) & group[axis].notna()]
        buckets = (selected[axis] // bin_seconds).astype(int).value_counts()
        for bucket in range(int(maximum // bin_seconds) + 1):
            rows.append({"시간(초)": bucket * bin_seconds, "클릭 수": int(buckets.get(bucket, 0)),
                         "세션": session["세션"], "조건": session["조건"]})
    return pd.DataFrame(rows)


def combat_table(frame, sessions):
    """<summary>전투 종료와 같은 세션·전투 ID의 클릭을 연결하고 전투별 성과 표를 반환한다.</summary>"""
    ends = frame[frame.event_name.eq("combat_end")].copy()
    if ends.empty:
        return ends
    counts = frame[frame.event_name.eq("combat_click")].groupby(["_session", "combat_id"]).size()
    ends["기록된 전투 클릭"] = [counts.get((row._session, row.combat_id), 0)
                                      if row.combat_id != "미기록" else float("nan")
                                      for _, row in ends.iterrows()]
    ends = ends.drop(columns=["세션", "조건"], errors="ignore").merge(
        sessions[["_session", "세션", "조건"]], on="_session", how="left")
    ends["전투"] = ends["세션"] + " · " + ends.combat_id
    for source, target in {"data.seconds": "전투 시간(초)", "data.damage_dealt": "가한 피해",
                           "data.damage_taken": "받은 피해", "data.kills": "처치",
                           "data.resource_failures": "자원 부족", "data.clicks": "종료 요약 클릭"}.items():
        ends[target] = ends[source]
    ends["초당 피해"] = ends["가한 피해"] / ends["전투 시간(초)"].where(ends["전투 시간(초)"].gt(0))
    ends["전투 클릭/분"] = ends["기록된 전투 클릭"] * 60 / ends["전투 시간(초)"].where(ends["전투 시간(초)"].gt(0))
    ends["클릭 차이(기록-요약)"] = ends["기록된 전투 클릭"] - ends["종료 요약 클릭"]
    return ends


def build_report(sessions, combats):
    """<summary>현재 선택 세션의 관측 사실과 분석 시 주의점을 한국어 Markdown 보고서로 반환한다.</summary>"""
    lines = ["# 게임 로그 분석 보고서", "", "## 현재 선택 데이터", "",
             f"- 세션 {len(sessions)}개, 참가자 {sessions['참가자'].nunique()}명",
             f"- 편집 클릭 {sessions['편집 클릭'].sum():,}회 / 전투 클릭 {sessions['전투 클릭'].sum():,}회",
             f"- 편집 행동 {sessions['편집 행동'].sum():,}건 중 거절 {sessions['거절 행동'].sum():,}건",
             f"- 컴파일 {sessions['컴파일'].sum():,}건 중 실패 {sessions['컴파일 실패'].sum():,}건", ""]
    for condition, group in sessions.groupby("조건"):
        valid = group["편집 클릭/분"].dropna()
        lines.append(f"- {condition}: {len(group)}세션 / {group['참가자'].nunique()}명"
                     + (f" / 세션별 편집 클릭/분 중앙값 {valid.median():.1f}" if len(valid) else " / 편집 시간 미기록"))
    completed = sessions["첫 완성 편집(초)"].dropna()
    lines.extend(["", "## 관측과 해석", "",
                  f"- 주문 완성 이벤트가 관측된 세션: {len(completed)}/{len(sessions)}개. "
                  + (f"해당 세션의 첫 완성 편집 시간 중앙값: {completed.median():.1f}초." if len(completed) else "완성 시간은 계산할 수 없습니다."),
                  "- 완성 기록이 없는 세션은 0초로 처리하지 않습니다. 관측 종료까지 미관측 상태입니다.",
                  "- 클릭 수가 적다는 사실만으로 사용성이 좋다고 결론 내릴 수 없습니다. 완성 시간·성과·거절률을 함께 보세요.",
                  "- 컴파일은 자동 실행일 수 있으므로 실패율을 사용자 실수율로 해석하지 마세요.",
                  "- 거절률은 result가 success/rejected인 행동만 분모로 사용합니다. 기타 결과는 원본에서 확인하세요.",
                  "- 조건 비교는 세션별 지표를 먼저 계산합니다. 반복 참여자는 독립된 참가자 여러 명이 아닙니다.",
                  "- 버전·스테이지·시드·연습 여부·플레이 시간 차이를 확인한 뒤 조건을 비교하세요.",
                  "- 누락된 시간·분모 0은 결측값입니다. 부분 로그의 누적 시간은 실제 관측 노출 시간과 다를 수 있습니다.",
                  "- 시작·종료 이벤트가 있어도 테스트 계획 전체를 완료했다는 의미는 아닙니다. 종료 사유를 확인하세요.",
                  "- 전투 클릭 합계는 combat_click만 셉니다. combat_end의 clicks는 검산용이며 더하지 않습니다."])
    if not combats.empty:
        mismatches = combats["클릭 차이(기록-요약)"].dropna().ne(0).sum()
        lines.append(f"- 종료된 전투 {len(combats)}건 중 클릭 기록과 종료 요약이 다른 전투: {mismatches}건.")
    lines.extend(["", "## 세션별 기록", ""])
    for _, row in sessions.iterrows():
        lines.append(f"- {row['세션']}: 편집 {row['편집 클릭']}회, 전투 {row['전투 클릭']}회. {row['품질 메모']}")
    return "\n".join(lines)


def csv_bytes(frame):
    """<summary>표를 Excel 호환 UTF-8 BOM CSV로 변환하고 문자열 수식 실행을 방지한다.</summary>"""
    output = frame.copy()
    for column in output.select_dtypes(include=["object", "string"]).columns:
        output[column] = output[column].map(
            lambda value: "'" + value if isinstance(value, str) and value.lstrip().startswith(("=", "+", "-", "@")) else value)
    return output.to_csv(index=False).encode("utf-8-sig")

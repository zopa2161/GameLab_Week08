"""여러 게임 JSONL 로그를 탐색하는 로컬 Streamlit 대시보드."""

from pathlib import Path

import pandas as pd
import plotly.express as px
import streamlit as st

from analysis import CLICK_EVENTS, build_report, click_timeline, combat_table, csv_bytes, load_events, summarize


ROOT = Path(__file__).resolve().parent
COLORS = ["#4f8cff", "#33c5a0", "#ffb454", "#c792ea", "#f47c91", "#74c9e8"]
st.set_page_config(page_title="플레이 로그 분석", page_icon="📊", layout="wide")


@st.cache_data(show_spinner=False, max_entries=3)
def parse_files(files):
    """<summary>파일명·내용 목록의 파싱 결과를 현재 서버 메모리에 캐시해 반환한다.</summary>"""
    return load_events(files)


def show_chart(figure, key):
    """<summary>그래프를 표시하고 설정에 따라 독립 실행 HTML 다운로드를 제공한다.</summary>"""
    figure.update_layout(font_family="Malgun Gothic, Arial, sans-serif", legend_title_text="",
                         margin=dict(l=16, r=16, t=32, b=16), colorway=COLORS)
    st.plotly_chart(figure, width="stretch", key=key,
                    config={"displaylogo": False, "toImageButtonOptions": {"format": "png", "scale": 2}})
    if st.session_state.get("html_export", False):
        st.download_button("이 그래프 HTML 저장", figure.to_html(include_plotlyjs=True),
                           file_name=f"{key}.html", mime="text/html", key=f"save-{key}")


def show_table(frame, name):
    """<summary>표를 표시하고 현재 표 전체의 CSV 다운로드를 제공한다.</summary>"""
    visible = frame.drop(columns=["_session"], errors="ignore")
    st.dataframe(visible, width="stretch", hide_index=True)
    st.download_button("이 표 CSV 저장", csv_bytes(visible), f"{name}.csv", "text/csv", key=f"csv-{name}")


st.title("플레이 로그 분석")
st.caption("JSONL 파일을 모아 클릭·편집 과정·전투 성과를 비교합니다. 파일은 이 PC에서 처리됩니다.")

with st.sidebar:
    st.header("로그 가져오기")
    uploads = st.file_uploader("events.jsonl 여러 개 선택", type=["jsonl", "ndjson"], accept_multiple_files=True)
    example = False
    st.caption("파일 이름이 모두 events.jsonl이어도 세션 ID로 구분합니다.")
    with st.expander("폴더에서 한꺼번에 읽기"):
        folder = st.text_input("이 PC의 로그 폴더 경로", placeholder=r"C:\PlayLogs")
        use_folder = st.checkbox("이 폴더의 하위 JSONL도 포함")
    st.checkbox("그래프 HTML 저장 버튼 표시", key="html_export")

files = [(item.name, item.getvalue()) for item in uploads]
if use_folder:
    path = Path(folder.strip().strip('"'))
    if not folder.strip() or not path.is_dir():
        st.error("로그가 들어 있는 폴더의 전체 경로를 입력하세요.")
        st.stop()
    try:
        paths = sorted({*path.rglob("*.jsonl"), *path.rglob("*.ndjson")})
        files.extend((str(item.relative_to(path)), item.read_bytes()) for item in paths)
    except OSError as error:
        st.error(f"폴더를 읽지 못했습니다: {error}")
        st.stop()
elif not files and example:
    sample = ROOT / "example" / "events.jsonl"
    if sample.exists():
        files = [("예제/events.jsonl", sample.read_bytes())]
        st.info("예제 데이터 표시 중입니다. 왼쪽에 파일을 넣으면 실제 분석으로 전환됩니다.")

if not files:
    st.info("왼쪽에서 JSONL 파일 여러 개를 선택하거나 로그 폴더를 지정하세요.")
    st.markdown("**제공하는 분석**\n\n- 세션별 클릭 수와 시간대별 추이\n- 클릭 대상과 마우스 버튼 분포\n- 편집 행동의 거절률과 컴파일 결과\n- 구성 크기 변화, 첫 주문 완성 시간, 전투 성과\n- 분석 해석 안내와 보고서 다운로드")
    st.stop()

with st.spinner("로그를 읽고 있습니다."):
    events, issues, diagnostics = parse_files(files)
if events.empty:
    st.error("분석할 수 있는 이벤트가 없습니다.")
    if not issues.empty:
        show_table(issues, "input_errors")
    st.stop()

with st.sidebar:
    st.header("분석 범위")
    participants = sorted(events.participant_id.unique())
    selected_participants = st.multiselect("참가자", participants, default=participants)
    conditions = sorted(events.condition.unique())
    selected_conditions = st.multiselect("조건", conditions, default=conditions)
    versions = sorted(events.app_version.unique())
    selected_versions = st.multiselect("앱 버전", versions, default=versions)
    exclude_debug = st.checkbox("디버그 이벤트 제외", value=True)
    exclude_practice = st.checkbox("연습 이벤트 제외", value=True)

filtered = events[events.participant_id.isin(selected_participants)
                  & events.condition.isin(selected_conditions)
                  & events.app_version.isin(selected_versions)].copy()
if exclude_debug:
    filtered = filtered[~filtered.is_debug.eq(True)]
if exclude_practice:
    filtered = filtered[~filtered.is_practice.eq(True)]
if filtered.empty:
    st.warning("선택 조건에 해당하는 이벤트가 없습니다. 왼쪽 필터를 조정하세요.")
    st.stop()
sessions = summarize(filtered)
with st.sidebar:
    lookup = dict(zip(sessions._session, sessions["세션"]))
    selected_sessions = st.multiselect("세션", list(lookup), default=list(lookup), format_func=lookup.get)
filtered = filtered[filtered._session.isin(selected_sessions)]
sessions = sessions[sessions._session.isin(selected_sessions)]
if sessions.empty:
    st.warning("분석할 세션을 선택하세요.")
    st.stop()
filtered = filtered.merge(sessions[["_session", "세션", "조건"]], on="_session", how="left")
combats = combat_table(filtered, sessions)

columns = st.columns(4)
columns[0].metric("참가자 / 세션", f"{sessions['참가자'].nunique()}명 / {len(sessions)}개")
columns[1].metric("편집 클릭", f"{sessions['편집 클릭'].sum():,}회")
columns[2].metric("전투 클릭", f"{sessions['전투 클릭'].sum():,}회")
columns[3].metric("편집 클릭/분 중앙값", f"{sessions['편집 클릭/분'].median():.1f}"
                  if sessions["편집 클릭/분"].notna().any() else "계산 불가")
st.caption("클릭은 editor_click·combat_click 이벤트만 집계합니다. 키보드 조작과 combat_end 요약 클릭은 합산하지 않습니다.")
if diagnostics["중복 파일"] or diagnostics["중복 이벤트"] or diagnostics["충돌 이벤트"] or not issues.empty:
    st.warning(f"입력 점검: 중복 파일 {diagnostics['중복 파일']}개, 중복 이벤트 {diagnostics['중복 이벤트']}건 제외, "
               f"충돌 {diagnostics['충돌 이벤트']}건, 오류·주의 {len(issues)}건. 아래 원본·품질 탭에서 확인하세요.")
if not sessions["품질 메모"].eq("시작·종료 기록 있음").all():
    st.warning("일부 세션에 시작·종료·시간 누락 또는 이벤트 간격이 있습니다. 비율 해석 전 원본·품질 탭을 확인하세요.")

click_tab, process_tab, combat_tab, report_tab, raw_tab = st.tabs(
    ["클릭 분석", "편집 과정", "전투 성과", "유의미하게 보는 법", "원본·품질"])

with click_tab:
    st.subheader("세션별 클릭 비교")
    metric = st.radio("비교할 지표", ["클릭 수", "편집 클릭/분"], horizontal=True)
    if metric == "클릭 수":
        counts = sessions.melt(id_vars=["세션", "조건"], value_vars=["편집 클릭", "전투 클릭"],
                               var_name="구분", value_name="클릭 수")
        show_chart(px.bar(counts, x="세션", y="클릭 수", color="구분", barmode="group",
                          hover_data=["조건"]), "session_clicks")
    else:
        show_chart(px.bar(sessions, x="세션", y="편집 클릭/분", color="조건"), "editor_click_rate")
        st.caption("분모는 세션의 최대 editing_ms입니다. 전체 로그가 있고 편집 타이머가 정상 기록된 세션끼리 비교하세요.")

    st.subheader("시간대별 클릭")
    a, b, c = st.columns(3)
    axis = a.selectbox("시간축", ["경과 시간(초)", "편집 시간(초)"])
    bin_seconds = b.selectbox("구간 크기(초)", [5, 10, 15, 30, 60], index=1)
    available = ["편집"] if axis == "편집 시간(초)" else ["편집", "전투"]
    selected_kinds = c.multiselect("클릭 종류", available, default=available)
    timeline = click_timeline(filtered, sessions, axis, bin_seconds,
                              [key for key, value in CLICK_EVENTS.items() if value in selected_kinds])
    if not timeline.empty:
        show_chart(px.line(timeline, x="시간(초)", y="클릭 수", color="세션", markers=True,
                           hover_data=["조건"], line_shape="hv"), "click_timeline")
        st.caption(f"각 점은 표시된 시각부터 {bin_seconds}초 구간의 클릭 수입니다. 마지막 구간은 짧을 수 있습니다. "
                   "0은 해당 구간에서 클릭 이벤트가 관측되지 않았다는 뜻입니다.")
        with st.expander("시간 구간별 집계 표"):
            show_table(timeline, "click_timeline")
    st.subheader("어디를 클릭했나")
    clicks = filtered[filtered.event_name.isin(CLICK_EVENTS)].copy()
    if clicks.empty:
        st.info("선택 범위에 클릭 이벤트가 없습니다.")
    else:
        clicks["구분"] = clicks.event_name.map(CLICK_EVENTS)
        counts = clicks.groupby(["data.target", "구분"]).size().reset_index(name="클릭 수")
        top = counts.groupby("data.target")["클릭 수"].sum().nlargest(15).index
        top_counts = counts[counts["data.target"].isin(top)].copy()
        labels = {target: f"{index + 1}. {'/'.join(target.split('/')[-2:])}" for index, target in enumerate(top)}
        top_counts["클릭 대상"] = top_counts["data.target"].map(labels)
        show_chart(px.bar(top_counts, x="클릭 수", y="클릭 대상", color="구분",
                          hover_data=["data.target"], category_orders={"클릭 대상": list(labels.values())},
                          height=550), "click_targets")
        buttons = clicks.groupby(["data.button", "구분"]).size().reset_index(name="클릭 수")
        show_chart(px.bar(buttons, x="data.button", y="클릭 수", color="구분", barmode="group",
                          labels={"data.button": "마우스 버튼"}), "click_buttons")
        st.caption("대상별 그래프는 총 클릭 수 상위 15개입니다. 표시 이름은 경로 끝부분이며, 마우스를 올리면 전체 경로를 확인합니다.")
        with st.expander("선택된 클릭 이벤트 전체"):
            show_table(clicks, "click_events")
    with st.expander("세션별 요약 표", expanded=True):
        show_table(sessions, "session_summary")

with process_tab:
    st.subheader("행동별 성공·거절")
    actions = filtered[filtered.event_name.eq("editor_action_result")]
    if actions.empty:
        st.info("편집 행동 기록이 없습니다.")
    else:
        action_counts = actions.groupby(["data.action", "data.result"]).size().reset_index(name="건수")
        show_chart(px.bar(action_counts, x="data.action", y="건수", color="data.result",
                          labels={"data.action": "행동", "data.result": "결과"}), "action_results")
        rejected = actions[actions["data.result"].eq("rejected")]
        if not rejected.empty:
            reasons = rejected.groupby(["data.action", "data.reason"]).size().reset_index(name="거절 건수")
            show_table(reasons, "rejection_reasons")
        st.caption("거절은 시스템이 기록한 rejected 판정입니다. 사용자의 혼란이나 오조작을 직접 증명하지는 않습니다.")
    st.subheader("세션별 거절률·컴파일 실패율")
    rates = sessions.melt(id_vars=["세션", "조건"], value_vars=["거절률(%)", "컴파일 실패율(%)"],
                          var_name="지표", value_name="비율(%)")
    show_chart(px.bar(rates, x="세션", y="비율(%)", color="지표", barmode="group", range_y=[0, 100]), "failure_rates")
    st.caption("거절률 분모: success/rejected 행동. 컴파일 실패율 분모: ok가 참/거짓인 compile_result. 분모가 없으면 빈 값입니다.")
    st.subheader("구성 크기 변화")
    compiles = filtered[filtered.event_name.eq("compile_result")]
    value = st.selectbox("구성 지표", ["data.nodes", "data.edges", "data.ram"],
                         format_func=lambda item: {"data.nodes": "노드 수", "data.edges": "연결 수", "data.ram": "RAM"}[item])
    if compiles.empty:
        st.info("컴파일 기록이 없습니다.")
    else:
        show_chart(px.line(compiles.sort_values(["_session", "elapsed_ms", "event_seq"]),
                           x="편집 시간(초)", y=value, color="세션", markers=True,
                           hover_data=["data.ok", "graph_revision_id"]), "graph_evolution")
    st.subheader("첫 주문 완성까지 걸린 편집 시간")
    completed = sessions[sessions["첫 완성 편집(초)"].notna()]
    if not completed.empty:
        show_chart(px.bar(completed, x="세션", y="첫 완성 편집(초)", color="조건",
                          hover_data=["첫 완성 경과(초)"]), "first_completion")
    st.caption(f"spell_complete의 첫 기록을 사용합니다. {len(sessions) - len(completed)}세션은 시간 기록 또는 완성 기록이 없어 "
               "그래프에서 제외했습니다. 미관측 세션을 0초로 처리하지 않습니다.")

with combat_tab:
    st.subheader("전투별 성과")
    if combats.empty:
        st.info("combat_end 기록이 없습니다. 진행 중이거나 중단된 전투는 완료 성과에 포함하지 않습니다.")
    else:
        combat_metric = st.selectbox("전투 지표", ["초당 피해", "전투 시간(초)", "가한 피해", "받은 피해",
                                                "처치", "자원 부족", "전투 클릭/분"])
        show_chart(px.bar(combats, x="전투", y=combat_metric, color="조건",
                          hover_data=["data.result", "spell_build_id"]), "combat_results")
        st.caption("초당 피해 = 가한 피해 ÷ 종료 요약의 전투 시간. 스테이지·시드·빌드가 다른 전투는 단순 우열 비교에 주의하세요.")
        show_table(combats[["전투", "combat_id", "spell_build_id", "data.result", "전투 시간(초)",
                            "가한 피해", "받은 피해", "초당 피해", "처치", "자원 부족", "기록된 전투 클릭",
                            "종료 요약 클릭", "클릭 차이(기록-요약)"]], "combat_summary")
        if combats["클릭 차이(기록-요약)"].dropna().ne(0).any():
            st.warning("일부 전투에서 개별 클릭과 종료 요약 클릭이 다릅니다. 입력 이벤트 필터나 로그 누락 여부를 확인하세요.")

with report_tab:
    st.subheader("이 순서로 보면 유의미합니다")
    st.markdown("""
1. **같은 과제를 끝냈는지 먼저 확인**하세요. 첫 완성 시간과 완성 기록이 없는 세션을 함께 봅니다.
2. **클릭 수와 시간당 클릭 수를 나란히** 보세요. 짧게 플레이해서 적게 클릭한 것인지 구분할 수 있습니다.
3. **거절이 몰리는 행동과 이유**를 보세요. 특정 연결·삭제·설정 조작의 개선 후보를 찾는 데 유용합니다.
4. **노드·연결 수 변화와 컴파일 실패**를 함께 보세요. 구성이 복잡해지는 구간에서 문제가 늘어나는지 탐색합니다.
5. **편집 결과와 전투 성과를 함께** 보세요. 빠른 편집이 실제 피해량·완성으로 이어지는지 확인합니다.

클릭은 많아도 탐색이 활발했을 수 있고, 적어도 과제를 포기했을 수 있습니다. 이 화면은 관측을 정리하며 인과관계나 통계적 유의성을 판정하지 않습니다.
""")
    st.subheader("조건별 분포")
    comparison = st.selectbox("조건 비교 지표", ["편집 클릭/분", "전체 클릭", "거절률(%)",
                                               "컴파일 실패율(%)", "첫 완성 편집(초)"])
    show_chart(px.box(sessions, x="조건", y=comparison, color="조건", points="all",
                      hover_data=["참가자", "세션 ID", "버전"]), "condition_comparison")
    st.caption("점 하나는 세션 하나입니다. 조건당 참가자가 적거나 동일 참가자가 반복 참여했다면 탐색용으로 해석하세요.")
    report = build_report(sessions, combats)
    st.markdown(report)
    st.download_button("현재 분석 보고서 저장", report.encode("utf-8-sig"), "analysis_report.md", "text/markdown")

with raw_tab:
    st.subheader("입력 품질")
    st.write(diagnostics)
    st.caption("중복은 참가자·세션·event_seq 기준입니다. 내용 충돌 시 첫 이벤트를 사용합니다. "
               "세션 ID가 없으면 파일별 임시 세션이므로 서로 다른 파일 간 연결은 하지 않습니다.")
    if not issues.empty:
        show_table(issues, "input_issues")
    quality = sessions[["세션", "관측 경과(초)", "관측 편집(초)", "품질 메모"]]
    show_table(quality, "session_quality")
    st.subheader("원하는 이벤트만 추출")
    options = sorted(filtered.event_name.unique())
    selected_events = st.multiselect("이벤트 종류", options, default=[name for name in CLICK_EVENTS if name in options])
    extracted = filtered[filtered.event_name.isin(selected_events)]
    if not extracted.empty:
        event_counts = extracted.groupby(["세션", "event_name"]).size().reset_index(name="건수")
        show_chart(px.bar(event_counts, x="세션", y="건수", color="event_name", barmode="group"), "selected_events")
        st.caption(f"{len(extracted):,}건 중 화면에는 앞 2,000건을 표시합니다. CSV는 선택 이벤트 전체를 포함합니다.")
        st.dataframe(extracted.drop(columns=["_session"], errors="ignore").head(2000), hide_index=True)
        st.download_button("선택 이벤트 전체 CSV", csv_bytes(extracted.drop(columns=["_session"], errors="ignore")),
                           "selected_events.csv", "text/csv")
    else:
        st.info("추출할 이벤트를 선택하세요.")

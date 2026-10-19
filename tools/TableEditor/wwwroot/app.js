"use strict";
// 룬 코드 테이블 편집기다. 스키마(schemas.js)와 실제 파일 값으로 화면을 구성하고,
// 변경사항을 메모리의 파일 객체에 반영한 뒤 저장 시 서버로 JSON 문자열을 보낸다.

const $ = sel => document.querySelector(sel);
const state = {
  catalog: [], data: {}, saved: {}, dirty: {}, current: null,
  selection: {}, errors: {}, refsets: {}, stringsFilter: "",
};

const REF_DEFS = {
  enemy: { label: "적", get: () => state.refsets.enemy },
  formation: { label: "진형", get: () => state.refsets.formation },
  drop: { label: "드롭 테이블", get: () => state.refsets.drop },
  rune: { label: "룬", get: () => state.refsets.rune },
  modifier: { label: "Modifier 룬", get: () => state.refsets.modifier },
  modifierOrElite: { label: "Modifier", get: () => [...state.refsets.modifier, "eliteModifier"] },
  strings: { label: "현지화 키", get: () => state.refsets.strings },
  grade: { label: "등급", get: () => state.refsets.grade },
  spellNode: { label: "노드", get: () => state.refsets.spellNode },
};

function baseId(table) { return table.id.startsWith("spell:") ? "spell" : table.id; }
function tableById(id) { return state.catalog.find(t => t.id === id); }
function labelOf(key) { return LABELS[key] || key; }
// 스키마는 groups 배열 또는 단일 fields 배열 두 형태를 지원한다.
function groupsOf(schema) { return schema.groups || [{ title: "", fields: schema.fields || [] }]; }
function firstFields(schema) { return schema.groups ? schema.groups[0].fields : (schema.fields || []); }
function getRoot(table) {
  const file = state.data[table.id];
  if (!table.rootPath) return file;
  return file[table.rootPath];
}

function el(tag, attrs, ...children) {
  const node = document.createElement(tag);
  if (attrs) for (const [k, v] of Object.entries(attrs)) {
    if (k === "class") node.className = v;
    else if (k === "text") node.textContent = v;
    else if (k.startsWith("on")) node.addEventListener(k.slice(2), v);
    else node.setAttribute(k, v);
  }
  for (const child of children) if (child != null) node.appendChild(child);
  return node;
}

async function init() {
  state.catalog = await (await fetch("/api/catalog")).json();
  await Promise.all(state.catalog.map(async table => {
    const response = await fetch("/api/data?id=" + encodeURIComponent(table.id));
    if (!response.ok) throw new Error(`${table.title} 로드 실패: ${await response.text()}`);
    const raw = await response.text();
    state.saved[table.id] = raw;
    state.data[table.id] = JSON.parse(raw);
  }));
  rebuildRefsets();
  validateAll();
  renderSidebar();
  openTable(state.catalog[0].id);
  $("#btn-save").addEventListener("click", saveCurrent);
  $("#btn-reload").addEventListener("click", reloadCurrent);
  window.addEventListener("keydown", event => {
    if ((event.ctrlKey || event.metaKey) && event.key === "s") { event.preventDefault(); saveCurrent(); }
  });
}

// --- 참조 집합 ---

function rebuildRefsets() {
  const ids = (tableId, rootPath, key) => {
    const list = tableId && state.data[tableId];
    const rows = rootPath ? list && list[rootPath] : list;
    return (rows || []).map(row => row[key]).filter(v => v != null);
  };
  state.refsets.enemy = ids("enemies", "_enemies", "_id");
  state.refsets.formation = ids("formations", "_formations", "_id");
  state.refsets.drop = ids("drops", "_dropTables", "_id");
  state.refsets.rune = ids("runes", "_runes", "_id");
  state.refsets.strings = ids("strings", "_entries", "_key");
  state.refsets.modifier = (state.data.runes._runes || [])
    .filter(r => r._category === "modifier").map(r => r._id);
  state.refsets.grade = state.data.grades._gradeOrder || [];
  const current = state.current && tableById(state.current);
  state.refsets.spellNode = current && baseId(current) === "spell"
    ? (state.data[current.id]._nodes || []).map(n => n._id) : [];
}

// --- 사이드바 ---

function renderSidebar() {
  const nav = $("#sidebar");
  nav.innerHTML = "";
  let lastGroup = null;
  for (const table of state.catalog) {
    if (table.group !== lastGroup) {
      nav.appendChild(el("div", { class: "group", text: table.group }));
      lastGroup = table.group;
    }
    const item = el("button", {
      class: "item" + (state.current === table.id ? " active" : ""),
      onclick: () => openTable(table.id),
    });
    item.appendChild(el("span", { text: table.title }));
    if (state.dirty[table.id]) item.appendChild(el("span", { class: "dot", text: "●" }));
    nav.appendChild(item);
  }
}

function openTable(id) {
  state.current = id;
  renderSidebar();
  renderView();
  refreshHeader();
}

function refreshHeader() {
  const table = tableById(state.current);
  $("#table-title").textContent = table ? `${table.title} — ${table.file}` : "";
  $("#dirty-mark").hidden = !state.dirty[table.id];
  $("#btn-save").disabled = !state.dirty[table.id];
}

function touch() {
  state.dirty[state.current] = true;
  rebuildRefsets();
  validateAll();
  refreshHeader();
  renderSidebar();
  refreshGridCells();
}

// --- 뷰 분기 ---

function renderView() {
  const table = tableById(state.current);
  const main = $("#main");
  const scroll = { top: main.scrollTop };
  main.innerHTML = "";
  switch (table.view) {
    case "list": renderListView(table, main); break;
    case "object": renderObjectView(table, main); break;
    case "balance": renderBalanceView(table, main); break;
    case "strings": renderStringsView(table, main); break;
    case "grades": renderGradesView(table, main); break;
    case "rooms": renderRoomsView(table, main); break;
    case "stages": renderStagesView(table, main); break;
  }
  main.scrollTop = scroll.top;
  markInputErrors();
}

function renderListHead(container, schema, array, table) {
  const columns = schema.columns;
  const tableEl = el("table", { class: "grid" });
  const head = el("tr");
  for (const column of columns) head.appendChild(el("th", { text: labelOf(column) }));
  head.appendChild(el("th", { text: "작업" }));
  tableEl.appendChild(head);
  array.forEach((item, index) => {
    const selected = state.selection[table.id] === index;
    const row = el("tr", { class: selected ? "selected" : "", onclick: () => { state.selection[table.id] = index; renderView(); } });
    for (const column of columns) {
      const cell = el("td", { class: typeof item[column] === "number" ? "num" : "" });
      cell.appendChild(el("span", { class: "mono", text: fmtCell(item[column]) }));
      row.appendChild(cell);
    }
    const actions = el("td", { class: "item-actions" });
    actions.appendChild(el("button", { text: "복제", onclick: event => { event.stopPropagation(); cloneRow(table, array, index); } }));
    actions.appendChild(el("button", { text: "삭제", onclick: event => { event.stopPropagation(); deleteRow(table, array, index); } }));
    row.appendChild(actions);
    tableEl.appendChild(row);
  });
  container.appendChild(tableEl);
}

function fmtCell(value) {
  if (value == null) return "";
  if (Array.isArray(value)) return `(${value.length} 항목)`;
  if (typeof value === "object") return "{ … }";
  return String(value);
}

function itemActions(table, array, index) {
  const wrap = el("div", { class: "row-actions" });
  wrap.appendChild(el("button", { text: "복제", onclick: () => cloneRow(table, array, index) }));
  wrap.appendChild(el("button", { text: "삭제", onclick: () => deleteRow(table, array, index) }));
  wrap.appendChild(el("button", { text: "↑", onclick: () => moveRow(table, array, index, -1) }));
  wrap.appendChild(el("button", { text: "↓", onclick: () => moveRow(table, array, index, 1) }));
  return wrap;
}

function cloneRow(table, array, index) {
  array.splice(index + 1, 0, JSON.parse(JSON.stringify(array[index])));
  state.selection[table.id] = index + 1;
  touch(); renderView();
}
function deleteRow(table, array, index) {
  if (!confirm("이 항목을 삭제할까요?")) return;
  array.splice(index, 1);
  state.selection[table.id] = Math.min(index, array.length - 1);
  touch(); renderView();
}
function moveRow(table, array, index, delta) {
  const target = index + delta;
  if (target < 0 || target >= array.length) return;
  [array[index], array[target]] = [array[target], array[index]];
  state.selection[table.id] = target;
  touch(); renderView();
}

function addItem(table, array, fieldDefs) {
  array.push(makeDefault(fieldDefs));
  state.selection[table.id] = array.length - 1;
  touch(); renderView();
}

function makeDefault(fields) {
  const obj = {};
  for (const field of resolveFields(fields)) obj[field.k] = defaultValueOf(field);
  return obj;
}
function defaultValueOf(field) {
  switch (field.type) {
    case "number": return 0;
    case "bool": return false;
    case "select": return (VOCAB[field.vocab] || [])[0] || "";
    case "multi": return [];
    case "list": return [];
    case "object": return makeDefault(field.fields);
    case "tiles": return [];
    case "ref": return field.allowEmpty ? "" : "";
    default: return "";
  }
}
function resolveFields(fields) {
  if (typeof fields === "string") return SCHEMAS[fields];
  return fields || [];
}

// --- 목록 뷰 ---

function renderListView(table, main) {
  const schema = SCHEMAS[baseId(table)];
  const array = getRoot(table);
  const toolbar = el("div", { class: "toolbar" });
  toolbar.appendChild(el("span", { class: "muted", text: `${array.length}개 항목` }));
  toolbar.appendChild(el("button", { text: "+ 항목 추가", onclick: () => addItem(table, array, firstFields(schema)) }));
  main.appendChild(toolbar);
  renderListHead(main, schema, array, table);

  const index = state.selection[table.id];
  if (index != null && array[index]) {
    const detail = el("div", { class: "detail" });
    const item = array[index];
    const itemKey = item._id || item._enemyId || `#${index + 1}`;
    detail.appendChild(el("h3", { text: `상세 — ${itemKey}` }));
    detail.appendChild(itemActions(table, array, index));
    renderFieldGroups(detail, groupsOf(schema), item, `${table.id}:${itemKey}`);
    main.appendChild(detail);
  }
}

function renderFieldGroups(container, groups, obj, pathPrefix) {
  // 전달된 모든 그룹의 필드를 알려진 키로 보고, 어느 그룹에도 없는 키만 기타로 렌더링한다.
  const known = new Set(groups.flatMap(group => (group.fields || group.keys || []).map(f => typeof f === "string" ? f : f.k)));
  for (const group of groups) {
    const set = el("fieldset");
    set.appendChild(el("legend", { text: group.title }));
    if (group.keys) {
      for (const key of group.keys)
        renderField(set, { k: key, type: inferType(obj[key]), vocab: key === "_mode" ? "ramMode" : undefined }, obj, `${pathPrefix}:${key}`, key);
    } else {
      for (const field of group.fields) renderField(set, field, obj, `${pathPrefix}:${field.k}`);
    }
    for (const key of Object.keys(obj)) {
      if (!known.has(key)) renderField(set, { k: key, type: inferType(obj[key]) }, obj, `${pathPrefix}:${key}`, key);
    }
    container.appendChild(set);
  }
}

function inferType(value) {
  if (typeof value === "number") return "number";
  if (typeof value === "boolean") return "bool";
  if (Array.isArray(value)) return "list";
  if (value && typeof value === "object") return "object";
  return "text";
}

// --- 필드 렌더링 ---

function renderField(container, field, obj, path, overrideKey) {
  const key = overrideKey || field.k;
  const value = obj[key];
  const row = el("div", { class: "field" });
  row.appendChild(el("label", { text: field.label || labelOf(key) }));

  const commit = newValue => {
    if (newValue === undefined) delete obj[key];
    else obj[key] = newValue;
    touch();
  };

  switch (field.type) {
    case "number": {
      const input = el("input", { type: "number", step: "any", value: value == null ? "" : String(value) });
      input.dataset.path = path;
      input.addEventListener("change", () => {
        if (input.value.trim() === "") { input.value = String(value ?? 0); return; }
        const parsed = Number(input.value);
        if (Number.isNaN(parsed)) { input.value = String(value ?? 0); return; }
        commit(parsed);
      });
      row.appendChild(input);
      break;
    }
    case "bool": {
      const input = el("input", { type: "checkbox", class: "chk" });
      input.checked = value === true;
      input.addEventListener("change", () => commit(input.checked));
      row.appendChild(input);
      break;
    }
    case "select": {
      const options = VOCAB[field.vocab] || [];
      const input = el("select");
      if (!options.includes(value)) input.appendChild(el("option", { value, text: value == null ? "" : String(value) }));
      for (const option of options) input.appendChild(el("option", { value: option, text: option, selected: option === value ? "" : null }));
      input.value = value == null ? "" : String(value);
      input.dataset.path = path;
      input.addEventListener("change", () => commit(input.value));
      row.appendChild(input);
      break;
    }
    case "ref": {
      const options = (REF_DEFS[field.ref] && REF_DEFS[field.ref].get()) || [];
      const input = el("select");
      if (field.allowEmpty) input.appendChild(el("option", { value: "", text: "(없음)" }));
      if (value != null && value !== "" && !options.includes(value))
        input.appendChild(el("option", { value, text: `${value} (없는 참조)` }));
      for (const option of options) input.appendChild(el("option", { value: option, text: option, selected: option === value ? "" : null }));
      input.value = value == null ? "" : String(value);
      input.dataset.path = path;
      input.addEventListener("change", () => commit(input.value === "" ? (field.allowEmpty ? "" : undefined) : input.value));
      row.appendChild(input);
      break;
    }
    case "multi": {
      const wrap = el("div", { class: "checks" });
      const options = VOCAB[field.vocab] || [];
      for (const option of options) {
        const check = el("input", { type: "checkbox", class: "chk" });
        check.checked = Array.isArray(value) && value.includes(option);
        check.addEventListener("change", () => {
          const list = Array.isArray(value) ? value : [];
          const next = check.checked ? [...new Set([...list, option])] : list.filter(v => v !== option);
          commit(next);
        });
        wrap.appendChild(el("label", {}, check, el("span", { text: option })));
      }
      row.appendChild(wrap);
      break;
    }
    case "tiles": {
      const wrap = el("div");
      const input = el("textarea", { rows: "10" });
      input.value = (value || []).join("\n");
      input.dataset.path = path;
      const preview = el("div", { class: "tilemap" });
      const repaint = () => paintTiles(preview, input.value.split("\n"));
      input.addEventListener("change", () => { commit(input.value.split("\n")); repaint(); });
      input.addEventListener("input", repaint);
      repaint();
      wrap.appendChild(input);
      wrap.appendChild(preview);
      row.appendChild(wrap);
      break;
    }
    case "list": {
      const wrap = el("div");
      const itemDefs = typeof field.item === "string" ? SCHEMAS[field.item] : field.item;
      const scalar = field.scalarItem;
      if (!itemDefs && !scalar) {
        // 항목 구조 정의가 없는 목록은 읽기 전용으로 표시한다.
        const view = el("textarea", { rows: Math.min(10, 2 + (value || []).length), style: "width:520px" });
        view.value = JSON.stringify(value || [], null, 2);
        view.disabled = true;
        wrap.appendChild(view);
        row.appendChild(wrap);
        break;
      }
      if (!Array.isArray(obj[key])) obj[key] = []; // 빈 목록을 명시적으로 두어 편집이 유실되지 않게 한다.
      const list = obj[key];
      const sub = el("details", { class: "sub" });
      sub.appendChild(el("summary", { text: `${field.label || labelOf(key)} (${list.length})` }));
      const body = el("div", { class: "sub-body" });
      list.forEach((item, index) => {
        const itemRow = el("div", { class: "field" });
        if (scalar) {
          const itemField = itemDefs[0];
          const input = makeRefOrTextInput(itemField, item);
          input.dataset.path = `${path}[${index}]`;
          input.addEventListener("change", () => { list[index] = input.value; touch(); });
          itemRow.appendChild(input);
        } else {
          const nested = el("details", { class: "sub" });
          nested.appendChild(el("summary", { text: `#${index + 1} — ${summarize(item)}` }));
          const nestedBody = el("div", { class: "sub-body" });
          renderFieldGroups(nestedBody, [{ title: "", fields: itemDefs }], item, `${path}[${index}]`);
          const actions = el("div", { class: "row-actions" });
          actions.appendChild(el("button", { text: "삭제", onclick: () => { list.splice(index, 1); touch(); renderView(); } }));
          actions.appendChild(el("button", { text: "↑", onclick: () => { if (index > 0) { [list[index - 1], list[index]] = [list[index], list[index - 1]]; touch(); renderView(); } } }));
          actions.appendChild(el("button", { text: "↓", onclick: () => { if (index < list.length - 1) { [list[index + 1], list[index]] = [list[index], list[index + 1]]; touch(); renderView(); } } }));
          nestedBody.appendChild(actions);
          nested.appendChild(nestedBody);
          itemRow.appendChild(nested);
        }
        itemRow.appendChild(el("button", {
          class: "remove", text: "제거",
          onclick: () => { list.splice(index, 1); touch(); renderView(); },
        }));
        body.appendChild(itemRow);
      });
      body.appendChild(el("button", {
        text: "+ 추가",
        onclick: () => {
          list.push(scalar ? "" : makeDefault(itemDefs));
          touch(); renderView();
        },
      }));
      sub.appendChild(body);
      wrap.appendChild(sub);
      row.appendChild(wrap);
      break;
    }
    case "object": {
      const wrap = el("div");
      if (!value || typeof value !== "object" || Array.isArray(value)) obj[key] = {}; // 빈 인스턴스를 명시적으로 둔다.
      const target = obj[key];
      const sub = el("details", { class: "sub" });
      sub.appendChild(el("summary", { text: field.label || labelOf(key) }));
      const body = el("div", { class: "sub-body" });
      renderFieldGroups(body, [{ title: "", fields: resolveFields(field.fields) }], target, path);
      sub.appendChild(body);
      wrap.appendChild(sub);
      row.appendChild(wrap);
      break;
    }
    default: {
      const input = el("input", { type: "text", value: value == null ? "" : String(value) });
      input.dataset.path = path;
      input.addEventListener("change", () => commit(input.value));
      row.appendChild(input);
    }
  }

  if (!field.required && field.type !== "list" && field.type !== "object") {
    row.appendChild(el("button", {
      class: "remove", title: "이 필드를 파일에서 제거합니다.",
      text: "✕",
      onclick: () => { commit(undefined); renderView(); },
    }));
  }
  container.appendChild(row);
}

function makeRefOrTextInput(field, value) {
  if (field.type === "ref") {
    const options = (REF_DEFS[field.ref] && REF_DEFS[field.ref].get()) || [];
    const input = el("select");
    if (field.allowEmpty) input.appendChild(el("option", { value: "", text: "(없음)" }));
    if (value != null && value !== "" && !options.includes(value))
      input.appendChild(el("option", { value, text: `${value} (없는 참조)` }));
    for (const option of options) input.appendChild(el("option", { value: option, text: option, selected: option === value ? "" : null }));
    input.value = value == null ? "" : String(value);
    return input;
  }
  return el("input", { type: "text", value: value == null ? "" : String(value) });
}

function summarize(item) {
  if (item == null) return "";
  return item._id || item._enemyId || item._key || item._runeId || item._portId || item._paramId || fmtCell(item);
}

function paintTiles(preview, lines) {
  preview.innerHTML = "";
  const width = Math.max(...lines.map(line => line.length), 1);
  preview.style.gridTemplateColumns = `repeat(${width}, 11px)`;
  for (const line of lines) {
    for (let x = 0; x < width; x++) {
      const ch = line[x] || "";
      let cls = "floor", text = null;
      if (ch === "#") cls = "wall";
      else if (ch >= "0" && ch <= "9") { cls = "spawn"; text = ch; }
      else if (ch !== ".") { cls = "wall"; text = ch || null; }
      preview.appendChild(el("span", { class: cls, text }));
    }
  }
}

// --- 특수 뷰: 스테이지 / 섹터 / 밸런스 / 현지화 / 등급표 ---

function renderStagesView(table, main) {
  const schema = SCHEMAS.stages;
  const root = state.data[table.id];
  const headWrap = el("div");
  const headSet = el("fieldset");
  headSet.appendChild(el("legend", { text: "공통 설정" }));
  for (const field of schema.head.filter(f => f.type !== "list" && f.k !== "_stages"))
    renderField(headSet, field, root, `${table.id}:${field.k}`);
  headWrap.appendChild(headSet);
  const rewardField = schema.head.find(f => f.k === "_rewards");
  const rewardSet = el("fieldset");
  rewardSet.appendChild(el("legend", { text: "보상표" }));
  renderField(rewardSet, { ...rewardField, label: "보상표" }, root, `${table.id}:보상표`);
  headWrap.appendChild(rewardSet);
  main.appendChild(headWrap);

  main.appendChild(el("h2", { class: "section", text: "스테이지" }));
  const array = root._stages || [];
  const toolbar = el("div", { class: "toolbar" });
  toolbar.appendChild(el("span", { class: "muted", text: `${array.length}개 항목` }));
  toolbar.appendChild(el("button", { text: "+ 스테이지 추가", onclick: () => addItem(table, array, schema.groups[0].fields) }));
  main.appendChild(toolbar);
  renderListHead(main, schema, array, table);
  const index = state.selection[table.id];
  if (index != null && array[index]) {
    const detail = el("div", { class: "detail" });
    const item = array[index];
    detail.appendChild(el("h3", { text: `스테이지 ${item._fromStage}${item._toStage ? `~${item._toStage}` : "~∞"}` }));
    detail.appendChild(itemActions(table, array, index));
    renderFieldGroups(detail, schema.groups, item, `${table.id}:스테이지#${index + 1}`);
    main.appendChild(detail);
  }
}

function renderRoomsView(table, main) {
  const schema = SCHEMAS.sector1;
  const array = getRoot(table);
  const toolbar = el("div", { class: "toolbar" });
  toolbar.appendChild(el("span", { class: "muted", text: `${array.length}개 방 (게임은 5개 방을 기대합니다)` }));
  main.appendChild(toolbar);
  renderListHead(main, schema, array, table);
  const index = state.selection[table.id];
  if (index != null && array[index]) {
    const detail = el("div", { class: "detail" });
    const item = array[index];
    detail.appendChild(el("h3", { text: `방 — ${item._id || `#${index + 1}`}` }));
    detail.appendChild(itemActions(table, array, index));
    renderFieldGroups(detail, schema.groups, item, `${table.id}:${item._id || index}`);
    main.appendChild(detail);
  }
}

function renderObjectView(table, main) {
  const schema = SCHEMAS.spell;
  const root = state.data[table.id];
  const wrap = el("div");
  renderFieldGroups(wrap, schema.groups, root, table.id);
  main.appendChild(wrap);
}

function renderBalanceView(table, main) {
  const schema = SCHEMAS.balance;
  const root = state.data[table.id];
  const wrap = el("div");
  renderFieldGroups(wrap, schema.groups, root, table.id);
  main.appendChild(wrap);
}

function renderStringsView(table, main) {
  const entries = state.data.strings._entries || [];
  const toolbar = el("div", { class: "toolbar" });
  const search = el("input", { type: "text", placeholder: "키·값 검색", value: state.stringsFilter });
  search.addEventListener("input", () => { state.stringsFilter = search.value; refreshRows(); });
  toolbar.appendChild(search);
  toolbar.appendChild(el("span", { class: "muted", text: `${entries.length}키` }));
  toolbar.appendChild(el("button", {
    text: "+ 키 추가",
    onclick: () => { entries.push({ _key: "new.key", _value: "" }); touch(); renderView(); },
  }));
  main.appendChild(toolbar);

  const tableEl = el("table", { class: "grid" });
  tableEl.appendChild(el("tr", {}, el("th", { text: "키" }), el("th", { text: "값" }), el("th", { text: "작업" })));
  const filter = state.stringsFilter.toLowerCase();
  entries.forEach((entry, index) => {
    if (filter && !(`${entry._key} ${entry._value}`).toLowerCase().includes(filter)) return;
    const row = el("tr");
    const keyInput = el("input", { type: "text", value: entry._key ?? "", class: "mono" });
    keyInput.style.width = "260px";
    keyInput.addEventListener("change", () => { entry._key = keyInput.value; touch(); });
    const valueInput = el("input", { type: "text", value: entry._value ?? "" });
    valueInput.style.width = "420px";
    valueInput.addEventListener("change", () => { entry._value = valueInput.value; touch(); });
    const keyCell = el("td", {}, keyInput);
    if (state.errors.strings) {
      const dup = state.errors.strings.find(e => e.level === "error" && e.msg.includes(`"${entry._key}"`));
      if (dup) keyInput.classList.add("err");
    }
    row.appendChild(keyCell);
    row.appendChild(el("td", {}, valueInput));
    const actions = el("td", { class: "item-actions" });
    actions.appendChild(el("button", { text: "삭제", onclick: () => { entries.splice(index, 1); touch(); renderView(); } }));
    row.appendChild(actions);
    tableEl.appendChild(row);
  });
  main.appendChild(tableEl);
}

function renderGradesView(table, main) {
  const g = state.data.grades;
  const wrap = el("div");

  const headSet = el("fieldset");
  headSet.appendChild(el("legend", { text: "표 정보" }));
  const versionRow = el("div", { class: "field" });
  versionRow.appendChild(el("label", { text: "스키마 버전" }));
  const versionInput = el("input", { type: "number", value: g._schemaVersion ?? 1 });
  versionInput.addEventListener("change", () => { g._schemaVersion = Number(versionInput.value) || 1; touch(); });
  versionRow.appendChild(versionInput);
  headSet.appendChild(versionRow);

  const orderRow = el("div", { class: "field" });
  orderRow.appendChild(el("label", { text: "등급 순서" }));
  const orderInput = el("input", { type: "text", value: (g._gradeOrder || []).join(", "), class: "mono" });
  orderInput.addEventListener("change", () => {
    g._gradeOrder = orderInput.value.split(",").map(s => s.trim()).filter(Boolean);
    for (const effect of g._modifierGradeEffects || []) {
      const values = gradeValuesForEffect(effect);
      for (const grade of g._gradeOrder) if (!(grade in values)) values[grade] = effect._effectType === "energyCostMultiplier" ? 0.8 : null;
    }
    touch(); renderView();
  });
  orderRow.appendChild(orderInput);
  headSet.appendChild(orderRow);
  wrap.appendChild(headSet);

  wrap.appendChild(el("h2", { class: "section", text: "Modifier 등급 효과" }));
  const grades = g._gradeOrder || [];
  const effectTable = el("table", { class: "grid" });
  const effectHead = el("tr");
  for (const name of ["Modifier", "이름", "효과 종류", ...grades]) effectHead.appendChild(el("th", { text: name }));
  effectHead.appendChild(el("th", { text: "작업" }));
  effectTable.appendChild(effectHead);
  (g._modifierGradeEffects || []).forEach((effect, index) => {
    const row = el("tr");
    const modifierInput = makeRefOrTextInput({ type: "ref", ref: "modifier" }, effect._modifierId);
    modifierInput.addEventListener("change", () => { effect._modifierId = modifierInput.value; touch(); });
    row.appendChild(el("td", {}, modifierInput));
    const nameInput = el("input", { type: "text", value: effect._name ?? "" });
    nameInput.addEventListener("change", () => { effect._name = nameInput.value; touch(); });
    row.appendChild(el("td", {}, nameInput));
    const effectSelect = el("select");
    for (const option of VOCAB.effectType) effectSelect.appendChild(el("option", { value: option, text: option, selected: option === effect._effectType ? "" : null }));
    if (!VOCAB.effectType.includes(effect._effectType)) effectSelect.appendChild(el("option", { value: effect._effectType, text: effect._effectType, selected: "" }));
    effectSelect.value = effect._effectType || "";
    effectSelect.addEventListener("change", () => {
      effect._effectType = effectSelect.value;
      const values = gradeValuesForEffect(effect);
      for (const grade of grades)
        if (gradeValueError(effect._effectType, values[grade]) !== null) values[grade] = effect._effectType === "energyCostMultiplier" ? 0.8 : null;
      touch(); renderView();
    });
    row.appendChild(el("td", {}, effectSelect));
    for (const grade of grades) {
      const cell = el("td", {}, gradeValueControl(effect, grade));
      if (effect._effectType === "beamEmpowerMultiplier") {
        for (const [key, label] of [["_beamEmpowerManaMultByGrade", "최대 마나"], ["_beamEmpowerSizeMultByGrade", "최대 빔 폭"]]) {
          const values = effect[key] || {};
          const input = el("input", { type: "number", min: "1", step: "0.01", value: values[grade] ?? "", style: "width:72px" });
          input.addEventListener("change", () => {
            effect[key] = effect[key] || {};
            effect[key][grade] = Number(input.value);
            touch();
          });
          cell.appendChild(el("div", {}, el("span", { class: "muted", text: label + " × " }), input));
        }
      }
      row.appendChild(cell);
    }
    const actions = el("td", { class: "item-actions" });
    actions.appendChild(el("button", { text: "삭제", onclick: () => { g._modifierGradeEffects.splice(index, 1); touch(); renderView(); } }));
    row.appendChild(actions);
    effectTable.appendChild(row);
  });
  wrap.appendChild(effectTable);
  wrap.appendChild(el("button", {
    text: "+ Modifier 추가",
    onclick: () => {
      const values = {};
      for (const grade of grades) values[grade] = null;
      (g._modifierGradeEffects = g._modifierGradeEffects || []).push({ _modifierId: "", _name: "", _effectType: "effectMultiplier", _gradeValues: values });
      touch(); renderView();
    },
  }));

  wrap.appendChild(el("h2", { class: "section", text: "스테이지 드롭표" }));
  const stageTable = el("table", { class: "grid" });
  const stageHead = el("tr");
  for (const name of ["스테이지", "종류", ...grades, "보스 보상"]) stageHead.appendChild(el("th", { text: name }));
  stageHead.appendChild(el("th", { text: "작업" }));
  stageTable.appendChild(stageHead);
  (g._stageDropTables || []).forEach((row_, index) => {
    const row = el("tr");
    const stageInput = el("input", { type: "number", value: row_._stage ?? 0, style: "width:60px" });
    stageInput.addEventListener("change", () => { row_._stage = Number(stageInput.value) || 0; touch(); });
    row.appendChild(el("td", {}, stageInput));
    const typeSelect = el("select");
    for (const option of VOCAB.stageType) typeSelect.appendChild(el("option", { value: option, text: option, selected: option === row_._stageType ? "" : null }));
    typeSelect.value = row_._stageType || "normal";
    typeSelect.addEventListener("change", () => {
      row_._stageType = typeSelect.value;
      if (typeSelect.value === "boss") {
        row_._eliteModifierGradeChancesPercent = null;
        row_._stageClearModifierRewardChoice = row_._stageClearModifierRewardChoice
          || { _grade: "S", _choiceCount: 3, _selectCount: 1, _drawMethod: "uniformWithoutReplacement", _candidateRule: "_gradeValues.S is not null" };
      } else {
        row_._stageClearModifierRewardChoice = undefined;
        const chances = {};
        for (const grade of grades) chances[grade] = 0;
        row_._eliteModifierGradeChancesPercent = chances;
      }
      touch(); renderView();
    });
    row.appendChild(el("td", {}, typeSelect));
    const chances = row_._eliteModifierGradeChancesPercent;
    for (const grade of grades) {
      const cell = el("td");
      if (chances && grade in chances) {
        const input = el("input", { type: "number", value: chances[grade] ?? 0, style: "width:64px" });
        input.addEventListener("change", () => { chances[grade] = Number(input.value) || 0; touch(); });
        cell.appendChild(input);
      } else cell.appendChild(el("span", { class: "muted", text: "—" }));
      row.appendChild(cell);
    }
    const bossCell = el("td");
    const choice = row_._stageClearModifierRewardChoice;
    if (choice) {
      bossCell.appendChild(el("span", { class: "mono", text: `${choice._grade}급 ${choice._choiceCount}중 ${choice._selectCount} 선택` }));
    } else bossCell.appendChild(el("span", { class: "muted", text: "—" }));
    row.appendChild(bossCell);
    const actions = el("td", { class: "item-actions" });
    actions.appendChild(el("button", { text: "삭제", onclick: () => { g._stageDropTables.splice(index, 1); touch(); renderView(); } }));
    row.appendChild(actions);
    stageTable.appendChild(row);
  });
  wrap.appendChild(stageTable);
  wrap.appendChild(el("button", {
    text: "+ 스테이지 추가",
    onclick: () => {
      const chances = {};
      for (const grade of grades) chances[grade] = 0;
      (g._stageDropTables = g._stageDropTables || []).push({ _stage: 1, _stageType: "normal", _eliteModifierGradeChancesPercent: chances });
      touch(); renderView();
    },
  }));

  wrap.appendChild(el("h2", { class: "section", text: "데이터 메모" }));
  const notes = el("textarea", { rows: "5", style: "width:720px" });
  notes.value = (g._dataNotes || []).join("\n");
  notes.addEventListener("change", () => { g._dataNotes = notes.value.split("\n").filter(line => line.trim() !== ""); touch(); });
  wrap.appendChild(notes);

  main.appendChild(wrap);
}

// mod.homing에 정의된 유도 단계 이름 목록을 반환한다.
function homingTierNames() {
  const homing = (state.data.runes && state.data.runes._runes || []).find(row => row._id === "mod.homing");
  return ((homing && homing._homingTiers) || []).map(tier => tier._tier).filter(Boolean);
}

// 절약은 기존 비용 배율을 원본으로 사용하고 다른 효과는 별도 등급 효과 값을 편집한다.
function gradeValuesForEffect(effect) {
  const key = effect._effectType === "energyCostMultiplier" ? "_energyMultByGrade" : "_gradeValues";
  return effect[key] || (effect[key] = {});
}

// 효과 종류에 맞지 않는 등급 값이면 오류 문구를, 유효한 값이면 null을 반환한다.
function gradeValueError(effectType, value) {
  if (effectType === "energyCostMultiplier") return typeof value === "number" && isFinite(value) && value > 0 && value < 1 ? null : "0보다 크고 1보다 작은 전력 소비 배율이어야 합니다.";
  if (value === null || value === undefined) return null;
  const kind = GRADE_VALUE_KINDS[effectType];
  if (kind === "decimal") return typeof value === "number" && isFinite(value) && value > 0 ? null : "0보다 큰 숫자(배율)여야 합니다.";
  if (kind === "integer") return Number.isInteger(value) && value >= 0 ? null : "0 이상 정수여야 합니다.";
  if (kind === "integerOrAll") return (Number.isInteger(value) && value >= 0) || value === "ALL" ? null : "0 이상 정수 또는 ALL(무제한)이어야 합니다.";
  if (kind === "homingTier") return typeof value === "string" && homingTierNames().includes(value) ? null : "mod.homing에 정의된 유도 단계 이름이어야 합니다.";
  return "알 수 없는 효과 종류입니다.";
}

// 효과 종류마다 값 형식이 정해진 등급 값 입력을 만든다. 체크를 끄면 그 등급이 없음(null)이다.
// 배율은 소수, 개수는 정수, 관통은 정수 또는 무제한(ALL), 유도는 단계 선택으로만 입력한다.
function gradeValueControl(effect, grade) {
  const values = gradeValuesForEffect(effect);
  const kind = GRADE_VALUE_KINDS[effect._effectType];
  const wrap = el("div", { class: "field", style: "gap:4px" });
  const value = values[grade];
  const has = value !== null && value !== undefined;
  const check = el("input", { type: "checkbox", class: "chk", title: "등급 있음" });
  check.checked = has;
  wrap.appendChild(check);
  const defaults = { decimal: 1, integer: 1, integerOrAll: 1, homingTier: homingTierNames()[0] ?? null };
  check.addEventListener("change", () => {
    values[grade] = check.checked ? (effect._effectType === "energyCostMultiplier" ? 0.8 : (defaults[kind] ?? null)) : null;
    touch(); renderView();
  });
  if (!has) return wrap;
  if (kind === "homingTier") {
    const select = el("select");
    const names = homingTierNames();
    for (const name of names) select.appendChild(el("option", { value: name, text: name, selected: name === value ? "" : null }));
    if (!names.includes(value)) select.appendChild(el("option", { value: String(value), text: `${value} (없음)`, selected: "" }));
    select.addEventListener("change", () => { values[grade] = select.value; touch(); });
    wrap.appendChild(select);
  } else if (kind === "integerOrAll") {
    const isAll = value === "ALL";
    const input = el("input", { type: "number", step: "1", min: "0", value: isAll ? "" : String(value), style: "width:64px" });
    input.disabled = isAll;
    input.addEventListener("change", () => { values[grade] = Math.max(0, Math.round(Number(input.value) || 0)); touch(); });
    const all = el("input", { type: "checkbox", class: "chk", title: "무제한(ALL)" });
    all.checked = isAll;
    all.addEventListener("change", () => { values[grade] = all.checked ? "ALL" : 1; touch(); renderView(); });
    wrap.appendChild(input);
    wrap.appendChild(all);
    wrap.appendChild(el("span", { class: "muted", text: "ALL" }));
  } else {
    const isInteger = kind === "integer";
    const input = el("input", { type: "number", step: isInteger ? "1" : "0.01", min: isInteger ? "0" : "0.01", value: String(value), style: "width:72px" });
    input.addEventListener("change", () => {
      const number = Number(input.value) || 0;
      values[grade] = isInteger ? Math.max(0, Math.round(number)) : number;
      touch(); renderView();
    });
    wrap.appendChild(input);
  }
  const error = gradeValueError(effect._effectType, value);
  if (error) wrap.appendChild(el("span", { class: "muted", text: "⚠", title: error }));
  return wrap;
}

// --- 검증 ---

function validateAll() {
  state.errors = {};
  for (const table of state.catalog) {
    const validator = VALIDATORS[baseId(table)];
    state.errors[table.id] = validator ? validator(table) : [];
  }
  const all = Object.values(state.errors).flat();
  const errors = all.filter(e => e.level === "error").length;
  const warns = all.filter(e => e.level === "warn").length;
  const summary = $("#error-summary");
  summary.textContent = errors || warns ? `오류 ${errors} · 경고 ${warns}` : "검증 통과";
  summary.className = "error-summary" + (errors ? " has-error" : "");
  $("#error-count").textContent = errors || warns ? `(${errors} 오류 / ${warns} 경고)` : "";
  const list = $("#error-list");
  list.innerHTML = "";
  const current = state.errors[state.current] || [];
  const others = all.filter(e => !current.includes(e));
  for (const item of [...current, ...others]) {
    list.appendChild(el("div", { class: `error-item ${item.level}` },
      el("span", { class: "path", text: item.path + " " }), el("span", { text: item.msg })));
  }
  if (!all.length) list.appendChild(el("div", { class: "muted", text: "문제 없음" }));
}

function markInputErrors() {
  const errors = state.errors[state.current] || [];
  for (const input of document.querySelectorAll("#main [data-path]")) {
    input.classList.toggle("err", errors.some(e => e.level === "error" && e.path === input.dataset.path));
  }
}

function idsUnique(errors, tableId, rows, key) {
  const seen = new Set();
  rows.forEach((row, index) => {
    const value = row[key];
    if (value == null || value === "") {
      errors.push({ level: "error", path: `${tableId}:#${index + 1}:${key}`, msg: "ID가 비어 있습니다." });
      return;
    }
    if (seen.has(value)) errors.push({ level: "error", path: `${tableId}:${value}:${key}`, msg: `ID가 중복됩니다: ${value}` });
    seen.add(value);
  });
}

function checkRef(errors, level, path, value, refKey, allowEmpty) {
  if (value == null || value === "") {
    if (!allowEmpty) errors.push({ level, path, msg: "참조가 비어 있습니다." });
    return;
  }
  const options = (REF_DEFS[refKey] && REF_DEFS[refKey].get()) || [];
  if (!options.includes(value)) errors.push({ level, path, msg: `존재하지 않는 참조입니다: ${value}` });
}

const VALIDATORS = {
  runes(table) {
    const errors = [];
    const rows = state.data.runes._runes || [];
    idsUnique(errors, "runes", rows, "_id");
    rows.forEach(row => {
      const p = `runes:${row._id}`;
      if (row._category && !VOCAB.runeCategory.includes(row._category)) errors.push({ level: "error", path: `${p}:_category`, msg: `알 수 없는 카테고리: ${row._category}` });
      for (const target of row._modifierTargets || []) checkRef(errors, "error", `${p}:_modifierTargets`, target, "rune");
      if (row._element && row._element.HasAnyValue !== false) {
        const element = row._element;
        const hasValue = element._id || element._category || element._internalValue || element._runtimeTag;
        if (hasValue) {
          if (element._category && !VOCAB.elementCategory.includes(element._category)) errors.push({ level: "error", path: `${p}:_element`, msg: `분류는 elemental/functional: ${element._category}` });
          if (row._category !== "element") errors.push({ level: "error", path: `${p}:_element`, msg: "element 카테고리 룬에만 속성을 둘 수 있습니다." });
        }
      }
      (row._ports || []).forEach((port, index) => {
        const path = `${p}:_ports[${index}]`;
        if (port._kind && !VOCAB.portKind.includes(port._kind)) errors.push({ level: "error", path: `${path}:_kind`, msg: `포트 종류 오류: ${port._kind}` });
        if (port._direction && !VOCAB.portDirection.includes(port._direction)) errors.push({ level: "error", path: `${path}:_direction`, msg: `포트 방향 오류: ${port._direction}` });
        if ((port._kind === "chain" || port._kind === "element") && port._max !== 1) errors.push({ level: "error", path: `${path}:_max`, msg: `${port._kind} 포트의 최대 연결은 1이어야 합니다.` });
        if (port._kind === "exec" && port._direction === "in" && port._max !== 1) errors.push({ level: "error", path: `${path}:_max`, msg: "exec 입력 포트의 최대 연결은 1이어야 합니다." });
        if (port._kind === "element" && port._direction === "in" && row._category !== "shape") errors.push({ level: "error", path: `${path}`, msg: "element 입력 포트는 shape 룬에만 둘 수 있습니다." });
        if (port._kind === "element" && port._direction === "out" && row._category !== "element") errors.push({ level: "error", path: `${path}`, msg: "element 출력 포트는 element 룬에만 둘 수 있습니다." });
      });
      if (row._category === "shape" && !(row._ports || []).some(port => port._kind === "element" && port._direction === "in"))
        errors.push({ level: "warn", path: `${p}:_ports`, msg: "shape 룬에 element 입력 포트가 없어 속성 노드를 붙일 수 없습니다." });
      if (row._category === "element" && row._id !== "element.neutral" && !(row._ports || []).some(port => port._kind === "element" && port._direction === "out"))
        errors.push({ level: "warn", path: `${p}:_ports`, msg: "속성 룬에 element 출력 포트가 없어 그래프에 연결할 수 없습니다." });
      if (row._homingTiers && row._homingTiers.length && row._id !== "mod.homing")
        errors.push({ level: "error", path: `${p}:_homingTiers`, msg: "유도 단계는 mod.homing에만 둘 수 있습니다." });
      (row._params || []).forEach((param, index) => {
        if (param._kind === "enum" && param._defaultText) {
          const options = (param._options || []).map(option => option._option);
          if (!options.includes(param._defaultText)) errors.push({ level: "error", path: `${p}:_params[${index}]`, msg: `기본값이 선택지에 없습니다: ${param._defaultText}` });
        }
      });
    });
    return errors;
  },
  enemies(table) {
    const errors = [];
    const rows = state.data.enemies._enemies || [];
    idsUnique(errors, "enemies", rows, "_id");
    rows.forEach(row => {
      const p = `enemies:${row._id}`;
      if (row._movement && !VOCAB.enemyMovement.includes(row._movement)) errors.push({ level: "error", path: `${p}:_movement`, msg: `알 수 없는 이동 방식: ${row._movement}` });
      const attack = row._attack || "contact";
      if (!VOCAB.enemyAttack.includes(attack)) errors.push({ level: "error", path: `${p}:_attack`, msg: `알 수 없는 공격 방식: ${attack}` });
      for (const trait of row._traits || []) if (!VOCAB.enemyTrait.includes(trait)) errors.push({ level: "error", path: `${p}:_traits`, msg: `알 수 없는 특수 능력: ${trait}` });
      const positive = value => typeof value === "number" && isFinite(value) && value > 0;
      const has = trait => (row._traits || []).includes(trait);
      if (has("deathExplosion") && (!positive(row._explosionRadius) || !positive(row._explosionDamage))) errors.push({ level: "error", path: `${p}:_explosionRadius`, msg: "사망 폭발은 반경·피해가 양수여야 합니다." });
      if (has("circleShield") && (!(row._shieldRadius > row._radius))) errors.push({ level: "error", path: `${p}:_shieldRadius`, msg: "원형 보호막은 몸체보다 커야 합니다." });
      if (attack === "sniper" && (row._movement !== "sniper" || !positive(row._aimSeconds) || !positive(row._lockSeconds) || !positive(row._attackInterval) || !positive(row._attackRange) || row._attackInterval <= row._aimSeconds + row._lockSeconds))
        errors.push({ level: "error", path: `${p}:_attack`, msg: "저격 설정 오류(이동=sniper, 조준+잠금<공격 간격, 사거리 필요)." });
      if (attack === "interceptor" && (row._movement !== "interceptor" || !positive(row._moveSeconds) || !positive(row._stopSeconds) || !positive(row._preferredDistance) || !positive(row._speed)))
        errors.push({ level: "error", path: `${p}:_attack`, msg: "인터셉터 설정 오류(이동=interceptor, 이동·정지·선호 거리 필요)." });
      if ((attack === "sniper" || attack === "interceptor") && (!positive(row._projectileSpeed) || !positive(row._projectileRadius) || !positive(row._projectileLifetime)))
        errors.push({ level: "error", path: `${p}:_projectileSpeed`, msg: "저격·인터셉터는 탄환 속도·반경·수명이 필요합니다." });
      if ((row._movement === "sniper" && attack !== "sniper") || (row._movement === "interceptor" && attack !== "interceptor"))
        errors.push({ level: "error", path: `${p}:_movement`, msg: "이동·공격 조합 오류입니다." });
      if (row._dropTable) checkRef(errors, "error", `${p}:_dropTable`, row._dropTable, "drop");
      if (row.CanSplit || (row._splitInto && row._splitCount > 0)) {
        checkRef(errors, "error", `${p}:_splitInto`, row._splitInto, "enemy");
        if (row._splitInto === row._id) errors.push({ level: "error", path: `${p}:_splitInto`, msg: "자기 자신으로 분열할 수 없습니다." });
      }
    });
    return errors;
  },
  formations(table) {
    const errors = [];
    const rows = state.data.formations._formations || [];
    idsUnique(errors, "formations", rows, "_id");
    rows.forEach(row => {
      const p = `formations:${row._id}`;
      if (row._shape && !VOCAB.formationShape.includes(row._shape)) errors.push({ level: "error", path: `${p}:_shape`, msg: `구현되지 않은 진형 모양: ${row._shape}` });
      if (row._angle && !VOCAB.angle.includes(row._angle)) errors.push({ level: "error", path: `${p}:_angle`, msg: `각도는 random/ahead: ${row._angle}` });
    });
    return errors;
  },
  bosses(table) {
    const errors = [];
    const rows = state.data.bosses._bosses || [];
    rows.forEach(row => {
      const p = `bosses:${row._enemyId || "?"}`;
      checkRef(errors, "error", `${p}:_enemyId`, row._enemyId, "enemy");
      if (row._pattern && row._pattern !== "governor") errors.push({ level: "error", path: `${p}:_pattern`, msg: `알 수 없는 보스 패턴: ${row._pattern}` });
      if (row._phaseTwoThreshold <= row._phaseThreeThreshold) errors.push({ level: "error", path: `${p}:_phaseTwoThreshold`, msg: "2페이즈 문턱이 3페이즈 문턱보다 커야 합니다." });
      if (row._reinforcement) {
        checkRef(errors, "error", `${p}:_reinforcement._enemyId`, row._reinforcement._enemyId, "enemy");
        checkRef(errors, "error", `${p}:_reinforcement._formation`, row._reinforcement._formation, "formation");
        if (!(row._reinforcement._count >= 1)) errors.push({ level: "error", path: `${p}:_reinforcement._count`, msg: "증원 수량은 1 이상이어야 합니다." });
      } else errors.push({ level: "error", path: `${p}:_reinforcement`, msg: "증원 설정이 필요합니다." });
      if (row._dropTable) checkRef(errors, "error", `${p}:_dropTable`, row._dropTable, "drop");
    });
    return errors;
  },
  drops(table) {
    const errors = [];
    const tables = state.data.drops._dropTables || [];
    idsUnique(errors, "drops", tables, "_id");
    tables.forEach(tableDef => {
      const p = `drops:${tableDef._id || "?"}`;
      if (tableDef._rolls < 0) errors.push({ level: "error", path: `${p}:_rolls`, msg: "추첨 횟수는 0 이상이어야 합니다." });
      if (tableDef._rolls > 0 && !(tableDef._entries || []).length) errors.push({ level: "error", path: `${p}:_entries`, msg: "추첨 횟수가 있으면 항목이 필요합니다." });
      for (const [field, checkWeight] of [["_guaranteed", false], ["_entries", true]]) {
        (tableDef[field] || []).forEach((entry, index) => {
          const path = `${p}:${field}[${index}]`;
          if (entry._type && !VOCAB.dropType.includes(entry._type)) errors.push({ level: "error", path: `${path}:_type`, msg: `항목 형식 오류: ${entry._type}` });
          if (checkWeight && (!(entry._weight > 0) || !isFinite(entry._weight))) errors.push({ level: "error", path: `${path}:_weight`, msg: "가중치는 0보다 큰 유한값이어야 합니다." });
          if (entry._type === "modifier") {
            if (entry._id !== "eliteModifier") {
              checkRef(errors, "error", `${path}:_id`, entry._id, "modifier");
              if (entry._grade && state.data.grades) {
                const effect = (state.data.grades._modifierGradeEffects || []).find(e => e._modifierId === entry._id);
                if (effect && effect._gradeValues && entry._grade in effect._gradeValues && effect._gradeValues[entry._grade] === null)
                  errors.push({ level: "error", path: `${path}:_grade`, msg: `이 Modifier에 없는 등급입니다: ${entry._grade}` });
              }
            }
            if (!(entry._count >= 1)) errors.push({ level: "error", path: `${path}:_count`, msg: "수량은 1 이상이어야 합니다." });
          }
          if (entry._type === "fragments" && ((entry._min < 0) || (entry._max < entry._min))) errors.push({ level: "error", path: `${path}:_min`, msg: "0 ≤ 최소 ≤ 최대여야 합니다." });
        });
      }
    });
    return errors;
  },
  sector1(table) {
    const errors = [];
    const rooms = state.data.sector1._rooms || [];
    if (rooms.length !== 5) errors.push({ level: "warn", path: "sector1", msg: `게임은 5개 방을 기대합니다 (현재 ${rooms.length}).` });
    rooms.forEach(room => {
      const p = `sector1:${room._id || "?"}`;
      if (room._nameKey && !state.refsets.strings.includes(room._nameKey)) errors.push({ level: "warn", path: `${p}:_nameKey`, msg: `현지화 키가 strings.ko.json에 없습니다: ${room._nameKey}` });
      const tiles = room._tiles || [];
      const widths = new Set(tiles.map(line => line.length));
      if (widths.size > 1) errors.push({ level: "error", path: `${p}:_tiles`, msg: "타일 행의 길이가 서로 다릅니다." });
      const joined = tiles.join("\n");
      (room._waves || []).forEach((wave, waveIndex) => {
        (wave._spawns || []).forEach((spawn, spawnIndex) => {
          const path = `${p}:_waves[${waveIndex}]._spawns[${spawnIndex}]`;
          checkRef(errors, "error", `${path}:_enemyId`, spawn._enemyId, "enemy");
          if (!(spawn._count >= 1)) errors.push({ level: "error", path: `${path}:_count`, msg: "수량은 1 이상이어야 합니다." });
          const ch = String.fromCharCode("0".charCodeAt(0) + (spawn._point || 0));
          if (!tiles.some(line => line.includes(ch))) errors.push({ level: "error", path: `${path}:_point`, msg: `타일에 스폰 포인트 '${ch}'가 없습니다.` });
        });
      });
    });
    return errors;
  },
  stages(table) {
    const errors = [];
    const rows = state.data.stages._stages || [];
    rows.forEach((row, rowIndex) => {
      const p = `stages:#${rowIndex + 1}`;
      if (row._kind && !VOCAB.stageKind.includes(row._kind)) errors.push({ level: "error", path: `${p}:_kind`, msg: `종류는 normal/boss: ${row._kind}` });
      if (row._toStage !== 0 && row._toStage < row._fromStage) errors.push({ level: "error", path: `${p}:_toStage`, msg: "끝 스테이지가 시작 스테이지보다 작습니다." });
      const stream = row._stream;
      if (stream) {
        checkRef(errors, "error", `${p}:_stream._formation`, stream._formation, "formation");
        for (const candidate of stream._candidates || []) checkRef(errors, "error", `${p}:_stream._candidates`, candidate._enemyId, "enemy");
      }
      for (const wave of row._waves || []) {
        if (wave._formation && wave._formation._id) checkRef(errors, "error", `${p}:_waves._formation`, wave._formation._id, "formation");
        for (const unit of wave._units || []) checkRef(errors, "error", `${p}:_waves._units`, unit._enemyId, "enemy");
      }
      for (const elite of row._eliteTypes || []) {
        checkRef(errors, "error", `${p}:_eliteTypes`, elite._enemyId, "enemy");
        if (elite._dropTable) checkRef(errors, "error", `${p}:_eliteTypes._dropTable`, elite._dropTable, "drop");
      }
      for (const reward of row._modifierRewards || []) {
        checkRef(errors, "error", `${p}:_modifierRewards`, reward._runeId, "modifier");
        const effect = (state.data.grades._modifierGradeEffects || []).find(e => e._modifierId === reward._runeId);
        if (effect && effect._gradeValues && reward._grade in effect._gradeValues && effect._gradeValues[reward._grade] === null)
          errors.push({ level: "error", path: `${p}:_modifierRewards`, msg: `이 Modifier에 없는 등급입니다: ${reward._grade}` });
      }
      for (const reward of state.data.stages._rewards || []) checkRef(errors, "error", `stages:_rewards`, reward._enemyId, "enemy");
    });
    return errors;
  },
  spell(table) {
    const errors = [];
    const file = state.data[table.id];
    const nodeIds = new Set((file._nodes || []).map(node => node._id));
    if (file._nodes && nodeIds.size !== file._nodes.length) errors.push({ level: "error", path: `${table.id}:_nodes`, msg: "노드 ID가 중복됩니다." });
    for (const edge of file._edges || []) {
      if (!nodeIds.has(edge._fromNode)) errors.push({ level: "error", path: `${table.id}:_edges:${edge._id}`, msg: `시작 노드가 없습니다: ${edge._fromNode}` });
      if (!nodeIds.has(edge._toNode)) errors.push({ level: "error", path: `${table.id}:_edges:${edge._id}`, msg: `대상 노드가 없습니다: ${edge._toNode}` });
    }
    for (const node of file._nodes || [])
      if (node._runeId && !state.refsets.rune.includes(node._runeId))
        errors.push({ level: "warn", path: `${table.id}:_nodes:${node._id}`, msg: `룬 ID가 runes.json에 없습니다(마이그레이션 대상일 수 있음): ${node._runeId}` });
    return errors;
  },
  grades(table) {
    const errors = [];
    const g = state.data.grades;
    const grades = g._gradeOrder || [];
    for (const effect of g._modifierGradeEffects || []) {
      const p = `grades:${effect._modifierId || "?"}`;
      checkRef(errors, "error", `${p}:_modifierId`, effect._modifierId, "modifier");
      if (!GRADE_VALUE_KINDS[effect._effectType]) errors.push({ level: "error", path: `${p}:_effectType`, msg: `알 수 없는 효과 종류: ${effect._effectType}` });
      const values = gradeValuesForEffect(effect);
      for (const grade of grades) {
        const message = gradeValueError(effect._effectType, values[grade]);
        if (message && GRADE_VALUE_KINDS[effect._effectType]) errors.push({ level: "error", path: `${p}:_gradeValues.${grade}`, msg: message });
      }
      if (!grades.some(grade => values[grade] !== null && values[grade] !== undefined))
        errors.push({ level: "error", path: `${p}:_gradeValues`, msg: "값이 있는 등급이 하나 이상 있어야 합니다." });
      if (effect._effectType === "beamEmpowerMultiplier") {
        for (const key of ["_beamEmpowerManaMultByGrade", "_beamEmpowerSizeMultByGrade"]) {
          for (const grade of grades) {
            const multiplier = effect[key]?.[grade];
            if (typeof multiplier !== "number" || !Number.isFinite(multiplier) || multiplier < 1)
              errors.push({ level: "error", path: `${p}:${key}.${grade}`, msg: "1 이상의 유한한 배율이 필요합니다." });
          }
        }
      }
    }
    for (const row of g._stageDropTables || []) {
      const p = `grades:${row._stage}스테이지`;
      if (row._stageType && !VOCAB.stageType.includes(row._stageType)) errors.push({ level: "error", path: `${p}:_stageType`, msg: `종류는 normal/boss: ${row._stageType}` });
      const chances = row._eliteModifierGradeChancesPercent;
      if (row._stageType === "normal" && chances) {
        const total = Object.values(chances).reduce((sum, value) => sum + (Number(value) || 0), 0);
        if (total !== 100) errors.push({ level: "warn", path: `${p}:_eliteModifierGradeChancesPercent`, msg: `확률 합계가 100이 아닙니다: ${total}` });
      }
      if (row._stageType === "boss" && !row._stageClearModifierRewardChoice)
        errors.push({ level: "error", path: `${p}:_stageClearModifierRewardChoice`, msg: "보스 스테이지에는 클리어 보상 선택이 필요합니다." });
    }
    return errors;
  },
  balance(table) {
    const errors = [];
    const walk = (obj, prefix) => {
      for (const [key, value] of Object.entries(obj)) {
        if (typeof value === "number" && !isFinite(value)) errors.push({ level: "error", path: `${table.id}:${prefix}${key}`, msg: "유한한 숫자여야 합니다." });
        if (value && typeof value === "object" && !Array.isArray(value)) walk(value, `${prefix}${key}.`);
      }
    };
    walk(state.data.balance, "");
    if (state.data.balance._sim && state.data.balance._sim._tickRate !== 60)
      errors.push({ level: "warn", path: "balance:_sim._tickRate", msg: "게임은 틱 레이트 60을 기대합니다." });
    if (state.data.balance._ram && !VOCAB.ramMode.includes(state.data.balance._ram._mode))
      errors.push({ level: "error", path: "balance:_ram._mode", msg: `RAM 모드는 shared/perSpell: ${state.data.balance._ram._mode}` });
    return errors;
  },
  strings(table) {
    const errors = [];
    const seen = new Set();
    (state.data.strings._entries || []).forEach((entry, index) => {
      if (!entry._key) errors.push({ level: "error", path: `strings:#${index + 1}:_key`, msg: "키가 비어 있습니다." });
      else if (seen.has(entry._key)) errors.push({ level: "error", path: `strings:${entry._key}:_key`, msg: `키가 중복됩니다: "${entry._key}"` });
      seen.add(entry._key);
    });
    return errors;
  },
};

// --- 저장 ---

async function saveCurrent() {
  const table = tableById(state.current);
  const errors = (state.errors[table.id] || []).filter(e => e.level === "error");
  if (errors.length && !confirm(`검증 오류 ${errors.length}건을 무시하고 저장할까요?\n게임 로드가 실패할 수 있습니다.`)) return;
  const body = JSON.stringify(state.data[table.id], null, 2);
  const response = await fetch("/api/data?id=" + encodeURIComponent(table.id), { method: "POST", body });
  if (!response.ok) {
    alert("저장 실패: " + await response.text());
    return;
  }
  state.saved[table.id] = body;
  state.dirty[table.id] = false;
  refreshHeader();
  renderSidebar();
  const summary = $("#error-summary");
  const flash = el("span", { class: "saved-flash", text: "저장됨" });
  summary.after(flash);
  setTimeout(() => flash.remove(), 1500);
}

async function reloadCurrent() {
  const table = tableById(state.current);
  if (state.dirty[table.id] && !confirm("저장하지 않은 변경을 버리고 다시 불러올까요?")) return;
  const response = await fetch("/api/data?id=" + encodeURIComponent(table.id));
  if (!response.ok) { alert("다시 불러오기 실패: " + await response.text()); return; }
  const raw = await response.text();
  state.saved[table.id] = raw;
  state.data[table.id] = JSON.parse(raw);
  state.dirty[table.id] = false;
  rebuildRefsets();
  validateAll();
  refreshHeader();
  renderSidebar();
  renderView();
}

function refreshGridCells() {
  // 그리드의 값 셀은 상세 폼 변경 후 다음 렌더링 때 갱신된다. 여기서는 선택 행 표시만 유지한다.
}

init().catch(error => {
  document.body.innerHTML = `<p style="padding:20px;color:#e5604c">초기화 실패: ${error.message}</p>`;
});

// IAW Scan 3 Coupé - browser side. Plain JavaScript, no dependencies: the phone reaches this page
// over the device's own Wi-Fi, with no internet to fetch anything else from.
//
// The device owns the ECU session; this page only mirrors it. Everything it shows comes from one
// Server-Sent Events stream (/api/events): a full "state" snapshot whenever something structural
// changes, plus lighter "values", "errors" and "sample" events for the live data. Buttons POST a
// command and wait for the next event to show its outcome.
'use strict';

const PALETTE = ['#3B5BDB', '#E8590C', '#2F9E44', '#E03531', '#942FE8', '#0C8E8E', '#C2255C', '#847A00'];
const CHART_WINDOW_S = 15;
const CHART_KEEP_S = 60;
const CONNECTED_VIEWS = ['dashboard', 'parameters', 'errors', 'tests', 'graph'];
const SIDE_VIEWS = ['logbook', 'info']; // reachable from the top bar, connected or not
const LOGBOOK_REFRESH_MS = 15000;
const SVG_NS = 'http://www.w3.org/2000/svg';

const app = {
  setup: null,      // SetupDto: ECU choices, serial ports, about text
  s: null,          // last StateDto from the device
  values: [],       // latest formatted value per parameter index (null when not polled)
  errors: [],
  view: 'connect',
  backView: 'connect',
  selectedEcu: null,
  linkUp: false,
  plotOff: new Set(), // checked parameters left out of the next recording
  chart: { key: '', live: false, series: [] },
  files: [],
  datalog: null,    // DatalogDto, fetched while the logbook is open
};

// ---- Small helpers ---------------------------------------------------------------------

const $ = (id) => document.getElementById(id);

function h(tag, props, ...children) {
  const el = document.createElement(tag);
  for (const [key, value] of Object.entries(props || {})) {
    if (value == null || value === false) continue;
    if (key === 'class') el.className = value;
    else if (key === 'text') el.textContent = value;
    else if (key === 'dataset') Object.assign(el.dataset, value);
    else if (key.startsWith('on')) el.addEventListener(key.slice(2), value);
    else el.setAttribute(key, value === true ? '' : value);
  }
  for (const child of children.flat()) {
    if (child == null || child === false) continue;
    el.append(child instanceof Node ? child : document.createTextNode(String(child)));
  }
  return el;
}

function icon(name, extraClass) {
  const svg = document.createElementNS(SVG_NS, 'svg');
  svg.setAttribute('class', 'ic' + (extraClass ? ' ' + extraClass : ''));
  const use = document.createElementNS(SVG_NS, 'use');
  use.setAttribute('href', '#i-' + name);
  svg.append(use);
  return svg;
}

let toastTimer = 0;
function toast(message, danger) {
  const el = $('toast');
  el.textContent = message;
  el.className = 'toast' + (danger ? ' danger' : '');
  el.hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => { el.hidden = true; }, 4000);
}

function confirmAction({ title, body, ok = 'Conferma', danger = false }) {
  const dialog = $('confirm');
  if (typeof dialog.showModal !== 'function') return Promise.resolve(window.confirm(title + '\n\n' + body));
  $('confirm-title').textContent = title;
  $('confirm-body').textContent = body;
  const okButton = $('confirm-ok');
  okButton.textContent = ok;
  okButton.className = 'btn ' + (danger ? 'danger' : 'primary');
  dialog.returnValue = '';
  return new Promise((resolve) => {
    dialog.addEventListener('close', () => resolve(dialog.returnValue === 'ok'), { once: true });
    dialog.showModal();
  });
}

/** Calls the device API. Returns the parsed body ({} for 204), or null after showing the error. */
async function api(method, path, body) {
  const headers = { 'X-IAW-Coupe': '1' };
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  let response;
  try {
    response = await fetch('/api' + path, {
      method,
      headers,
      body: body !== undefined ? JSON.stringify(body) : undefined,
      cache: 'no-store',
    });
  } catch {
    toast('Dispositivo non raggiungibile.', true);
    return null;
  }
  if (response.ok) {
    if (response.status === 204) return {};
    return response.json().catch(() => ({}));
  }
  let message = 'Errore ' + response.status;
  try {
    const json = await response.json();
    if (json && json.message) message = json.message;
  } catch { /* no JSON body */ }
  toast(message, true);
  return null;
}

const isConnected = () => !!app.s && (app.s.status === 'connected' || app.s.status === 'commLost');
const isSimulation = () => !!app.s?.connection?.isSimulation;

function formatSize(bytes) {
  if (bytes < 1024) return bytes + ' B';
  if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1).replace('.', ',') + ' kB';
  if (bytes < 1024 * 1024 * 1024) return (bytes / 1024 / 1024).toFixed(1).replace('.', ',') + ' MB';
  return (bytes / 1024 / 1024 / 1024).toFixed(1).replace('.', ',') + ' GB';
}

// ---- Navigation ------------------------------------------------------------------------

let logbookTimer = 0;

function show(view) {
  if (SIDE_VIEWS.includes(view) && !SIDE_VIEWS.includes(app.view)) app.backView = app.view;
  const changed = app.view !== view;
  app.view = view;
  document.body.dataset.view = view;
  for (const section of document.querySelectorAll('.view')) section.hidden = section.id !== 'view-' + view;
  for (const tab of document.querySelectorAll('.tab')) tab.classList.toggle('active', tab.dataset.view === view);
  if (changed) $('main').scrollTop = 0;
  // Keeps the screen in the address, so a bookmark (or a reload) reopens it.
  history.replaceState(null, '', view === 'connect' ? location.pathname : '#' + view);
  // What the device polls (live values or fault codes) follows the open screen.
  if (isConnected()) api('POST', '/screen', { screen: view });
  if (view === 'graph') {
    loadFiles();
    requestDraw();
  }
  clearInterval(logbookTimer);
  if (view === 'logbook') {
    loadDatalog();
    logbookTimer = setInterval(loadDatalog, LOGBOOK_REFRESH_MS);
  }
}

function goBack() {
  const target = app.backView;
  if (CONNECTED_VIEWS.includes(target) && !isConnected()) show('connect');
  else show(SIDE_VIEWS.includes(target) ? 'connect' : target);
}

// ---- Device events ---------------------------------------------------------------------

let events = null;

function openEvents() {
  if (events) events.close();
  events = new EventSource('/api/events');
  let clockSent = false;
  events.addEventListener('state', (e) => {
    setLink(true);
    if (!clockSent) {
      // The device has no battery-backed clock: every phone that (re)connects lends it the time.
      clockSent = true;
      api('POST', '/clock', { utcMs: Date.now(), timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone || null });
    }
    onState(JSON.parse(e.data));
  });
  events.addEventListener('values', (e) => {
    app.values = JSON.parse(e.data);
    updateValues();
  });
  events.addEventListener('errors', (e) => {
    app.errors = JSON.parse(e.data);
    if (app.s) app.s.errorsRead = true;
    renderErrors();
    renderTabbar();
  });
  events.addEventListener('sample', (e) => onSample(JSON.parse(e.data)));
  events.addEventListener('ping', () => setLink(true));
  events.onerror = () => {
    setLink(false);
    // The browser retries by itself unless the stream was closed for good.
    if (events.readyState === EventSource.CLOSED) setTimeout(openEvents, 3000);
  };
}

function setLink(up) {
  if (app.linkUp === up) return;
  app.linkUp = up;
  renderTop();
  renderBanners();
}

function onState(state) {
  const previous = app.s;
  app.s = state;
  app.values = state.parameters.map((p) => p.value);
  app.errors = state.errors;
  if (!app.setup) loadSetup();

  syncChart(state.recording);

  if (!previous && isConnected() && !SIDE_VIEWS.includes(app.view)) show(app.initialView); // page (re)loaded mid-session
  else if (isConnected() && !CONNECTED_VIEWS.includes(app.view) && !SIDE_VIEWS.includes(app.view)) show('dashboard');
  else if (!isConnected() && CONNECTED_VIEWS.includes(app.view)) show('connect');

  renderAll();
  if (previous?.recording.isRecording && !state.recording.isRecording) loadFiles();
  if (app.view === 'logbook' && previous && loggerChanged(previous.logger, state.logger)) loadDatalog();
}

function loggerChanged(a, b) {
  return a.active !== b.active || a.paused !== b.paused || a.autoEnabled !== b.autoEnabled || a.clockTrusted !== b.clockTrusted || a.dataFile !== b.dataFile;
}

// ---- Rendering -------------------------------------------------------------------------

function renderAll() {
  if (!app.s) return;
  renderTop();
  renderBanners();
  renderTabbar();
  renderConnect();
  renderDashboard();
  renderParameters();
  renderErrors();
  renderTests();
  renderGraph();
  renderInfo();
  renderLogbook();
}

function renderTop() {
  const status = $('top-status');
  let cls = '';
  let text = 'Non connesso';
  const s = app.s;
  if (!app.linkUp) { cls = 'bad'; text = 'Offline'; }
  else if (s?.status === 'connecting') { cls = 'busy'; text = 'Connessione'; }
  else if (s?.status === 'commLost') { cls = 'bad'; text = 'Persa'; }
  else if (s?.status === 'connected') {
    cls = isSimulation() ? 'sim' : s.logger.active ? 'rec' : 'ok';
    text = isSimulation() ? 'Simulazione' : s.logger.active ? 'Registra' : 'Connesso';
  }
  else if (s?.logger.searching) { cls = 'busy'; text = 'Cerca ECU'; }
  status.className = 'status ' + cls;
  status.querySelector('.status-text').textContent = text;

  const conn = isConnected() ? s.connection : null;
  $('top-title').textContent = conn && conn.isKnownCoupe ? conn.carModel : 'Fiat Coupé';
  $('top-sub').textContent = conn ? conn.ecuType : 'IAW Scan 3 · Edizione Coupé';
}

function renderBanners() {
  const s = app.s;
  $('banner-link').hidden = app.linkUp;
  const commLost = s?.status === 'commLost';
  $('banner-comm').hidden = !commLost;
  if (commLost) $('banner-comm-text').textContent = s.communicationLostMessage || '';

  const conn = isConnected() ? s.connection : null;
  const unknownIso = !!conn && !conn.isSimulation && !conn.isKnownCoupe;
  $('banner-iso').hidden = !unknownIso;
  if (unknownIso) {
    $('banner-iso').textContent = `Codice ISO ${conn.isoCode} non riconosciuto come Fiat Coupé: controlla di essere collegato alla presa giusta. I valori vengono comunque letti con le formule ${conn.ecuType}.`;
  }
  $('banner-sim').hidden = !(conn && conn.isSimulation);
}

function renderTabbar() {
  $('tabbar').hidden = !isConnected();
  const count = app.errors.length;
  const badge = $('errors-badge');
  badge.hidden = count === 0;
  badge.textContent = count;
  $('recording-dot').hidden = !app.s?.recording.isRecording;
}

function renderConnect() {
  const logger = app.s.logger;
  const searching = logger.searching;
  const connecting = app.s.status === 'connecting';

  // Automatic logging works on its own at every power-on; while it is looking for the ECU the
  // manual connection waits (or the user pauses it).
  $('auto-card').hidden = !logger.autoEnabled;
  $('auto-status').textContent = logger.status;
  $('auto-spinner').hidden = !searching;
  $('btn-auto-pause').hidden = !logger.autoEnabled || logger.paused || isConnected();
  $('btn-auto-resume').hidden = !logger.paused;

  $('connecting').hidden = !connecting || searching;
  $('btn-connect').hidden = connecting && !searching;
  for (const el of document.querySelectorAll('#view-connect .choice, #port, #btn-ports, #simulation')) el.disabled = connecting;
  if (!connecting) $('port').disabled = $('simulation').checked;
  const error = $('connect-error');
  error.hidden = !app.s.errorMessage;
  error.textContent = app.s.errorMessage || '';
  updateConnectButton();
}

function renderEcuChoices() {
  $('ecu-choices').replaceChildren(...app.setup.ecus.map((ecu) => h('button', {
    class: 'choice',
    type: 'button',
    role: 'radio',
    'aria-checked': String(ecu.id === app.selectedEcu),
    onclick: () => { app.selectedEcu = ecu.id; renderEcuChoices(); updateConnectButton(); },
  },
  h('div', { class: 'badge', text: ecu.badge }),
  h('div', { class: 'choice-role', text: ecu.role }),
  h('div', { class: 'choice-name', text: ecu.name }),
  h('div', { class: 'chips' }, ecu.vehicles.map((v) => h('span', { class: 'chip', text: v }))))));
}

function renderPorts(ports, preferred) {
  const select = $('port');
  const current = select.value || preferred;
  select.replaceChildren(...ports.map((p) => h('option', { value: p, text: p })));
  if (ports.length === 0) select.append(h('option', { value: '', text: 'Nessun cavo trovato' }));
  if (current && ports.includes(current)) select.value = current;
  $('port-hint').textContent = ports.length
    ? ''
    : 'Collega il cavo KKL al dispositivo e tocca il pulsante di aggiornamento. Senza cavo puoi usare la modalità simulazione.';
  updateConnectButton();
}

function updateConnectButton() {
  const simulation = $('simulation').checked;
  const searching = !!app.s?.logger.searching;
  $('btn-connect').disabled = !app.selectedEcu || (!simulation && !$('port').value) || !app.linkUp || searching;
}

function renderDashboard() {
  const checked = app.s.parameters.filter((p) => p.checked);
  $('dash-empty').hidden = checked.length > 0;
  const box = $('dash-tiles');
  const key = (app.s.connection?.ecuId || '') + '|' + checked.map((p) => p.index).join(',');
  if (box.dataset.key !== key) {
    box.dataset.key = key;
    box.replaceChildren(...checked.map((p) => h('div', { class: 'gauge' },
      h('div', { class: 'gauge-label', text: p.description }),
      h('div', { class: 'gauge-value', dataset: { index: p.index, empty: '–' } },
        h('span', { class: 'v' }),
        p.unit ? h('span', { class: 'gauge-unit', text: p.unit }) : null))));
  }
  updateValues();
}

function renderParameters() {
  const params = app.s.parameters;
  const logging = app.s.logger.active;
  $('params-hint').textContent = logging
    ? 'La registrazione automatica legge sempre tutti i parametri: qui scegli quali mostrare nel cruscotto.'
    : 'Tocca un parametro per mostrarlo nel cruscotto. Meno parametri scegli, più spesso vengono aggiornati.';
  const box = $('param-list');
  const key = (app.s.connection?.ecuId || '') + '|' + logging + '|' + params.map((p) => (p.checked ? 1 : 0)).join('');
  if (box.dataset.key !== key) {
    box.dataset.key = key;
    box.replaceChildren(...params.map((p) => h('button', {
      class: 'param' + (p.checked ? ' on' : ''),
      type: 'button',
      'aria-pressed': String(p.checked),
      onclick: (e) => toggleParameter(p, e.currentTarget),
    },
    h('span', { class: 'param-name', text: p.description }),
    h('span', { class: 'param-check' }, icon('check')),
    h('span', { class: 'param-value', dataset: { index: p.index, empty: '' } },
      h('span', { class: 'v' }),
      (p.checked || logging) && p.unit ? h('small', { text: p.unit }) : null))));
  }
  const recording = app.s.recording.isRecording;
  $('btn-uncheck-all').disabled = recording || !params.some((p) => p.checked);
  $('btn-check-all').disabled = params.every((p) => p.checked);
  updateValues();
}

async function toggleParameter(p, button) {
  button.classList.toggle('on'); // instant feedback; the next state event confirms it
  const ok = await api('POST', '/parameters/' + p.index, { checked: !p.checked });
  if (!ok) {
    $('param-list').dataset.key = '';
    renderParameters();
  }
}

/** Writes the latest values into every tile currently on screen, without rebuilding them. */
function updateValues() {
  for (const el of document.querySelectorAll('[data-index]')) {
    const value = app.values[Number(el.dataset.index)];
    const text = value ?? el.dataset.empty;
    const span = el.firstElementChild;
    if (span.textContent !== text) span.textContent = text;
    el.classList.toggle('err', value === '#ERR');
  }
}

function renderErrors() {
  if (!app.s) return;
  const list = app.errors;
  const read = app.s.errorsRead;
  $('error-list').replaceChildren(...(read ? list : []).map((e) => h('div', { class: 'error-row' + (e.milOn ? ' mil' : '') },
    h('div', { class: 'error-desc', text: e.description }),
    h('span', { class: 'pill' + (e.milOn ? ' on' : ''), text: 'MIL' }),
    e.reason ? h('div', { class: 'error-reason', text: e.reason }) : null,
    h('div', { class: 'error-state', text: e.state }))));
  $('errors-empty').hidden = !read || list.length > 0;

  const hint = $('errors-hint');
  if (!read) hint.textContent = 'Lettura dei codici di errore in corso…';
  else if (isSimulation()) hint.textContent = 'In simulazione i codici sono di prova e l\'azzeramento non è disponibile.';
  else hint.textContent = '';

  const clear = $('btn-clear-codes');
  clear.disabled = !app.s.canClearCodes;
  clear.textContent = app.s.isClearing ? 'Azzeramento…' : 'Azzera codici';

  const status = $('errors-status');
  status.hidden = !app.s.errorsStatus;
  status.textContent = app.s.errorsStatus || '';
}

function resultClass(result) {
  if (result === 'Superato') return 'ok';
  if (result === 'In corso...') return 'run';
  if (result === 'DEMO') return '';
  return 'bad';
}

function renderTests() {
  const s = app.s;
  $('tests-empty').hidden = s.tests.length > 0 || !isConnected();
  const busy = s.runningTest != null || s.isClearing || s.status !== 'connected';
  $('test-list').replaceChildren(...s.tests.map((t) => {
    const running = s.runningTest === t.index;
    return h('div', { class: 'test-row' + (running ? ' running' : '') },
      h('div', { class: 'test-main' },
        h('div', { class: 'test-name', text: t.description }),
        h('div', { class: 'test-meta' },
          h('span', { text: `max ${t.timeoutSeconds} s` }),
          t.result ? h('span', { class: 'result ' + resultClass(t.result), text: t.result }) : null)),
      running
        ? h('button', { class: 'btn small ghost', type: 'button', onclick: () => api('POST', '/tests/cancel') },
          h('span', { class: 'spinner small' }), 'Annulla')
        : h('button', { class: 'btn small primary', type: 'button', disabled: busy, onclick: () => runTest(t) }, 'Esegui'));
  }));
}

async function runTest(test) {
  if (!isSimulation()) {
    const ok = await confirmAction({
      title: test.description,
      body: `Il componente verrà azionato davvero, per al massimo ${test.timeoutSeconds} secondi. Quadro acceso, motore spento.`,
      ok: 'Esegui test',
    });
    if (!ok) return;
  }
  api('POST', `/tests/${test.index}/run`);
}

// ---- Graph -----------------------------------------------------------------------------

function plannedTraces() {
  return app.s.parameters.filter((p) => p.checked && !app.plotOff.has(p.index));
}

function renderGraph() {
  const s = app.s;
  const recording = s.recording.isRecording;
  const checked = s.parameters.filter((p) => p.checked);

  const record = $('btn-record');
  record.classList.toggle('recording', recording);
  record.replaceChildren(icon(recording ? 'stop' : 'record'), h('span', { text: recording ? 'Interrompi' : 'Registra' }));
  record.disabled = !recording && (s.status !== 'connected' || plannedTraces().length === 0);

  const chips = $('trace-chips');
  if (recording) {
    chips.replaceChildren(...s.recording.indices.map((index, i) => traceChip(s.parameters[index], PALETTE[i % PALETTE.length], false, true)));
  } else {
    let colorIndex = 0;
    chips.replaceChildren(...checked.map((p) => {
      const off = app.plotOff.has(p.index);
      return traceChip(p, off ? null : PALETTE[colorIndex++ % PALETTE.length], off, false);
    }));
  }

  const hint = $('trace-hint');
  if (recording) hint.textContent = s.recording.fileName ? `Registrazione in corso su ${s.recording.fileName}` : 'Registrazione in corso (file non salvato).';
  else if (checked.length === 0) hint.textContent = 'Scegli prima i parametri nella scheda Parametri.';
  else hint.textContent = 'Tocca un tracciato per escluderlo. Ogni registrazione viene salvata anche come file CSV.';

  renderFiles();
  requestDraw();
}

function traceChip(p, color, off, locked) {
  return h('button', {
    class: 'trace' + (off ? ' off' : ''),
    type: 'button',
    disabled: locked,
    onclick: () => {
      if (app.plotOff.has(p.index)) app.plotOff.delete(p.index);
      else app.plotOff.add(p.index);
      renderGraph();
    },
  }, h('span', { class: 'swatch', style: color ? `background:${color}` : null }), p.description);
}

async function toggleRecording() {
  if (app.s.recording.isRecording) {
    api('POST', '/recording/stop');
    return;
  }
  const result = await api('POST', '/recording/start', { indices: plannedTraces().map((p) => p.index) });
  if (result && result.message) toast(result.message, true);
}

function syncChart(recording) {
  if (!recording.isRecording) {
    app.chart.live = false;
    return;
  }
  const key = recording.indices.join(',');
  if (app.chart.live && app.chart.key === key) return;
  app.chart = {
    key,
    live: true,
    series: recording.indices.map((index, i) => ({
      name: app.s.parameters[index]?.description || '#' + index,
      color: PALETTE[i % PALETTE.length],
      points: [],
    })),
  };
}

function onSample(sample) {
  if (!app.chart.live) return;
  app.chart.series.forEach((series, i) => {
    series.points.push([sample.t, sample.v[i]]);
    while (series.points.length && series.points[0][0] < sample.t - CHART_KEEP_S) series.points.shift();
  });
  requestDraw();
}

let drawPending = false;
function requestDraw() {
  if (drawPending) return;
  drawPending = true;
  requestAnimationFrame(() => {
    drawPending = false;
    drawChart();
  });
}

/** Same layout as the desktop LiveLineChart: shared Y axis, 15 s rolling window, legend top-left. */
function drawChart() {
  if (app.view !== 'graph') return;
  const canvas = $('chart');
  const dpr = window.devicePixelRatio || 1;
  const width = canvas.clientWidth;
  const height = canvas.clientHeight;
  if (canvas.width !== Math.round(width * dpr) || canvas.height !== Math.round(height * dpr)) {
    canvas.width = Math.round(width * dpr);
    canvas.height = Math.round(height * dpr);
  }
  const ctx = canvas.getContext('2d');
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  ctx.clearRect(0, 0, width, height);

  const css = getComputedStyle(document.documentElement);
  const textColor = css.getPropertyValue('--text-2').trim();
  const gridColor = css.getPropertyValue('--border').trim();
  const font = css.getPropertyValue('--font').trim();

  const placeholder = (message) => {
    ctx.fillStyle = textColor;
    ctx.font = `14px ${font}`;
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.fillText(message, width / 2, height / 2);
  };

  const series = app.chart.series;
  if (!series.length) {
    placeholder(app.s && app.s.parameters.some((p) => p.checked) ? 'Tocca Registra per avviare il grafico.' : 'Nessun tracciato selezionato.');
    return;
  }

  let maxX = 0;
  let any = false;
  for (const s of series) for (const [x] of s.points) { if (x > maxX) maxX = x; any = true; }
  if (!any) { placeholder('In attesa di dati...'); return; }

  const left = 48, right = 10, top = 10, bottom = 22;
  const plot = { x: left, y: top, w: Math.max(0, width - left - right), h: Math.max(0, height - top - bottom) };
  const minX = Math.max(0, maxX - CHART_WINDOW_S);

  let minY = Infinity, maxY = -Infinity;
  for (const s of series) {
    for (const [x, y] of s.points) {
      if (x < minX) continue;
      if (y < minY) minY = y;
      if (y > maxY) maxY = y;
    }
  }
  if (minY > maxY) { minY = 0; maxY = 1; }
  if (Math.abs(maxY - minY) < 0.0001) { minY -= 1; maxY += 1; }
  const pad = (maxY - minY) * 0.08;
  minY -= pad;
  maxY += pad;

  const px = (x) => plot.x + ((x - minX) / CHART_WINDOW_S) * plot.w;
  const py = (y) => plot.y + plot.h - ((y - minY) / (maxY - minY)) * plot.h;

  ctx.font = `11px ${font}`;
  ctx.lineWidth = 1;
  ctx.strokeStyle = gridColor;
  ctx.fillStyle = textColor;
  ctx.textAlign = 'right';
  ctx.textBaseline = 'middle';
  for (let i = 0; i <= 4; i++) {
    const t = i / 4;
    const y = plot.y + plot.h - t * plot.h;
    ctx.beginPath();
    ctx.moveTo(plot.x, y);
    ctx.lineTo(plot.x + plot.w, y);
    ctx.stroke();
    const value = minY + t * (maxY - minY);
    ctx.fillText(String(Number(value.toFixed(2))).replace('.', ','), plot.x - 6, y);
  }
  ctx.textAlign = 'center';
  ctx.textBaseline = 'top';
  for (let i = 0; i <= 3; i++) {
    const t = i / 3;
    ctx.fillText(Math.round(minX + t * CHART_WINDOW_S) + 's', plot.x + t * plot.w, plot.y + plot.h + 6);
  }

  ctx.save();
  ctx.beginPath();
  ctx.rect(plot.x, plot.y, plot.w, plot.h);
  ctx.clip();
  ctx.lineWidth = 2;
  ctx.lineJoin = 'round';
  for (const s of series) {
    if (s.points.length < 2) continue;
    ctx.strokeStyle = s.color;
    ctx.beginPath();
    let started = false;
    for (const [x, y] of s.points) {
      if (x < minX - 1) continue;
      if (!started) { ctx.moveTo(px(x), py(y)); started = true; }
      else ctx.lineTo(px(x), py(y));
    }
    ctx.stroke();
  }
  ctx.restore();

  ctx.textAlign = 'left';
  ctx.textBaseline = 'middle';
  let ly = plot.y + 10;
  for (const s of series) {
    ctx.fillStyle = s.color;
    ctx.fillRect(plot.x + 6, ly - 5, 10, 10);
    ctx.fillStyle = textColor;
    ctx.fillText(s.name, plot.x + 22, ly);
    ly += 17;
  }
}

async function loadFiles() {
  const files = await api('GET', '/recordings');
  if (!files) return;
  app.files = files;
  renderFiles();
}

function renderFiles() {
  const box = $('file-list');
  if (app.files.length === 0) {
    box.replaceChildren(h('p', { class: 'hint', text: 'Nessuna registrazione salvata.' }));
    return;
  }
  const active = app.s?.recording.isRecording ? app.s.recording.fileName : null;
  box.replaceChildren(...app.files.map((f) => h('div', { class: 'file-row' },
    h('div', { class: 'file-main' },
      h('div', { class: 'file-name', text: f.name }),
      h('div', { class: 'file-meta', text: f.name === active ? 'In registrazione…' : `${f.modified} · ${formatSize(f.size)}` })),
    f.name === active ? null : h('a', {
      class: 'icon-btn', href: '/api/recordings/' + encodeURIComponent(f.name), download: f.name, 'aria-label': 'Scarica ' + f.name,
    }, icon('download')),
    f.name === active ? null : h('button', {
      class: 'icon-btn', type: 'button', 'aria-label': 'Elimina ' + f.name,
      onclick: () => deleteFile(f.name),
    }, icon('trash')))));
}

async function deleteFile(name) {
  const ok = await confirmAction({ title: 'Eliminare la registrazione?', body: name, ok: 'Elimina', danger: true });
  if (!ok) return;
  if (await api('DELETE', '/recordings/' + encodeURIComponent(name))) loadFiles();
}

// ---- Logbook ---------------------------------------------------------------------------

function formatDuration(seconds) {
  const total = Math.max(0, Math.round(seconds));
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  if (h > 0) return `${h} h ${m} min`;
  if (m > 0) return `${m} min`;
  return `${total} s`;
}

function formatWhen(utc) {
  if (!utc) return null;
  return new Date(utc).toLocaleString('it-IT', { weekday: 'short', day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' });
}

function renderLogbook() {
  const logger = app.s.logger;
  $('autolog').checked = logger.autoEnabled;
  const status = $('log-status');
  status.textContent = logger.status;
  status.classList.toggle('on', logger.active);
  $('btn-log-pause').hidden = !logger.autoEnabled || logger.paused || isConnected();
  $('btn-log-resume').hidden = !logger.paused;

  const clock = !logger.clockTrusted
    ? 'Non ancora sincronizzato: gli orari verranno ricostruiti dai secondi trascorsi dall\'accensione.'
    : logger.clockSource === 'ntp' ? 'Sincronizzato via internet' : 'Sincronizzato con il telefono';
  const rows = [
    ['Accensione in corso', 'n. ' + logger.bootSeq],
    ['Orologio', clock],
  ];
  if (logger.dataFile) rows.push(['File dati', logger.dataFile]);
  if (app.datalog) {
    rows.push(['Spazio usato', `${formatSize(app.datalog.usedBytes)} di ${formatSize(app.datalog.maxBytes)}`]);
    rows.push(['Campionamento', `fino a ${String(app.datalog.samplesPerSecond).replace('.', ',')} righe al secondo`]);
  }
  $('log-kv').replaceChildren(...rows.flatMap(([k, v]) => [h('dt', { text: k }), h('dd', { text: v })]));
  renderBoots();
}

function renderBoots() {
  const box = $('boot-list');
  const boots = app.datalog?.boots;
  if (!boots) {
    box.replaceChildren(h('p', { class: 'hint', text: 'Caricamento…' }));
    return;
  }
  if (boots.length === 0) {
    box.replaceChildren(h('p', { class: 'hint', text: 'Ancora nessuna accensione registrata.' }));
    return;
  }
  box.replaceChildren(...boots.map((b) => {
    const meta = [
      formatWhen(b.startUtc) || 'Orario sconosciuto (nessun telefono collegato)',
      formatDuration(b.durationSeconds),
      formatSize(b.sizeBytes),
    ];
    const details = [];
    if (b.vehicles.length) details.push(b.vehicles.join(', '));
    details.push(b.ecuSessions === 1 ? '1 collegamento alla centralina' : `${b.ecuSessions} collegamenti alla centralina`);
    return h('div', { class: 'boot-row' },
      h('div', { class: 'boot-main' },
        h('div', { class: 'boot-title' }, 'Accensione n. ' + b.seq, b.current ? h('span', { class: 'chip live', text: 'in corso' }) : null),
        h('div', { class: 'boot-meta', text: meta.join(' · ') }),
        h('div', { class: 'boot-meta', text: details.join(' · ') }),
        b.errors.length ? h('div', { class: 'boot-errors', text: 'Errori: ' + b.errors.join('; ') }) : null),
      h('a', {
        class: 'icon-btn', href: '/api/datalog/export?boot=' + b.seq, download: '', 'aria-label': 'Scarica accensione ' + b.seq,
      }, icon('download')),
      b.current ? null : h('button', {
        class: 'icon-btn', type: 'button', 'aria-label': 'Elimina accensione ' + b.seq,
        onclick: () => deleteBoot(b.seq),
      }, icon('trash')));
  }));
}

async function loadDatalog() {
  const datalog = await api('GET', '/datalog');
  if (!datalog) return;
  app.datalog = datalog;
  if (app.s) renderLogbook();
}

async function deleteBoot(seq) {
  const ok = await confirmAction({ title: `Eliminare l'accensione n. ${seq}?`, body: 'Eventi e dati di questa accensione verranno cancellati dal dispositivo.', ok: 'Elimina', danger: true });
  if (!ok) return;
  if (await api('DELETE', '/datalog/' + seq)) loadDatalog();
}

// ---- Info ------------------------------------------------------------------------------

function renderInfo() {
  const conn = isConnected() ? app.s.connection : null;
  $('info-connection').hidden = !conn;
  if (conn) {
    const rows = [
      ['Centralina', conn.ecuType],
      ['Veicolo', conn.carModel],
      ['Codice ISO', conn.isoCode],
      ['Codice ricambio', conn.repCode],
      ['Collegamento', conn.isSimulation ? 'Simulazione' : 'Cavo K-line'],
    ];
    $('info-kv').replaceChildren(...rows.flatMap(([k, v]) => [h('dt', { text: k }), h('dd', { text: v })]));
  }
}

function renderAbout() {
  const about = app.setup.about;
  $('about-product').textContent = about.productName;
  $('about-version').textContent = about.version;
  $('about-description').textContent = about.description;
  $('about-fork-copyright').textContent = about.forkCopyright;
  $('about-fork-url').textContent = about.forkRepoUrl;
  $('about-fork-url').href = about.forkRepoUrl;
  $('about-original').textContent = about.originalProjectLine;
  $('about-original-url').textContent = about.originalProjectUrl;
  $('about-original-url').href = about.originalProjectUrl;
  $('about-license').textContent = about.licenseText;
}

async function loadSetup() {
  if (app.setupLoading) return;
  app.setupLoading = true;
  const setup = await api('GET', '/setup');
  app.setupLoading = false;
  if (!setup) return;
  app.setup = setup;
  const known = setup.ecus.some((e) => e.id === setup.lastEcu);
  app.selectedEcu = known ? setup.lastEcu : setup.ecus[0]?.id;
  renderEcuChoices();
  renderPorts(setup.ports, setup.lastPort);
  renderAbout();
}

// ---- Wiring ----------------------------------------------------------------------------

function wire() {
  for (const tab of document.querySelectorAll('.tab')) tab.addEventListener('click', () => show(tab.dataset.view));

  document.addEventListener('click', (e) => {
    const target = e.target.closest('[data-goto]');
    if (!target) return;
    if (target.dataset.goto === 'back') goBack();
    else show(target.dataset.goto);
  });

  $('btn-info').addEventListener('click', () => (app.view === 'info' ? goBack() : show('info')));
  $('btn-logbook').addEventListener('click', () => (app.view === 'logbook' ? goBack() : show('logbook')));

  $('autolog').addEventListener('change', (e) => api('POST', '/datalog/auto', { enabled: e.target.checked }));
  for (const id of ['btn-auto-pause', 'btn-log-pause']) $(id).addEventListener('click', () => api('POST', '/datalog/pause'));
  for (const id of ['btn-auto-resume', 'btn-log-resume']) $(id).addEventListener('click', () => api('POST', '/datalog/resume'));

  if (document.fullscreenEnabled) {
    const button = $('btn-fullscreen');
    button.hidden = false;
    button.addEventListener('click', () => {
      if (document.fullscreenElement) document.exitFullscreen();
      else document.documentElement.requestFullscreen({ navigationUI: 'hide' }).catch(() => {});
    });
  }

  $('simulation').addEventListener('change', () => {
    $('port').disabled = $('simulation').checked;
    updateConnectButton();
  });
  $('port').addEventListener('change', updateConnectButton);
  $('btn-ports').addEventListener('click', async () => {
    const ports = await api('GET', '/ports');
    if (ports) renderPorts(ports, null);
  });

  $('btn-connect').addEventListener('click', () => {
    const simulation = $('simulation').checked;
    api('POST', '/connect', { ecu: app.selectedEcu, port: $('port').value || null, simulation });
  });
  $('btn-cancel-connect').addEventListener('click', () => api('POST', '/connect/cancel'));

  $('btn-reconnect').addEventListener('click', () => api('POST', '/reconnect'));
  $('btn-comm-disconnect').addEventListener('click', () => api('POST', '/disconnect'));

  $('btn-check-all').addEventListener('click', () => api('POST', '/parameters', { checked: true }));
  $('btn-uncheck-all').addEventListener('click', () => api('POST', '/parameters', { checked: false }));

  $('btn-clear-codes').addEventListener('click', async () => {
    const ok = await confirmAction({
      title: 'Azzerare i codici di errore?',
      body: 'I codici memorizzati nella centralina verranno cancellati. Se ti servono, annotali prima.',
      ok: 'Azzera',
      danger: true,
    });
    if (ok) api('POST', '/errors/clear');
  });

  $('btn-record').addEventListener('click', toggleRecording);

  $('btn-disconnect').addEventListener('click', async () => {
    const logger = app.s?.logger;
    if (app.s?.recording.isRecording || logger?.active) {
      const ok = await confirmAction({
        title: 'Disconnettere?',
        body: logger?.active
          ? 'La registrazione automatica andrà in pausa fino alla prossima accensione (o finché non tocchi Riprendi nel Diario).'
          : 'La registrazione in corso verrà interrotta e salvata.',
        ok: 'Disconnetti',
      });
      if (!ok) return;
    }
    api('POST', '/disconnect');
  });

  window.addEventListener('resize', requestDraw);
  document.addEventListener('visibilitychange', () => {
    if (!document.hidden && (!events || events.readyState === EventSource.CLOSED)) openEvents();
  });
}

const requested = location.hash.slice(1);
app.initialView = CONNECTED_VIEWS.includes(requested) ? requested : 'dashboard';

wire();
show(SIDE_VIEWS.includes(requested) ? requested : 'connect');
loadSetup();
openEvents();

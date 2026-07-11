'use strict';

// ---------------- helpers ----------------
const $ = (id) => document.getElementById(id);
const $$ = (sel) => Array.from(document.querySelectorAll(sel));

async function apiGet(url) {
  const r = await fetch(url);
  return r.json();
}
async function apiPost(url, body) {
  const r = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body)
  });
  const data = await r.json().catch(() => ({}));
  return { ok: r.ok, status: r.status, data };
}

const state = { columns: ['Name', 'Email'], count: 0, provider: 'Smtp', sourceMode: false, sendJob: null, poll: null };

// ---------------- theme ----------------
// The head bootstrap already applied the stored theme (default dark); here we keep the toggle
// button label in sync and persist the user's choice to localStorage.
function currentTheme() { return document.documentElement.getAttribute('data-theme') || 'dark'; }
function applyTheme(theme) {
  document.documentElement.setAttribute('data-theme', theme);
  localStorage.setItem('emailblaster-theme', theme);
  const btn = $('themeToggle');
  if (btn) btn.textContent = theme === 'dark' ? '🌙 Dark' : '☀ Light';
}
$('themeToggle').addEventListener('click', () => applyTheme(currentTheme() === 'dark' ? 'light' : 'dark'));
applyTheme(currentTheme());

// ---------------- tab navigation ----------------
$$('.nav-item').forEach((btn) => {
  btn.addEventListener('click', () => activateTab(btn.dataset.tab));
});
function activateTab(tab) {
  $$('.nav-item').forEach((b) => b.classList.toggle('active', b.dataset.tab === tab));
  $$('.panel').forEach((p) => p.classList.toggle('active', p.id === 'tab-' + tab));
  if (tab === 'preview') { saveTemplate().then(loadPreviewList); }
  if (tab === 'send') refreshSendSummary();
  if (tab === 'compose') buildComposeChips();
}

// ---------------- footer ----------------
function refreshFooter() {
  $('statusProvider').textContent = 'Provider: ' + (state.provider === 'Aws' ? 'AWS SES' : 'SMTP');
  $('statusRecipients').textContent = state.count.toLocaleString() + ' recipients loaded';
}

// =====================================================================
// Configuration
// =====================================================================
async function loadConfig() {
  const { config } = await apiGet('/api/config');
  $('fromName').value = config.fromName || '';
  $('fromEmail').value = config.fromEmail || '';
  $('replyTo').value = config.replyToEmail || '';
  const unlimited = config.sendRatePerSecond <= 0;
  $('unlimited').checked = unlimited;
  $('rate').value = unlimited ? '' : config.sendRatePerSecond;
  $('rate').disabled = unlimited;
  setRadio('provider', config.provider);
  state.provider = config.provider;

  $('smtpHost').value = config.smtp.host || '';
  $('smtpPort').value = config.smtp.port || 587;
  $('smtpSecurity').value = config.smtp.security || 'StartTls';
  $('smtpUser').value = config.smtp.username || '';
  $('smtpPass').placeholder = config.smtp.hasPassword ? '•••••• (stored — leave blank to keep)' : '';

  $('awsRegion').value = config.aws.region || 'us-east-1';
  $('awsConfigSet').value = config.aws.configurationSetName || '';
  setRadio('awsAuth', config.aws.authMode);
  $('awsProfile').value = config.aws.profile || '';
  $('awsAccessKey').value = config.aws.accessKeyId || '';
  $('awsSecret').placeholder = config.aws.hasSecret ? '•••••• (stored — leave blank to keep)' : '';

  toggleProvider();
  toggleAwsAuth();
  refreshFooter();
  loadSesIdentities();
}

async function loadAwsProfiles() {
  try {
    const { profiles } = await apiGet('/api/aws-profiles');
    const list = $('awsProfileList');
    list.innerHTML = '';
    for (const name of profiles || []) {
      const opt = document.createElement('option');
      opt.value = name;
      list.appendChild(opt);
    }
  } catch {
    // Auto-completion is best-effort; the field still accepts free text.
  }
}

function collectConfig() {
  return {
    sendRatePerSecond: $('unlimited').checked ? 0 : parseFloat($('rate').value || '0'),
    provider: getRadio('provider'),
    fromName: $('fromName').value,
    fromEmail: $('fromEmail').value,
    replyToEmail: $('replyTo').value,
    smtp: {
      host: $('smtpHost').value,
      port: parseInt($('smtpPort').value || '587', 10),
      security: $('smtpSecurity').value,
      username: $('smtpUser').value,
      password: $('smtpPass').value
    },
    aws: {
      region: $('awsRegion').value,
      authMode: getRadio('awsAuth'),
      profile: $('awsProfile').value,
      accessKeyId: $('awsAccessKey').value,
      secretAccessKey: $('awsSecret').value,
      sessionToken: $('awsSessionToken').value,
      configurationSetName: $('awsConfigSet').value
    }
  };
}

$('btnSaveConfig').addEventListener('click', async () => {
  const { data } = await apiPost('/api/config', collectConfig());
  if (data.ok) {
    state.provider = getRadio('provider');
    refreshFooter();
    setConfigStatus(true, 'Settings saved to ' + data.savedTo);
    $('smtpPass').value = ''; $('awsSecret').value = '';
    loadConfig();
  } else {
    setConfigStatus(false, 'Please fix these settings first:\n• ' + (data.errors || ['Unknown error']).join('\n• '));
  }
});

$('btnDownloadConfig').addEventListener('click', async () => {
  // Capture on-screen edits into the session first so the download reflects what the user sees.
  await apiPost('/api/config', collectConfig());
  window.location.href = '/api/config/export';
});

$('configFileInput').addEventListener('change', async (e) => {
  const file = e.target.files[0];
  if (!file) return;
  const form = new FormData();
  form.append('file', file);
  const r = await fetch('/api/config/import', { method: 'POST', body: form });
  const data = await r.json().catch(() => ({}));
  e.target.value = '';
  if (!r.ok) { setConfigStatus(false, data.error || 'Could not load the configuration file.'); return; }
  await loadConfig();
  setConfigStatus(true, 'Configuration loaded from ' + file.name + '. Click "Save settings" to persist it.');
});

$('btnTestConnection').addEventListener('click', async (e) => {
  const btn = e.target; busy(btn, 'Testing…');
  await apiPost('/api/config', collectConfig());
  const { data } = await apiPost('/api/test-connection');
  unbusy(btn, 'Test connection');
  if (data.ok) setConfigStatus(true, 'Connection succeeded. Credentials and transport look good.');
  else setConfigStatus(false, 'Connection failed: ' + data.error);
});

// ---------------- verified SES identities ----------------
// When the AWS provider is active, the From email offers verified identities as suggestions
// (emails directly, domains as suffix completions of whatever local part is typed) and shows a
// live verified/not-verified hint — or the reason the identities could not be listed.
let sesIdentities = null;   // { emails: [], domains: [] } when loaded, null otherwise
let sesFetchSeq = 0;

function setFromEmailHint(text, color) {
  const el = $('fromEmailHint');
  if (!text) { el.classList.add('hidden'); return; }
  el.textContent = text;
  el.style.color = color;
  el.classList.remove('hidden');
}

function validateFromEmail() {
  if (!sesIdentities || getRadio('provider') !== 'Aws') return;
  const address = $('fromEmail').value.trim();
  if (!address || address.indexOf('@') <= 0 || address.endsWith('@')) {
    setFromEmailHint('Pick a verified identity from the suggestions, or type an address to validate it.', 'var(--muted)');
    return;
  }
  const lower = address.toLowerCase();
  const domain = lower.slice(lower.lastIndexOf('@') + 1);
  if (sesIdentities.emails.some((e) => e.toLowerCase() === lower)) {
    setFromEmailHint('✓ Verified SES email identity.', 'var(--success)');
  } else if (sesIdentities.domains.some((d) => d.toLowerCase() === domain)) {
    setFromEmailHint('✓ The domain is a verified SES identity.', 'var(--success)');
  } else {
    setFromEmailHint('Not a verified SES identity — SES will reject sends from this address.', 'var(--danger)');
  }
}

function rebuildFromEmailSuggestions() {
  const list = $('fromEmailList');
  list.innerHTML = '';
  if (!sesIdentities || getRadio('provider') !== 'Aws') return;
  const options = [...sesIdentities.emails];
  const typed = $('fromEmail').value.trim();
  const local = typed.includes('@') ? typed.slice(0, typed.indexOf('@')) : typed;
  for (const domain of sesIdentities.domains)
    options.push((local || 'you') + '@' + domain);
  for (const value of options) {
    const opt = document.createElement('option');
    opt.value = value;
    list.appendChild(opt);
  }
}

async function loadSesIdentities() {
  if (getRadio('provider') !== 'Aws') { sesIdentities = null; setFromEmailHint(null); rebuildFromEmailSuggestions(); return; }
  const seq = ++sesFetchSeq;
  setFromEmailHint('Looking up verified SES identities…', 'var(--muted)');
  await apiPost('/api/config', collectConfig());
  const data = await apiGet('/api/ses-identities');
  if (seq !== sesFetchSeq || getRadio('provider') !== 'Aws') return;
  if (!data.ok) {
    sesIdentities = null;
    rebuildFromEmailSuggestions();
    setFromEmailHint(data.message || 'Could not list the verified SES identities.', 'var(--danger)');
    return;
  }
  sesIdentities = { emails: data.emails || [], domains: data.domains || [] };
  rebuildFromEmailSuggestions();
  if (!sesIdentities.emails.length && !sesIdentities.domains.length) {
    setFromEmailHint('This AWS account has no verified SES identities in the selected region. Verify an email address or domain in the SES console first.', 'var(--danger)');
  } else {
    validateFromEmail();
  }
}

$('fromEmail').addEventListener('input', () => { rebuildFromEmailSuggestions(); validateFromEmail(); });
['awsProfile', 'awsRegion', 'awsAccessKey', 'awsSecret', 'awsSessionToken'].forEach((id) =>
  $(id).addEventListener('change', () => { if (getRadio('provider') === 'Aws') loadSesIdentities(); }));

let ssoAttempt = 0;

function finishAwsAccessTest(btn, data) {
  btn.dataset.ssoWait = '';
  unbusy(btn, 'Test AWS access');
  setConfigStatus(!!data.ok, data.message);
  if (data.ok) {
    // Access is proven: refresh the identity-driven UI and drop a known-good address into the
    // test-email box (a verified recipient also satisfies SES sandbox accounts).
    loadSesIdentities().then(() => {
      if (!sesIdentities) return;
      const from = $('fromEmail').value.trim();
      const domain = from.includes('@') ? from.slice(from.lastIndexOf('@') + 1).toLowerCase() : '';
      const fromVerified =
        sesIdentities.emails.some((e) => e.toLowerCase() === from.toLowerCase()) ||
        sesIdentities.domains.some((d) => d.toLowerCase() === domain);
      const target = fromVerified ? from : sesIdentities.emails[0];
      if (target) $('testTo').value = target;
    });
  }
}

// Launches the browser sign-in immediately and waits for it. While waiting, the button stays
// enabled as "Relaunch SSO sign-in" (the server cancels the previous attempt), for when the
// sign-in opened in the wrong browser. A superseded wait simply abandons its response.
async function ssoSignInThenRetest(btn) {
  const attempt = ++ssoAttempt;
  btn.dataset.ssoWait = '1';
  btn.disabled = false;
  btn.textContent = 'Relaunch SSO sign-in';
  setConfigStatus(false,
    'A browser window has been opened on the machine running the server for the AWS SSO sign-in. ' +
    'Complete the sign-in there — or click "Relaunch SSO sign-in" if it opened in the wrong browser.');

  const login = (await apiPost('/api/aws-sso-login')).data;
  if (attempt !== ssoAttempt) return;          // a relaunch superseded this wait
  if (!login.ok) { finishAwsAccessTest(btn, login); return; }

  btn.dataset.ssoWait = '';
  busy(btn, 'Testing…');
  const { data } = await apiPost('/api/test-aws-access');
  finishAwsAccessTest(btn, data);
}

$('btnTestAwsAccess').addEventListener('click', async (e) => {
  const btn = e.target;
  if (btn.dataset.ssoWait === '1') { ssoSignInThenRetest(btn); return; }   // relaunch
  busy(btn, 'Testing…');
  await apiPost('/api/config', collectConfig());
  const { data } = await apiPost('/api/test-aws-access');
  if (data.canAttemptSsoLogin) { ssoSignInThenRetest(btn); return; }
  finishAwsAccessTest(btn, data);
});

$('btnSendTest').addEventListener('click', async (e) => {
  const to = $('testTo').value.trim();
  if (!to) { setConfigStatus(false, 'Enter an address to send the test to.'); return; }
  const btn = e.target; busy(btn, 'Sending…');
  await apiPost('/api/config', collectConfig());
  await saveTemplate();
  const { data } = await apiPost('/api/test', { to });
  unbusy(btn, 'Send test');
  if (data.success) setConfigStatus(true, 'Test email sent to ' + to + (data.messageId ? ' (id: ' + data.messageId + ')' : ''));
  else setConfigStatus(false, 'Test failed: ' + data.error);
});

$$('input[name=provider]').forEach((r) => r.addEventListener('change', () => { state.provider = getRadio('provider'); toggleProvider(); loadSesIdentities(); }));
$$('input[name=awsAuth]').forEach((r) => r.addEventListener('change', toggleAwsAuth));
$('unlimited').addEventListener('change', () => {
  const u = $('unlimited').checked;
  $('rate').disabled = u;
  if (u) $('rate').value = ''; else if (!$('rate').value) $('rate').value = '5';
});

function toggleProvider() {
  const smtp = getRadio('provider') === 'Smtp';
  $('smtpCard').classList.toggle('hidden', !smtp);
  $('awsCard').classList.toggle('hidden', smtp);
}
function toggleAwsAuth() {
  const profile = getRadio('awsAuth') === 'Profile';
  $('awsProfilePanel').classList.toggle('hidden', !profile);
  $('awsKeyPanel').classList.toggle('hidden', profile);
}
function setRadio(name, value) {
  $$('input[name=' + name + ']').forEach((r) => (r.checked = r.value === value));
}
function getRadio(name) {
  const el = document.querySelector('input[name=' + name + ']:checked');
  return el ? el.value : null;
}
function setConfigStatus(ok, msg) {
  const el = $('configStatus');
  el.className = 'statusbar ' + (ok ? 'ok' : 'err');
  el.textContent = msg;
}
function busy(btn, text) { btn.dataset.label = btn.textContent; btn.textContent = text; btn.disabled = true; }
function unbusy(btn, text) { btn.textContent = text; btn.disabled = false; }

// =====================================================================
// Recipients
// =====================================================================
$('fileInput').addEventListener('change', async (e) => {
  const file = e.target.files[0];
  if (!file) return;
  const form = new FormData();
  form.append('file', file);
  const r = await fetch('/api/recipients', { method: 'POST', body: form });
  const data = await r.json();
  if (!r.ok) { alert(data.error || 'Import failed.'); e.target.value = ''; return; }
  applyRecipients(data);
  e.target.value = '';
});
$('btnClearRecipients').addEventListener('click', async () => {
  const { data } = await apiPost('/api/recipients/clear');
  applyRecipients(data);
});

function applyRecipients(data) {
  state.columns = data.columns;
  state.count = data.count;
  refreshFooter();

  $('recipientSummary').textContent = data.count === 0
    ? 'No recipients loaded yet.'
    : data.count.toLocaleString() + ' recipient(s) loaded' + (data.skipped > 0 ? '  •  ' + data.skipped + ' row(s) skipped (no email)' : '');

  const chips = $('recipientChips'); chips.innerHTML = '';
  data.columns.forEach((c) => {
    const s = document.createElement('span'); s.className = 'chip static'; s.textContent = '{{' + c + '}}'; chips.appendChild(s);
  });

  const table = $('recipientTable');
  const empty = $('recipientEmpty');
  if (data.count === 0) { table.innerHTML = ''; empty.classList.remove('hidden'); }
  else {
    empty.classList.add('hidden');
    let html = '<thead><tr>' + data.columns.map((c) => '<th>' + esc(c) + '</th>').join('') + '</tr></thead><tbody>';
    html += data.rows.map((row) => '<tr>' + data.columns.map((c) => '<td>' + esc(row[c] || '') + '</td>').join('') + '</tr>').join('');
    html += '</tbody>';
    table.innerHTML = html;
  }
  buildComposeChips();
}

// =====================================================================
// Compose (rich editor)
// =====================================================================
const editor = $('editor');
let savedRange = null;

function saveSelection() {
  const sel = window.getSelection();
  if (sel && sel.rangeCount > 0) {
    const r = sel.getRangeAt(0);
    if (editor.contains(r.commonAncestorContainer)) savedRange = r.cloneRange();
  }
}
function restoreSelection() {
  if (!savedRange) { editor.focus(); return; }
  const sel = window.getSelection(); sel.removeAllRanges(); sel.addRange(savedRange);
}
function exec(cmd, value) {
  restoreSelection();
  document.execCommand(cmd, false, value || null);
  editor.focus(); saveSelection(); scheduleSave();
}

$('toolbar').addEventListener('mousedown', (e) => { if (!e.target.closest('#linkbar')) e.preventDefault(); });
$$('.tbtn[data-cmd]').forEach((b) => b.addEventListener('click', () => exec(b.dataset.cmd)));
$$('.tbtn[data-block]').forEach((b) => b.addEventListener('click', () => exec('formatBlock', b.dataset.block)));
$$('.swatch').forEach((s) => {
  s.addEventListener('mousedown', (e) => e.preventDefault());
  s.addEventListener('click', () => exec('foreColor', s.dataset.color));
});
editor.addEventListener('keyup', saveSelection);
editor.addEventListener('mouseup', saveSelection);
editor.addEventListener('blur', saveSelection);
editor.addEventListener('input', scheduleSave);
$('subject').addEventListener('input', scheduleSave);
$('source').addEventListener('input', scheduleSave);

// link bar
const linkbar = $('linkbar'), linkInput = $('linkInput');
$('linkBtn').addEventListener('click', () => { saveSelection(); linkbar.style.display = 'flex'; linkInput.value = 'https://'; linkInput.focus(); linkInput.select(); });
linkInput.addEventListener('keydown', (e) => {
  if (e.key === 'Enter') { e.preventDefault(); const url = linkInput.value.trim(); linkbar.style.display = 'none'; if (url) exec('createLink', url); }
  else if (e.key === 'Escape') { e.preventDefault(); linkbar.style.display = 'none'; editor.focus(); }
});

let lastFocusSubject = false;
$('subject').addEventListener('focus', () => (lastFocusSubject = true));
editor.addEventListener('focus', () => (lastFocusSubject = false));
$('source').addEventListener('focus', () => (lastFocusSubject = false));

function insertPlaceholder(token) {
  if (lastFocusSubject) {
    const el = $('subject'); const s = el.selectionStart || el.value.length;
    el.value = el.value.slice(0, s) + token + el.value.slice(s); el.focus(); el.selectionStart = el.selectionEnd = s + token.length;
    scheduleSave(); return;
  }
  if (state.sourceMode) {
    const el = $('source'); const s = el.selectionStart || el.value.length;
    el.value = el.value.slice(0, s) + token + el.value.slice(s); el.focus(); el.selectionStart = el.selectionEnd = s + token.length;
    scheduleSave(); return;
  }
  restoreSelection(); editor.focus(); document.execCommand('insertHTML', false, token); saveSelection(); scheduleSave();
}

function buildComposeChips() {
  const chips = $('composeChips'); chips.innerHTML = '';
  state.columns.forEach((c) => {
    const token = '{{' + c + '}}';
    const b = document.createElement('button'); b.className = 'chip'; b.textContent = token;
    b.addEventListener('click', () => insertPlaceholder(token));
    chips.appendChild(b);
  });
}

$('btnToggleSource').addEventListener('click', () => {
  if (!state.sourceMode) {
    $('source').value = editor.innerHTML;
    $('source').classList.remove('hidden'); editor.classList.add('hidden');
    $('btnToggleSource').textContent = 'Visual editor'; state.sourceMode = true;
  } else {
    editor.innerHTML = $('source').value;
    $('source').classList.add('hidden'); editor.classList.remove('hidden');
    $('btnToggleSource').textContent = 'Edit HTML source'; state.sourceMode = false;
  }
});

let saveTimer = null;
function scheduleSave() { clearTimeout(saveTimer); saveTimer = setTimeout(saveTemplate, 400); }
async function saveTemplate() {
  const html = state.sourceMode ? $('source').value : editor.innerHTML;
  await apiPost('/api/template', { subject: $('subject').value, html });
}
async function loadTemplate() {
  const t = await apiGet('/api/template');
  $('subject').value = t.subject || '';
  editor.innerHTML = t.html || '';
}

// =====================================================================
// Preview
// =====================================================================
async function loadPreviewList() {
  const sel = $('recipientSelect');
  const prevIndex = sel.selectedIndex;
  sel.innerHTML = '';
  const summary = await apiGet('/api/recipients');
  const n = summary.count > 0 ? summary.count : 1;
  const rows = summary.rows || [];
  for (let i = 0; i < Math.min(n, 200); i++) {
    const opt = document.createElement('option');
    const label = summary.count > 0 && rows[i] ? (rows[i]['Name'] ? rows[i]['Name'] + ' <' + rows[i]['Email'] + '>' : rows[i]['Email']) : 'Alex Sample <alex.sample@example.com>';
    opt.textContent = label; opt.value = i; sel.appendChild(opt);
  }
  sel.selectedIndex = prevIndex >= 0 && prevIndex < sel.options.length ? prevIndex : 0;
  renderPreview();
}
$('recipientSelect').addEventListener('change', renderPreview);
$('prevRecipient').addEventListener('click', () => { const s = $('recipientSelect'); if (s.selectedIndex > 0) { s.selectedIndex--; renderPreview(); } });
$('nextRecipient').addEventListener('click', () => { const s = $('recipientSelect'); if (s.selectedIndex < s.options.length - 1) { s.selectedIndex++; renderPreview(); } });

async function renderPreview() {
  const sel = $('recipientSelect');
  const index = sel.selectedIndex < 0 ? 0 : sel.selectedIndex;
  const { data } = await apiPost('/api/preview', { index });
  $('previewTo').textContent = data.to;
  $('previewSubject').textContent = data.subject;
  const frame = $('previewFrame');
  frame.srcdoc = '<!DOCTYPE html><html><head><meta charset="utf-8"><style>body{font-family:Segoe UI,system-ui,sans-serif;color:#0f172a;font-size:14px;line-height:1.55;padding:22px;margin:0;}a{color:#2563eb;}img{max-width:100%;}</style></head><body>' + (data.html || '') + '</body></html>';
}

// =====================================================================
// Send
// =====================================================================
function refreshSendSummary() {
  $('sendRecipients').textContent = state.count.toLocaleString();
  $('sendProvider').textContent = state.provider === 'Aws' ? 'AWS SES' : 'SMTP';
  const unlimited = $('unlimited').checked;
  $('sendRate').textContent = unlimited ? 'Unlimited' : (($('rate').value || '5') + ' / sec');
  $('sendFrom').textContent = $('fromEmail').value || '—';
}

$('btnStartSend').addEventListener('click', async () => {
  if (state.count === 0) { alert('Import recipients before sending.'); return; }
  await apiPost('/api/config', collectConfig());
  await saveTemplate();
  if (!confirm('Send this message to ' + state.count.toLocaleString() + ' recipient(s) using ' + $('sendProvider').textContent + '?')) return;

  $('logBody').innerHTML = '';
  const { ok, data } = await apiPost('/api/send');
  if (!ok) { alert((data.error || 'Send failed') + (data.errors ? '\n• ' + data.errors.join('\n• ') : '')); return; }

  state.sendJob = data.jobId;
  setSending(true);
  state.poll = setInterval(pollSend, 500);
});

$('btnCancelSend').addEventListener('click', async () => {
  if (state.sendJob) { await apiPost('/api/send/' + state.sendJob + '/cancel'); $('btnCancelSend').disabled = true; $('progressLabel').textContent = 'Cancelling…'; }
});

async function pollSend() {
  if (!state.sendJob) return;
  const s = await apiGet('/api/send/' + state.sendJob);
  const pct = s.total === 0 ? 100 : Math.round((s.processed / s.total) * 100);
  $('progressBar').style.width = pct + '%';
  $('progressLabel').textContent = s.done ? (s.cancelled ? 'Cancelled' : (s.error ? 'Error' : 'Completed')) : 'Sending…';
  $('progressCounts').textContent = s.succeeded.toLocaleString() + ' sent · ' + s.failed.toLocaleString() + ' failed · ' + s.processed.toLocaleString() + ' / ' + s.total.toLocaleString();

  renderLog(s);

  if (s.done) {
    clearInterval(state.poll); state.poll = null;
    setSending(false);
    if (s.error) alert('Send failed: ' + s.error);
  }
}

function renderLog(s) {
  // Show failures (the server tracks these); successes are summarised in the counts.
  const body = $('logBody');
  body.innerHTML = (s.recentErrors || []).map((e) =>
    '<tr><td><span class="badge fail">Failed</span></td><td>' + esc(e.email) + '</td><td>' + esc(e.message) + '</td></tr>').join('');
  if ((s.recentErrors || []).length === 0 && s.processed > 0) {
    body.innerHTML = '<tr><td><span class="badge ok">OK</span></td><td colspan="2">' + s.succeeded.toLocaleString() + ' delivered to transport, no failures.</td></tr>';
  }
}

function setSending(running) {
  $('btnStartSend').disabled = running;
  $('btnCancelSend').disabled = !running;
  $('btnStartSend').textContent = running ? 'Sending…' : 'Start sending';
  if (running) { $('progressBar').style.width = '0%'; $('progressLabel').textContent = 'Starting…'; $('progressCounts').textContent = ''; }
}

// ---------------- utils ----------------
function esc(s) { return String(s).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c])); }

// ---------------- boot ----------------
(async function init() {
  loadAwsProfiles();
  await loadConfig();
  await loadTemplate();
  const summary = await apiGet('/api/recipients');
  applyRecipients(summary);
})();

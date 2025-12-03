const state = {
  apiKey: null,
  user: null,
  tier: null,
  endpoints: [],
  allowedEndpoints: [],
  entries: [],
  permissions: {},
  lastDetailEntry: null,
  configEditor: null,
  monacoReady: null,
  lastConfigText: null
};

const KIND_SUGGESTIONS = [
  'note',
  'fact',
  'procedure',
  'conversationTurn',
  'timelineEvent',
  'codeSnippet',
  'decision',
  'observation',
  'question',
  'task'
];

const selectors = {
  loginOverlay: document.getElementById('login-overlay'),
  loginForm: document.getElementById('login-form'),
  status: document.getElementById('app-status'),
  navButtons: Array.from(document.querySelectorAll('[data-tab]')),
  tabs: Array.from(document.querySelectorAll('.tab-pane'))
};

function showEntryModal() {
  if (!state.allowedEndpoints.length) {
    setStatus('No accessible project to create entries', 'danger');
    return;
  }

  resetEntryForm();
  document.getElementById('entry-modal').classList.remove('hidden');
  document.getElementById('entry-kind').focus();
}

function closeEntryModal() {
  document.getElementById('entry-modal').classList.add('hidden');
}

function stopModalClickBubble() {
  const dialog = document.querySelector('#entry-modal .modal-dialog');
  if (dialog) {
    dialog.addEventListener('click', (event) => {
      event.stopPropagation();
    });
  }
}

function resetEntryForm() {
  const modal = document.getElementById('entry-modal');
  if (!modal) {
    return;
  }

  document.getElementById('entry-form').reset();
  const projectSelect = document.getElementById('entry-project');
  if (projectSelect && state.allowedEndpoints.length) {
    projectSelect.value = state.allowedEndpoints[0].key;
  }

  document.getElementById('entry-kind').value = 'note';
  document.getElementById('entry-tier').value = 'provisional';
  document.getElementById('entry-confidence').value = '0.5';
  document.getElementById('entry-tags').value = '';
  document.getElementById('entry-body').value = '';
  document.getElementById('entry-id').value = '';
  document.getElementById('entry-epic-slug').value = '';
  document.getElementById('entry-epic-case').value = '';
  document.getElementById('entry-relations').value = '';
  document.getElementById('entry-source').value = '';
  document.getElementById('entry-permanent').checked = false;
  document.getElementById('entry-pinned').checked = false;
}

function handleEntryFormSubmit(event) {
  event.preventDefault();
  createEntry();
}

document.addEventListener('DOMContentLoaded', () => {
  setupNavigation();
  selectors.loginForm.addEventListener('submit', handleLogin);
  document.getElementById('logout-btn').addEventListener('click', logout);
  document.getElementById('entity-refresh').addEventListener('click', () => loadEntities());
  document.getElementById('projects-list').addEventListener('click', handleProjectButton);
  document.getElementById('entities-body').addEventListener('click', handleEntityActions);
  document.getElementById('entity-detail').addEventListener('click', handleEntryDetailActions);
  document.getElementById('user-save').addEventListener('click', saveUser);
  document.getElementById('user-form').addEventListener('submit', (event) => event.preventDefault());
  document.getElementById('permissions-endpoint').addEventListener('change', populatePermissionsPayload);
  document.getElementById('permissions-save').addEventListener('click', savePermissions);
  document.getElementById('project-create').addEventListener('click', createProject);
  document.getElementById('open-entry-modal').addEventListener('click', showEntryModal);
  document.getElementById('entry-modal-close').addEventListener('click', closeEntryModal);
  document.getElementById('entry-modal-cancel').addEventListener('click', closeEntryModal);
  document.getElementById('entry-form').addEventListener('submit', handleEntryFormSubmit);
  document.getElementById('entry-modal').addEventListener('click', (event) => {
    if (event.target === document.getElementById('entry-modal')) {
      closeEntryModal();
    }
  });
  renderRelationsControl(document.getElementById('entry-relations'), []);
  renderSourceControl(document.getElementById('entry-source'), null);
  document.getElementById('entity-project').addEventListener('change', () => loadEntities());
  document.getElementById('health-refresh')?.addEventListener('click', loadHealthBlade);
  document.getElementById('health-download-logs')?.addEventListener('click', downloadLogs);
  document.getElementById('config-reload')?.addEventListener('click', () => loadConfig());
  document.getElementById('config-validate')?.addEventListener('click', () => validateConfig(false));
  document.getElementById('config-save')?.addEventListener('click', () => validateConfig(true));
  stopModalClickBubble();
  selectors.loginOverlay.classList.remove('hidden');
  (async () => {
    await fetchEndpoints();
    await tryResumeSession();
  })();
});

function setupNavigation() {
  selectors.navButtons.forEach((button) => {
    button.addEventListener('click', () => {
      setActiveTab(button.dataset.tab);
    });
  });
}

function setActiveTab(tab) {
  selectors.tabs.forEach((pane) => {
    pane.classList.toggle('active', pane.id === tab);
  });
  selectors.navButtons.forEach((button) => {
    button.classList.toggle('active', button.dataset.tab === tab);
  });
  if (tab === 'overview') {
    loadOverview();
  }
  if (tab === 'projects') {
    refreshAuthState();
  }
  if (tab === 'entities') {
    loadEntities();
  }
  if (tab === 'users') {
    loadAdminData();
  }
  if (tab === 'help') {
    loadHelpContent();
  }
  if (tab === 'agent-help') {
    loadAgentHelp();
  }
  if (tab === 'admin-ui-help') {
    loadAdminUiHelp();
  }
  if (tab === 'config') {
    loadConfig();
  }
  if (tab === 'health') {
    loadHealthBlade();
  }
}

function handleProjectButton(event) {
  const button = event.target.closest('[data-action="save-project"]');
  if (button) {
    const endpoint = button.dataset.endpoint;
    saveProjectMetadata(endpoint);
    return;
  }

  const deleteButton = event.target.closest('[data-action="delete-project"]');
  if (!deleteButton) {
    return;
  }

  const endpoint = deleteButton.dataset.endpoint;
  deleteProject(endpoint);
}

function handleEntityActions(event) {
  const button = event.target.closest('[data-action="view-entry"]');
  if (button) {
    const entryId = button.dataset.entryId;
    const result = state.entries.find((item) => item.entry?.id === entryId);
    if (result) {
      renderEntryDetail(result.entry);
    }
  }

  if (event.target.closest('[data-action="update-entry"]')) {
    saveEntryDetail();
  }

  if (event.target.closest('[data-action="delete-entry"]')) {
    const entryId = event.target.closest('[data-entry-id]')?.dataset.entryId;
    if (entryId) {
      deleteEntry(entryId);
    }
  }
}

function handleEntryDetailActions(event) {
  const save = event.target.closest('[data-action="update-entry"]');
  if (save) {
    saveEntryDetail();
    return;
  }

  const del = event.target.closest('[data-action="delete-entry"]');
  if (del) {
    const entryId = del.dataset.entryId || state.lastDetailEntry?.id;
    if (entryId) {
      deleteEntry(entryId);
    }
  }
}

async function fetchEndpoints() {
  try {
    const response = await fetch('/admin/endpoints');
    if (!response.ok) {
      setStatus('Unable to load endpoints', 'danger');
      return;
    }

    const data = await response.json();
    state.endpoints = data.endpoints ?? [];
    renderProjectsList();
  } catch (error) {
    console.error(error);
    setStatus('Failed to load endpoint catalog', 'danger');
  }
}

function renderProjectsList() {
  const container = document.getElementById('projects-list');
  if (!state.allowedEndpoints.length) {
    const message = state.endpoints.length
      ? 'Log in with an API key to see projects you can access.'
      : 'No endpoints configured on the server.';
    container.innerHTML = `<div class="text-muted">${message}</div>`;
    return;
  }

  container.innerHTML = state.allowedEndpoints
    .map((endpoint) => {
      return `
        <div class="project-card" data-endpoint="${endpoint.key}">
          <div class="d-flex justify-content-between align-items-start">
            <div>
              <strong>${escapeHtml(endpoint.name || endpoint.key)}</strong>
              <div class="text-muted small">Slug: ${escapeHtml(endpoint.slug)}</div>
              <div class="text-muted small">Storage: ${escapeHtml(formatStoragePath(endpoint.storagePath))}</div>
            </div>
            <div>
              <button type="button" class="btn btn-sm btn-outline-primary me-2" data-action="save-project" data-endpoint="${endpoint.key}">Save</button>
              <button type="button" class="btn btn-sm btn-outline-danger" data-action="delete-project" data-endpoint="${endpoint.key}">Delete</button>
            </div>
          </div>
            <div class="project-meta mt-3" data-form="${endpoint.key}">
            <div>
              <label class="form-label">Name</label>
              <input name="name" class="form-control" value="${escapeHtml(endpoint.name)}" />
            </div>
            <div>
              <label class="form-label">Slug</label>
              <input name="slug" class="form-control" value="${escapeHtml(endpoint.slug)}" />
            </div>
            <div>
              <label class="form-label">Description</label>
              <input name="description" class="form-control" value="${escapeHtml(endpoint.description)}" />
            </div>
            <div>
              <label class="form-label">Storage</label>
              <input name="storagePath" class="form-control" value="${escapeHtml(formatStoragePath(endpoint.storagePath))}" />
            </div>
            <div class="form-check mt-2">
              <input name="includeShared" class="form-check-input" type="checkbox" ${endpoint.includeInSearchByDefault ? 'checked' : ''} />
              <label class="form-check-label">Include shared memories</label>
            </div>
            <div class="form-check mt-2">
              <input name="inheritShared" class="form-check-input" type="checkbox" ${endpoint.inheritShared ? 'checked' : ''} />
              <label class="form-check-label">Inherit shared memory</label>
            </div>
          </div>
        </div>
      `;
    })
    .join('');
}

async function createProject() {
  if (!ensureAuth()) {
    return;
  }

  const key = document.getElementById('project-key').value.trim();
  const name = document.getElementById('project-name').value.trim();
  const slug = document.getElementById('project-slug').value.trim();
  const description = document.getElementById('project-description').value.trim();
  const storagePath = normalizeStoragePath(document.getElementById('project-storage').value);
  const includeInSearch = document.getElementById('project-include-search').checked;
  const inheritShared = document.getElementById('project-inherit-shared').checked;

  if (!key || !name) {
    setStatus('Project key and name are required', 'danger');
    return;
  }

  const response = await fetch('/admin/endpoints/manage', {
    method: 'POST',
    headers: authHeaders(true),
    body: JSON.stringify({
      key,
      name,
      slug,
      description,
      storagePath,
      includeInSearchByDefault: includeInSearch,
      inheritShared
    })
  });

  if (!response.ok) {
    setStatus('Failed to create project', 'danger');
    return;
  }

  setStatus('Project created', 'success');
  await refreshAuthState();
}

async function deleteProject(endpointKey) {
  if (!endpointKey || !ensureAuth()) {
    return;
  }

  const response = await fetch(`/admin/endpoints/manage/${encodeURIComponent(endpointKey)}`, {
    method: 'DELETE',
    headers: authHeaders(false)
  });

  if (!response.ok) {
    setStatus('Failed to delete project', 'danger');
    return;
  }

  setStatus('Project removed', 'info');
  await refreshAuthState();
}

async function saveProjectMetadata(endpointKey) {
  if (!ensureAuth()) {
    return;
  }

  const form = document.querySelector(`[data-form="${endpointKey}"]`);
  if (!form) {
    setStatus('Project form missing', 'danger');
    return;
  }

  const payload = {
    id: `${endpointKey}:metadata`,
    project: endpointKey,
    kind: 'projectMetadata',
    title: `Project metadata (${endpointKey})`,
    curationTier: 'curated',
    tags: ['project'],
    body: {
      name: form.querySelector('[name="name"]').value,
      slug: form.querySelector('[name="slug"]').value,
      description: form.querySelector('[name="description"]').value,
      storagePath: normalizeStoragePath(form.querySelector('[name="storagePath"]').value),
      includeShared: form.querySelector('[name="includeShared"]').checked,
      lastSavedUtc: new Date().toISOString()
    }
  };

  const response = await fetch(`/mcp/${endpointKey}/entries`, {
    method: 'POST',
    headers: authHeaders(true),
    body: JSON.stringify(payload)
  });

  if (!response.ok) {
    setStatus('Failed to store project metadata', 'danger');
    return;
  }

  setStatus('Project metadata saved', 'success');
}

function updateEntityProjectSelect() {
  const select = document.getElementById('entity-project');
  const entrySelect = document.getElementById('entry-project');
  if (!select) {
    return;
  }

  if (!state.allowedEndpoints.length) {
    select.innerHTML = '<option value="">Select a project</option>';
    if (entrySelect) {
      entrySelect.innerHTML = '<option value="">Select a project</option>';
    }
    return;
  }

  const options = state.allowedEndpoints
    .map((endpoint) => `<option value="${endpoint.key}">${escapeHtml(endpoint.name || endpoint.key)}</option>`)
    .join('');

  select.innerHTML = options;
  select.value = state.allowedEndpoints[0].key;

  if (entrySelect) {
    entrySelect.innerHTML = options;
    entrySelect.value = state.allowedEndpoints[0].key;
  }
}

function updatePermissionsEndpointSelect() {
  const select = document.getElementById('permissions-endpoint');
  if (!select) {
    return;
  }

  if (!state.allowedEndpoints.length) {
    select.innerHTML = '<option value="">Select a project</option>';
    return;
  }

  select.innerHTML = state.allowedEndpoints
    .map((endpoint) => `<option value="${endpoint.key}">${escapeHtml(endpoint.key)}</option>`)
    .join('');
}

function renderConfigInstructions() {
  const container = document.getElementById('overview-config');
  if (!container) {
    return;
  }

  const endpoints = state.allowedEndpoints.map((endpoint) => endpoint.key).join(', ') || 'none';
  const helpLinks = `
    <a href="/admin/help/end-user" target="_blank">End-user help</a>
    · <a href="/admin/help/agent" target="_blank">Agent help</a>
  `;

  container.innerHTML = `
    <h4>Welcome${state.user ? ', ' + escapeHtml(state.user) : ''}!</h4>
    <p class="text-muted small mb-1">Tier: ${escapeHtml(state.tier || 'Reader')} · Authorized endpoints: ${escapeHtml(endpoints)}</p>
    <p class="mb-0">Need a refresher? ${helpLinks}</p>
  `;
}

async function loadHelpContent() {
  if (!ensureAuth()) {
    return;
  }

  const response = await fetch('/admin/help/end-user', { headers: authHeaders(false) });
  if (!response.ok) {
    setStatus('Unable to load help content', 'danger');
    return;
  }

  const html = await response.text();
  document.getElementById('help-content').innerHTML = html;
}

async function ensureMonaco() {
  if (state.monacoReady) {
    return state.monacoReady;
  }

  state.monacoReady = new Promise((resolve, reject) => {
    if (window.monaco) {
      resolve(window.monaco);
      return;
    }

    if (typeof window.require !== 'function') {
      reject(new Error('Monaco loader not available'));
      return;
    }

    window.require(['vs/editor/editor.main'], () => resolve(window.monaco), reject);
  });

  return state.monacoReady;
}

async function loadConfig() {
  if (!ensureAuth()) {
    return;
  }

  setConfigStatus('Loading...', 'info');
  try {
    const monaco = await ensureMonaco();
    if (!state.configEditor) {
      const container = document.getElementById('config-editor');
      state.configEditor = monaco.editor.create(container, {
        value: '',
        language: 'toml',
        theme: 'vs-dark',
        automaticLayout: true,
        minimap: { enabled: false }
      });
    }

  const response = await fetch('/admin/config/raw', { headers: authHeaders(false), credentials: 'same-origin' });
    if (!response.ok) {
      setConfigStatus('Failed to load config', 'danger');
      return;
    }

    const text = await response.text();
    state.lastConfigText = text;
    state.configEditor.setValue(text);
    clearConfigErrors();
    setConfigStatus('Loaded', 'success');
  } catch (error) {
    console.error('config load failed', error);
    setConfigStatus('Config load failed', 'danger');
  }
}

function renderRelationsControl(container, relations) {
  if (!container) return;
  container.innerHTML = '';

  const list = document.createElement('div');
  list.className = 'relations-list';
  container.appendChild(list);

  const addBtn = document.createElement('button');
  addBtn.type = 'button';
  addBtn.className = 'btn btn-sm btn-outline-primary mt-2';
  addBtn.textContent = 'Add relation';
  addBtn.addEventListener('click', () => addRelationRow(list, { type: 'ref', targetId: '' }));
  container.appendChild(addBtn);

  (relations && relations.length ? relations : [{ type: 'ref', targetId: '' }]).forEach((rel) => addRelationRow(list, rel));
}

function addRelationRow(list, rel) {
  const row = document.createElement('div');
  row.className = 'd-flex gap-2 mb-2 align-items-center';

  const type = document.createElement('select');
  type.className = 'form-select form-select-sm';
  ['ref', 'see-also', 'dependency', 'custom'].forEach((opt) => {
    const o = document.createElement('option');
    o.value = opt;
    o.textContent = opt;
    if (rel.type === opt) o.selected = true;
    type.appendChild(o);
  });

  const target = document.createElement('input');
  target.className = 'form-control form-control-sm';
  target.placeholder = 'project:key';
  target.value = rel.targetId || '';

  const remove = document.createElement('button');
  remove.type = 'button';
  remove.className = 'btn btn-sm btn-outline-danger';
  remove.textContent = '×';
  remove.addEventListener('click', () => row.remove());

  row.append(type, target, remove);
  list.appendChild(row);
}

function collectRelations(container) {
  if (!container) return [];
  const rows = Array.from(container.querySelectorAll('.relations-list > div'));
  const result = [];
  for (const row of rows) {
    const type = row.querySelector('select')?.value?.trim();
    const targetId = row.querySelector('input')?.value?.trim();
    if (!type || !targetId) {
      continue;
    }
    result.push({ type, targetId });
  }
  return result;
}

function renderSourceControl(container, source) {
  if (!container) return;
  container.innerHTML = '';

  const fields = [
    { key: 'type', placeholder: 'api/file/manual' },
    { key: 'url', placeholder: 'https://...' },
    { key: 'path', placeholder: 'C:/path/to/file' },
    { key: 'shard', placeholder: 'shard-id (optional)' }
  ];

  fields.forEach((f) => {
    const group = document.createElement('div');
    group.className = 'mb-2';
    const input = document.createElement('input');
    input.className = 'form-control form-control-sm';
    input.placeholder = f.placeholder;
    input.dataset.field = f.key;
    input.value = source && source[f.key] ? source[f.key] : '';
    group.appendChild(input);
    container.appendChild(group);
  });
}

function collectSource(container) {
  if (!container) return null;
  const inputs = Array.from(container.querySelectorAll('input[data-field]'));
  const src = {};
  inputs.forEach((i) => {
    if (i.value.trim()) {
      src[i.dataset.field] = i.value.trim();
    }
  });
  return Object.keys(src).length ? src : null;
}

async function validateConfig(applyChanges) {
  if (!state.configEditor) {
    await loadConfig();
    if (!state.configEditor) return;
  }

  const content = state.configEditor.getValue();
  const url = applyChanges ? '/admin/config/raw/apply' : '/admin/config/raw/validate';
  setConfigStatus(applyChanges ? 'Saving...' : 'Validating...', 'info');
  clearConfigErrors();
  renderConfigDiff();

  const response = await fetch(url, {
    method: 'POST',
    headers: { ...authHeaders(true), 'Content-Type': 'application/json' },
    credentials: 'same-origin',
    body: JSON.stringify({ content })
  });

  if (response.ok) {
    state.lastConfigText = content;
    setConfigStatus(applyChanges ? 'Saved successfully' : 'Valid TOML', 'success');
    renderConfigDiff(null);
    return;
  }

  const payload = await response.json().catch(() => ({}));
  const errors = payload.errors || ['Validation failed'];
  showConfigErrors(errors);
  setConfigStatus('Invalid TOML', 'danger');
}

function showConfigErrors(errors) {
  const box = document.getElementById('config-errors');
  if (!box) return;
  box.style.display = 'block';
  box.textContent = errors.join('\n');
}

function clearConfigErrors() {
  const box = document.getElementById('config-errors');
  if (!box) return;
  box.style.display = 'none';
  box.textContent = '';
}

function setConfigStatus(message, tone) {
  const el = document.getElementById('config-status');
  if (!el) return;
  el.textContent = message || '';
  el.className = `text-${tone || 'muted'} small ms-2`;
}

function renderConfigDiff() {
  const container = document.getElementById('config-diff');
  if (!container || !state.configEditor || !state.monacoReady) return;

  Promise.resolve(state.monacoReady).then((monaco) => {
    const original = state.lastConfigText ?? '';
    const modified = state.configEditor.getValue();
    if (original === modified) {
      container.style.display = 'none';
      container.textContent = '';
      return;
    }

    container.style.display = 'block';
    container.innerHTML = '';
    const diffNode = document.createElement('div');
    diffNode.style.height = '240px';
    container.appendChild(diffNode);

    monaco.editor.createDiffEditor(diffNode, {
      automaticLayout: true,
      readOnly: true,
      theme: 'vs-dark',
      minimap: { enabled: false }
    }).setModel({
      original: monaco.editor.createModel(original, 'toml'),
      modified: monaco.editor.createModel(modified, 'toml')
    });
  });
}

async function loadHealthBlade() {
  if (!ensureAuth()) {
    return;
  }

  setHealthStatus('Loading...', 'info');
  try {
    const response = await fetch('/health', { headers: authHeaders(false), credentials: 'same-origin' });
    if (!response.ok) {
      setHealthStatus('Failed to load health', 'danger');
      return;
    }
    const report = await response.json();
    renderHealthBlade(report);
    setHealthStatus('Updated', 'success');
  } catch (error) {
    console.error('health load failed', error);
    setHealthStatus('Health load failed', 'danger');
  }
}

function renderHealthBlade(report) {
  const summary = document.getElementById('health-summary');
  const issues = document.getElementById('health-issues');
  if (!summary || !issues) {
    return;
  }

  const status = report.status || 'unknown';
  const time = report.generatedUtc || new Date().toISOString();
  summary.innerHTML = `
    <div class="d-flex justify-content-between align-items-center">
      <div>
        <h5 class="mb-1">Status: <span class="badge bg-${status === 'Healthy' ? 'success' : status === 'Degraded' ? 'warning' : 'danger'}">${escapeHtml(status)}</span></h5>
        <div class="text-muted small">Generated: ${escapeHtml(time)}</div>
      </div>
      <div class="text-end">
        <div class="small text-muted">Stores: ${report.stores?.length ?? 0}</div>
      </div>
    </div>
  `;

  const list = Array.isArray(report.issues) ? report.issues : [];
  if (!list.length) {
    issues.innerHTML = '<div class="text-success">No issues reported.</div>';
    return;
  }

  const content = list
    .map((item) => {
      const severity = escapeHtml(item.severity || 'info');
      const message = escapeHtml(item.message || '');
      return `<div class="border rounded p-2 mb-2"><span class="badge bg-${severity === 'error' ? 'danger' : severity === 'warning' ? 'warning' : 'info'} me-2">${severity}</span>${message}</div>`;
    })
    .join('');

  issues.innerHTML = content;
}

function setHealthStatus(message, tone) {
  const el = document.getElementById('health-status');
  if (!el) return;
  el.textContent = message || '';
  el.className = `text-${tone || 'muted'} small`;
}

async function downloadLogs() {
  if (!ensureAuth()) {
    return;
  }

  setHealthStatus('Preparing logs...', 'info');
  try {
    const response = await fetch('/admin/logs', { headers: authHeaders(false), credentials: 'same-origin' });
    if (!response.ok) {
      setHealthStatus('Failed to download logs', 'danger');
      return;
    }

    const blob = await response.blob();
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'quick-memory-logs.zip';
    document.body.appendChild(a);
    a.click();
    a.remove();
    window.URL.revokeObjectURL(url);
    setHealthStatus('Logs downloaded', 'success');
  } catch (error) {
    console.error('log download failed', error);
    setHealthStatus('Log download failed', 'danger');
  }
}

async function loadAgentHelp() {
  if (!ensureAuth()) {
    return;
  }

  const response = await fetch('/admin/help/agent', { headers: authHeaders(false) });
  if (!response.ok) {
    setStatus('Unable to load agent help', 'danger');
    return;
  }

  const html = await response.text();
  document.getElementById('agent-help-content').innerHTML = html;
}

async function loadAdminUiHelp() {
  if (!ensureAuth()) {
    return;
  }

  const response = await fetch('/admin/help/admin-ui', { headers: authHeaders(false) });
  if (!response.ok) {
    setStatus('Unable to load admin UI help', 'danger');
    return;
  }

  const html = await response.text();
  document.getElementById('admin-ui-help-content').innerHTML = html;
}

function renderEntryDetail(entry) {
  state.lastDetailEntry = entry;
  const container = document.getElementById('entity-detail');
  const updated = entry.timestamps?.updatedUtc ? formatDate(entry.timestamps.updatedUtc) : 'never';
  container.innerHTML = `
    <div class="d-flex justify-content-between">
      <div>
        <h4 class="mb-1">${escapeHtml(entry.title || entry.id)}</h4>
        <p class="text-muted small mb-0">
          ${escapeHtml(entry.kind)} • Project: ${escapeHtml(entry.project)} • Updated: ${updated}
        </p>
      </div>
    </div>
    <form id="entry-detail-form" class="mt-3">
      <div class="row g-2">
        <div class="col-md-4">
          <label class="form-label">Kind</label>
          <input list="kind-options" name="kind" class="form-control" value="${escapeHtml(entry.kind)}" />
        </div>
        <div class="col-md-4">
          <label class="form-label">Title</label>
          <input name="title" class="form-control" value="${escapeHtml(entry.title)}" />
        </div>
        <div class="col-md-4">
          <label class="form-label">Curation tier</label>
          <select name="curationTier" class="form-select">
            <option value="provisional" ${entry.curationTier === 'provisional' ? 'selected' : ''}>Provisional</option>
            <option value="curated" ${entry.curationTier === 'curated' ? 'selected' : ''}>Curated</option>
            <option value="canonical" ${entry.curationTier === 'canonical' ? 'selected' : ''}>Canonical</option>
          </select>
        </div>
      </div>
      <div class="row g-2 mt-2">
        <div class="col-md-6">
          <label class="form-label">Tags (comma separated)</label>
          <input
            name="tags"
            class="form-control"
            value="${(entry.tags || []).join(', ')}"
          />
        </div>
        <div class="col-md-6">
          <label class="form-label">Body (JSON or text)</label>
          <textarea name="body" class="form-control" rows="4">${entry.body ? escapeHtml(typeof entry.body === 'string' ? entry.body : JSON.stringify(entry.body, null, 2)) : ''}</textarea>
        </div>
      </div>
      <div class="row g-2 mt-2">
        <div class="col-md-3">
          <label class="form-label">Confidence</label>
          <input name="confidence" type="number" step="0.01" min="0" max="1" class="form-control" value="${entry.confidence ?? 0.5}" />
        </div>
        <div class="col-md-3">
          <div class="form-check form-switch mt-4 pt-1">
            <input name="isPermanent" class="form-check-input" type="checkbox" ${entry.isPermanent ? 'checked' : ''} />
            <label class="form-check-label">Permanent</label>
          </div>
        </div>
        <div class="col-md-3">
          <div class="form-check form-switch mt-4 pt-1">
            <input name="pinned" class="form-check-input" type="checkbox" ${entry.pinned ? 'checked' : ''} />
            <label class="form-check-label">Pinned</label>
          </div>
        </div>
        <div class="col-md-3">
          <label class="form-label">Project ID</label>
          <input class="form-control" value="${escapeHtml(entry.id)}" readonly />
        </div>
      </div>
      <div class="row g-2 mt-2">
        <div class="col-md-6">
          <label class="form-label">Epic slug</label>
          <input name="epicSlug" class="form-control" value="${escapeHtml(entry.epicSlug)}" />
        </div>
        <div class="col-md-6">
          <label class="form-label">Epic case</label>
          <input name="epicCase" class="form-control" value="${escapeHtml(entry.epicCase)}" />
        </div>
      </div>
      <div class="row g-2 mt-2">
        <div class="col-md-6">
          <label class="form-label">Relations (JSON array)</label>
          <textarea name="relations" class="form-control" rows="2">${entry.relations ? escapeHtml(JSON.stringify(entry.relations, null, 2)) : ''}</textarea>
        </div>
        <div class="col-md-6">
          <label class="form-label">Source metadata (JSON)</label>
          <textarea name="source" class="form-control" rows="2">${entry.source ? escapeHtml(JSON.stringify(entry.source, null, 2)) : ''}</textarea>
        </div>
      </div>
      <div class="mt-3 d-flex gap-2">
        <button type="button" class="btn btn-success" data-action="update-entry">Save entry</button>
        <button type="button" class="btn btn-outline-danger" data-action="delete-entry" data-entry-id="${entry.id}">Delete entry</button>
      </div>
    </form>
  `;
}

async function saveEntryDetail() {
  if (!state.lastDetailEntry || !ensureAuth()) {
    return;
  }

  setStatus('Saving entry...', 'info');

  const form = document.getElementById('entry-detail-form');
  const title = form.querySelector('[name="title"]').value.trim();
  const tags = form
    .querySelector('[name="tags"]').value
    .split(',')
    .map((part) => part.trim())
    .filter(Boolean);
  const tier = form.querySelector('[name="curationTier"]').value.trim();
  const bodyText = form.querySelector('[name="body"]').value.trim();
  const kind = form.querySelector('[name="kind"]').value.trim();
  const confidenceValue = parseFloat(form.querySelector('[name="confidence"]').value);
  const isPermanent = form.querySelector('[name="isPermanent"]').checked;
  const pinned = form.querySelector('[name="pinned"]').checked;
  const epicSlug = form.querySelector('[name="epicSlug"]').value.trim();
  const epicCase = form.querySelector('[name="epicCase"]').value.trim();
  const relationsText = form.querySelector('[name="relations"]').value.trim();
  const sourceText = form.querySelector('[name="source"]').value.trim();

  const payload = {};
  if (kind && kind !== state.lastDetailEntry.kind) {
    payload.kind = kind;
  }
  if (title) {
    payload.title = title;
  }

  if (tags.length) {
    payload.tags = tags;
  }

  if (tier) {
    payload.curationTier = tier;
  }

  if (bodyText) {
    try {
      payload.body = JSON.parse(bodyText);
    } catch (err) {
      payload.body = bodyText;
    }
  }

  if (!Number.isNaN(confidenceValue)) {
    payload.confidence = confidenceValue;
  }

  if (isPermanent !== Boolean(state.lastDetailEntry.isPermanent)) {
    payload.isPermanent = isPermanent;
  }

  if (pinned !== Boolean(state.lastDetailEntry.pinned)) {
    payload.pinned = pinned;
  }

  if (epicSlug) {
    payload.epicSlug = epicSlug;
  }

  if (epicCase) {
    payload.epicCase = epicCase;
  }

  if (relationsText) {
    try {
      const parsed = JSON.parse(relationsText);
      if (Array.isArray(parsed) && parsed.every((r) => r && typeof r === 'object' && r.type && r.targetId)) {
        payload.relations = parsed;
      } else {
        setStatus('Relations must be an array of { "type": "ref", "targetId": "project:key" }', 'danger');
        return;
      }
    } catch {
      setStatus('Relations must be valid JSON', 'danger');
      return;
    }
  }

  if (sourceText) {
    try {
      const parsed = JSON.parse(sourceText);
      if (parsed && typeof parsed === 'object') {
        payload.source = parsed;
      } else {
        setStatus('Source must be a JSON object (e.g., { "type": "api", "url": "https://..." })', 'danger');
        return;
      }
    } catch {
      setStatus('Source metadata must be valid JSON', 'danger');
      return;
    }
  }

  if (!Object.keys(payload).length) {
    setStatus('No changes detected', 'info');
    return;
  }

  const response = await fetch(`/mcp/${state.lastDetailEntry.project}/entries/${state.lastDetailEntry.id}`, {
    method: 'PATCH',
    headers: authHeaders(true),
    body: JSON.stringify(payload)
  });

  if (!response.ok) {
    const err = await response.text().catch(() => '');
    setStatus(`Failed to update entry (${response.status}) ${err}`, 'danger');
    return;
  }

  setStatus('Entry updated', 'success');
  loadEntities();
}

async function loadEntities() {
  if (!ensureAuth()) {
    return;
  }

  const projectSelect = document.getElementById('entity-project');
  const project = (projectSelect && projectSelect.value) || state.allowedEndpoints[0]?.key;
  if (!project) {
    setStatus('No accessible project to query', 'danger');
    return;
  }
  const text = document.getElementById('entity-text').value.trim();
  const response = await fetch(`/mcp/${project}/searchEntries`, {
    method: 'POST',
    headers: authHeaders(true),
    body: JSON.stringify({ text, maxResults: 40, includeShared: true })
  });

  if (response.status === 401) {
    promptLogin('Session expired. Please log in again.');
    return;
  }

  if (!response.ok) {
    setStatus('Failed to fetch entries', 'danger');
    return;
  }

  const data = await response.json();
  state.entries = data.results ?? [];
  renderEntitiesTable();
}

function renderEntitiesTable() {
  const body = document.getElementById('entities-body');
  if (!state.entries.length) {
    body.innerHTML = '<tr><td colspan="6" class="text-muted">No entries found.</td></tr>';
    return;
  }

  body.innerHTML = state.entries
    .map((result) => {
      const entry = result.entry;
      const updated = entry.timestamps?.updatedUtc ? formatDate(entry.timestamps.updatedUtc) : 'never';
      return `
        <tr>
          <td>${escapeHtml(entry.id)}</td>
          <td>${escapeHtml(entry.title || entry.id)}</td>
          <td>${escapeHtml(entry.kind)}</td>
          <td>${(result.score ?? 0).toFixed(2)}</td>
          <td>${updated}</td>
          <td><button class="btn btn-sm btn-outline-secondary" data-action="view-entry" data-entry-id="${entry.id}">View</button></td>
        </tr>
      `;
    })
    .join('');
}

async function createEntry() {
  if (!ensureAuth()) {
    return;
  }

  const form = document.getElementById('entry-form');
  if (!form) {
    return;
  }

  const project = document.getElementById('entry-project').value;
  if (!project) {
    setStatus('Select a project for the entry', 'danger');
    return;
  }

  const kind = document.getElementById('entry-kind').value.trim() || 'note';
  const title = document.getElementById('entry-title').value.trim();
  const tier = document.getElementById('entry-tier').value || 'provisional';
  const tags = document
    .getElementById('entry-tags')
    .value.split(',')
    .map((part) => part.trim())
    .filter(Boolean);
  const rawBody = document.getElementById('entry-body').value.trim();
  let body;
  if (rawBody) {
    try {
      body = JSON.parse(rawBody);
    } catch (err) {
      body = rawBody;
    }
  }

  const idField = document.getElementById('entry-id').value.trim();
  const generatedId = (crypto.randomUUID && `${project}:${crypto.randomUUID()}`) || `${project}:${Date.now().toString(36)}`;
  const payload = {
    project,
    id: idField || generatedId,
    kind,
    title,
    curationTier: tier,
    tags,
    body,
    confidence: parseFloat(document.getElementById('entry-confidence').value) || 0.5,
    isPermanent: document.getElementById('entry-permanent').checked,
    pinned: document.getElementById('entry-pinned').checked
  };

  const epicSlug = document.getElementById('entry-epic-slug').value.trim();
  if (epicSlug) {
    payload.epicSlug = epicSlug;
  }

  const epicCase = document.getElementById('entry-epic-case').value.trim();
  if (epicCase) {
    payload.epicCase = epicCase;
  }

  const relationsRaw = document.getElementById('entry-relations').value.trim();
  if (relationsRaw) {
    try {
      const parsed = JSON.parse(relationsRaw);
      if (Array.isArray(parsed)) {
        payload.relations = parsed;
      } else {
        setStatus('Relations must be a JSON array', 'danger');
        return;
      }
    } catch (err) {
      setStatus('Relations must be valid JSON', 'danger');
      return;
    }
  }

  const sourceRaw = document.getElementById('entry-source').value.trim();
  if (sourceRaw) {
    try {
      payload.source = JSON.parse(sourceRaw);
    } catch (err) {
      setStatus('Source metadata must be valid JSON', 'danger');
      return;
    }
  }

  const response = await fetch(`/mcp/${project}/entries`, {
    method: 'POST',
    headers: authHeaders(true),
    body: JSON.stringify(payload)
  });

  if (!response.ok) {
    setStatus('Failed to create entry', 'danger');
    return;
  }

  setStatus('Entry created', 'success');
  closeEntryModal();
  loadEntities();
}

async function deleteEntry(entryId) {
  if (!ensureAuth()) {
    return;
  }

  const project = state.lastDetailEntry?.project || state.allowedEndpoints[0]?.key;
  if (!project) {
    setStatus('Project context missing', 'danger');
    return;
  }

  const response = await fetch(`/mcp/${project}/entries/${entryId}?force=true`, {
    method: 'DELETE',
    headers: authHeaders(false)
  });

  if (!response.ok) {
    setStatus('Failed to delete entry', 'danger');
    return;
  }

  setStatus('Entry deleted', 'info');
  loadEntities();
}

async function loadAdminData() {
  await Promise.all([loadUsers(), loadPermissions()]);
  updatePermissionsEndpointSelect();
  populatePermissionsPayload();
}

async function loadUsers() {
  if (!ensureAuth()) {
    return;
  }

  const response = await fetch('/admin/users', { method: 'GET', headers: authHeaders(false) });
  if (response.status === 401) {
    promptLogin('Insufficient privileges for admin operations.');
    return;
  }

  if (!response.ok) {
    setStatus('Unable to load users', 'danger');
    return;
  }

  const data = await response.json();
  const tbody = document.querySelector('#users-table tbody');
  tbody.innerHTML = data
    .map((user) => {
      return `
        <tr>
          <td>${escapeHtml(user.username)}</td>
          <td>${escapeHtml(user.defaultTier)}</td>
          <td><code class="text-break">${escapeHtml(user.apiKey)}</code></td>
          <td><button class="btn btn-sm btn-danger" onclick="removeUser('${user.username}')">Delete</button></td>
        </tr>
      `;
    })
    .join('');
}

window.removeUser = async function (username) {
  if (!ensureAuth()) {
    return;
  }

  const response = await fetch(`/admin/users/${encodeURIComponent(username)}`, {
    method: 'DELETE',
    headers: authHeaders(false)
  });

  if (!response.ok) {
    setStatus('Failed to remove user', 'danger');
    return;
  }

  setStatus(`${username} removed`, 'info');
  loadUsers();
};

async function saveUser() {
  if (!ensureAuth()) {
    return;
  }

  const username = document.getElementById('user-username').value.trim();
  const apiKey = document.getElementById('user-apikey').value.trim();
  const tier = document.getElementById('user-tier').value;
  if (!username || !apiKey) {
    setStatus('Username and API key are required', 'danger');
    return;
  }

  const response = await fetch('/admin/users', {
    method: 'POST',
    headers: authHeaders(true),
    body: JSON.stringify({ username, apiKey, defaultTier: tier })
  });

  if (!response.ok) {
    setStatus('Failed to save user', 'danger');
    return;
  }

  setStatus('User saved', 'success');
  loadUsers();
}

async function loadPermissions() {
  if (!ensureAuth()) {
    return;
  }

  const response = await fetch('/admin/permissions', {
    method: 'GET',
    headers: authHeaders(false)
  });
  if (!response.ok) {
    setStatus('Unable to load permissions', 'danger');
    return;
  }

  state.permissions = await response.json();
  populatePermissionsPayload();
}

function populatePermissionsPayload() {
  const endpoint = document.getElementById('permissions-endpoint').value;
  const assignments = state.permissions[endpoint] || {};
  document.getElementById('permissions-assignments').value = JSON.stringify(assignments, null, 2);
}

async function savePermissions() {
  if (!ensureAuth()) {
    return;
  }

  const endpoint = document.getElementById('permissions-endpoint').value;
  const json = document.getElementById('permissions-assignments').value.trim() || '{}';
  let parsed;
  try {
    parsed = JSON.parse(json);
  } catch (error) {
    setStatus('Permissions JSON is invalid', 'danger');
    return;
  }

  const response = await fetch(`/admin/permissions/${encodeURIComponent(endpoint)}`, {
    method: 'POST',
    headers: authHeaders(true),
    body: JSON.stringify({ assignments: parsed })
  });

  if (!response.ok) {
    setStatus('Failed to save permissions', 'danger');
    return;
  }

  setStatus('Permissions updated', 'success');
  loadPermissions();
}

async function handleLogin(event) {
  event.preventDefault();
  const apiKey = document.getElementById('login-api-key').value.trim();
  if (!apiKey) {
    setStatus('API key is required', 'danger');
    return;
  }

  state.apiKey = apiKey;
  const auth = await rebuildAccessForKey(apiKey);
  if (!auth.ok) {
    setStatus(auth.message || 'No endpoints accept that API key', 'danger');
    state.apiKey = null;
    return;
  }
  await persistSessionLogin(apiKey);
  selectors.loginOverlay.classList.add('hidden');
  setStatus(`Welcome ${state.user} (${state.tier})`, 'success');
  renderProjectsList();
  updateEntityProjectSelect();
  updatePermissionsEndpointSelect();
  renderConfigInstructions();
  loadOverview();
  loadAdminData();
}

function logout() {
  state.apiKey = null;
  state.user = null;
  state.tier = null;
  state.allowedEndpoints = [];
  selectors.loginOverlay.classList.remove('hidden');
  setStatus('Logged out', 'info');
  fetch('/admin/logout', { method: 'POST', credentials: 'same-origin' }).catch(() => {});
}

async function rebuildAccessForKey(apiKey) {
  if (!state.endpoints.length) {
    await fetchEndpoints();
  }

  if (!state.endpoints.length) {
    return { ok: false, message: 'No endpoints available to validate the key' };
  }

  setStatus('Validating API key...', 'info');
  const accessible = [];
  let payload;

  for (const endpoint of state.endpoints) {
    try {
      const response = await fetch(`/mcp/${endpoint.key}/ping`, {
        method: 'POST',
        headers: authHeaders(false, apiKey)
      });

      if (!response.ok) {
        continue;
      }

      const data = await response.json();
      accessible.push(endpoint);
      if (!payload) {
        payload = data;
      }
    } catch (error) {
      console.error('Ping failed', endpoint.key, error);
    }
  }

  if (!accessible.length) {
    return { ok: false, message: 'No endpoints accept that API key' };
  }

  state.allowedEndpoints = accessible;
  state.user = payload?.user ?? 'unknown';
  state.tier = payload?.tier ?? 'Reader';
  setStatus(`Welcome ${state.user} (${state.tier})`, 'success');
  return { ok: true };
}

async function refreshAuthState() {
  if (!state.apiKey) {
    await fetchEndpoints();
    renderProjectsList();
    return;
  }

  await fetchEndpoints();
  const result = await rebuildAccessForKey(state.apiKey);
  if (!result.ok) {
    setStatus(result.message || 'Unable to refresh access', 'danger');
    return;
  }
  renderProjectsList();
  updateEntityProjectSelect();
  updatePermissionsEndpointSelect();
  renderConfigInstructions();
}

async function persistSessionLogin(apiKey) {
  try {
    await fetch('/admin/login', {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ apiKey })
    });
  } catch (error) {
    console.warn('Session login persist failed', error);
  }
}

async function tryResumeSession() {
  try {
    const response = await fetch('/admin/session', { credentials: 'same-origin' });
    if (!response.ok || response.status === 204) {
      return;
    }
    const data = await response.json();
    if (!data.apiKey) {
      return;
    }

    state.apiKey = data.apiKey;
    const auth = await rebuildAccessForKey(state.apiKey);
    if (!auth.ok) {
      state.apiKey = null;
      return;
    }

    selectors.loginOverlay.classList.add('hidden');
    setStatus(`Welcome back ${state.user} (${state.tier})`, 'success');
    renderProjectsList();
    updateEntityProjectSelect();
    updatePermissionsEndpointSelect();
    renderConfigInstructions();
    loadOverview();
    loadAdminData();
  } catch (error) {
    console.warn('Session resume failed', error);
  }
}

async function loadOverview() {
  try {
    const response = await fetch('/health');
    if (!response.ok) {
      setStatus('Unable to read health report', 'danger');
      return;
    }

    const report = await response.json();
    renderHealthReport(report);
    renderSummary(report);
  } catch (error) {
    console.error(error);
    setStatus('Health endpoint unreachable', 'danger');
  }
}

function renderSummary(report) {
  const summary = document.getElementById('overview-summary');
  summary.innerHTML = `
    <div class="d-flex justify-content-between">
      <div>
        <h5 class="mb-0">${escapeHtml(report.status)}</h5>
        <p class="text-muted small mb-1">${escapeHtml(report.timestampUtc ?? '')}</p>
      </div>
      <a class="btn btn-sm btn-outline-secondary" href="/metrics" target="_blank">View metrics</a>
    </div>
    <p class="small text-muted mb-0">Running for ${escapeHtml(report.uptime ?? 'n/a')}</p>
  `;
}

function renderHealthReport(report) {
  const container = document.getElementById('overview-health');
  container.innerHTML = `
    <dt class="col-sm-4">Total entries</dt>
    <dd class="col-sm-8">${report.totalEntries}</dd>
    <dt class="col-sm-4">Total bytes</dt>
    <dd class="col-sm-8">${report.totalBytes}</dd>
    <dt class="col-sm-4">Issues</dt>
    <dd class="col-sm-8">${(report.issues || []).length ? report.issues.join(', ') : 'None'}</dd>
  `;

  const storesContainer = document.getElementById('overview-stores');
  storesContainer.innerHTML = (report.stores || [])
    .map((store) => `
      <div class="border p-2 rounded bg-white">
        <strong>${escapeHtml(store.project)}</strong>
        <div class="text-muted small">Entries: ${store.entryCount}</div>
        <div class="text-muted small">Updated: ${store.fileLastUpdatedUtc ? formatDate(store.fileLastUpdatedUtc) : 'n/a'}</div>
      </div>
    `)
    .join('');
}

function setStatus(message, variant = 'info') {
  selectors.status.textContent = message;
  selectors.status.className = `status-bar ${variant}`;
}

function promptLogin(message) {
  selectors.loginOverlay.classList.remove('hidden');
  setStatus(message, 'danger');
}

function ensureAuth() {
  if (!state.apiKey) {
    promptLogin('Authentication required.');
    return false;
  }

  return true;
}

function authHeaders(json = false, overrideKey = state.apiKey) {
  const headers = new Headers();
  if (json) {
    headers.set('Content-Type', 'application/json');
  }
  if (overrideKey) {
    headers.set('X-Api-Key', overrideKey);
  }
  return headers;
}

function formatDate(value) {
  if (!value) {
    return 'never';
  }
  return new Date(value).toLocaleString();
}

function escapeHtml(value) {
  if (value === null || value === undefined) {
    return '';
  }
  return value
    .toString()
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

function normalizeStoragePath(value) {
  if (!value) {
    return '';
  }

  const trimmed = value.trim();
  return trimmed.replace(/\\\\/g, '\\');
}

function formatStoragePath(value) {
  return normalizeStoragePath(value);
}

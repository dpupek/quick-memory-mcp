const state = {
  apiKey: null,
  user: null,
  tier: null,
  endpoints: [],
  allowedEndpoints: [],
  entries: [],
  selectedEntryIds: new Set(),
  permissions: {},
  users: [],
  activeProjectKey: null,
  selectedProjects: new Set(),
  projectFilter: '',
  lastDetailEntry: null,
  configEditor: null,
  monacoReady: null,
  lastConfigText: null,
  monacoEditors: {},
  backupActivity: [],
  backupBusy: {
    loading: false,
    saving: false,
    probing: false,
    running: false
  },
  backupRefreshTimer: null,
  backupLoaded: false
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

const TIER_OPTIONS = ['Reader', 'Editor', 'Curator', 'Admin'];

const selectors = {
  loginOverlay: document.getElementById('login-overlay'),
  loginForm: document.getElementById('login-form'),
  status: document.getElementById('app-status'),
  navButtons: Array.from(document.querySelectorAll('[data-tab]')),
  tabs: Array.from(document.querySelectorAll('.tab-pane'))
};

const ICONS = {
  overview: 'bi-speedometer2',
  projects: 'bi-folder2',
  entities: 'bi-journal-text',
  users: 'bi-people',
  config: 'bi-gear',
  backup: 'bi-cloud-arrow-down',
  health: 'bi-heart-pulse',
  help: 'bi-book',
  'agent-help': 'bi-robot',
  'admin-ui-help': 'bi-ui-checks'
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

  const projectSelect = document.getElementById('entry-project');
  if (projectSelect && state.allowedEndpoints.length) {
    projectSelect.value = state.allowedEndpoints[0].key;
  }

  document.getElementById('entry-kind').value = 'note';
  document.getElementById('entry-tier').value = 'provisional';
  document.getElementById('entry-confidence').value = '0.5';
  enhanceTagsInput('entry-tags');
  clearTagsInput('entry-tags');
  document.getElementById('entry-body').value = '';
  mountMonacoField('entryBodyEditor', 'entry-body-editor', 'entry-body', '');
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

const views = {
  overview: {
    id: 'overview',
    init() {
      // no-op for now; kept for future hooks
    },
    onShow() {
      loadOverview();
      renderVersion();
    }
  },
  projects: {
    id: 'projects',
    init() {
      const projectsList = document.getElementById('projects-list');
      if (projectsList) {
        projectsList.addEventListener('click', handleProjectButton);
      }
      const createButton = document.getElementById('project-create');
      if (createButton) {
        createButton.addEventListener('click', createProject);
      }
      const filter = document.getElementById('project-permission-filter');
      if (filter) {
        filter.addEventListener('input', handleProjectFilterInput);
      }
      const permissionsList = document.getElementById('project-permissions-list');
      if (permissionsList) {
        permissionsList.addEventListener('click', handleProjectPermissionsListClick);
        permissionsList.addEventListener('change', handleProjectSelectionChange);
      }
      const savePermissionsButton = document.getElementById('project-permissions-save');
      if (savePermissionsButton) {
        savePermissionsButton.addEventListener('click', saveProjectPermissions);
      }
      const bulkApplyButton = document.getElementById('project-bulk-apply');
      if (bulkApplyButton) {
        bulkApplyButton.addEventListener('click', applyBulkPermissions);
      }
    },
    onShow() {
      refreshAuthState();
    }
  },
  entities: {
    id: 'entities',
    init() {
      const refreshButton = document.getElementById('entity-refresh');
      if (refreshButton) {
        refreshButton.addEventListener('click', () => loadEntities());
      }
      const tableBody = document.getElementById('entities-body');
      if (tableBody) {
        tableBody.addEventListener('click', handleEntityActions);
        tableBody.addEventListener('change', handleEntitySelectionChange);
      }
      const detailPanel = document.getElementById('entity-detail');
      if (detailPanel) {
        detailPanel.addEventListener('click', handleEntryDetailActions);
      }
      const projectSelect = document.getElementById('entity-project');
      if (projectSelect) {
        projectSelect.addEventListener('change', () => loadEntities());
      }
      const openEntryModal = document.getElementById('open-entry-modal');
      if (openEntryModal) {
        openEntryModal.addEventListener('click', showEntryModal);
      }
      const entryForm = document.getElementById('entry-form');
      if (entryForm) {
        entryForm.addEventListener('submit', handleEntryFormSubmit);
      }
      const entryModalClose = document.getElementById('entry-modal-close');
      if (entryModalClose) {
        entryModalClose.addEventListener('click', closeEntryModal);
      }
      const entryModalCancel = document.getElementById('entry-modal-cancel');
      if (entryModalCancel) {
        entryModalCancel.addEventListener('click', closeEntryModal);
      }
      const importRun = document.getElementById('import-run');
      if (importRun) {
        importRun.addEventListener('click', runImport);
      }

      const selectAll = document.getElementById('entities-select-all');
      if (selectAll) {
        selectAll.addEventListener('change', handleEntitiesSelectAll);
      }
      const bulkDelete = document.getElementById('entities-bulk-delete');
      if (bulkDelete) {
        bulkDelete.addEventListener('click', bulkDeleteEntries);
      }
    },
    onShow() {
      loadEntities();
    }
  },
  users: {
    id: 'users',
    init() {
      const saveUserButton = document.getElementById('user-save');
      if (saveUserButton) {
        saveUserButton.addEventListener('click', saveUser);
      }
      const userForm = document.getElementById('user-form');
      if (userForm) {
        userForm.addEventListener('submit', (event) => event.preventDefault());
      }
    },
    onShow() {
      loadAdminData();
    }
  },
  config: {
    id: 'config',
    init() {
      const reload = document.getElementById('config-reload');
      if (reload) {
        reload.addEventListener('click', () => loadConfig());
      }
      const validate = document.getElementById('config-validate');
      if (validate) {
        validate.addEventListener('click', () => validateConfig(false));
      }
      const save = document.getElementById('config-save');
      if (save) {
        save.addEventListener('click', () => validateConfig(true));
      }
    },
    onShow() {
      loadConfig();
    }
  },
  backup: {
    id: 'backup',
    init() {
    },
    onShow() {
      ensureBackupLoaded();
    }
  },
  health: {
    id: 'health',
    init() {
      const refresh = document.getElementById('health-refresh');
      if (refresh) {
        refresh.addEventListener('click', loadHealthBlade);
      }
      const downloadLogsButton = document.getElementById('health-download-logs');
      if (downloadLogsButton) {
        downloadLogsButton.addEventListener('click', downloadLogs);
      }
    },
    onShow() {
      loadHealthBlade();
    }
  },
  help: {
    id: 'help',
    init() {},
    onShow() {
      loadHelpContent();
    }
  },
  'agent-help': {
    id: 'agent-help',
    init() {},
    onShow() {
      loadAgentHelp();
    }
  },
  'admin-ui-help': {
    id: 'admin-ui-help',
    init() {},
    onShow() {
      loadAdminUiHelp();
    }
  },
  'codex-workspace': {
    id: 'codex-workspace',
    init() {},
    onShow() {
      loadCodexWorkspaceHelp();
    }
  }
};

function initViews() {
  Object.values(views).forEach((view) => {
    if (typeof view.init === 'function') {
      view.init();
    }
  });
}

document.addEventListener('DOMContentLoaded', () => {
  setupNavigation();
  selectors.loginForm.addEventListener('submit', handleLogin);
  document.getElementById('logout-btn').addEventListener('click', logout);
  initViews();
  // Backdrop close disabled to prevent accidental dismiss; use close/cancel buttons instead
  // document.getElementById('entry-modal').addEventListener('click', (event) => { ... });
  renderRelationsControl(document.getElementById('entry-relations'), []);
  renderSourceControl(document.getElementById('entry-source'), null);
  document.getElementById('entry-body-mode')?.addEventListener('change', (event) => {
    const mode = event.target.value === 'text' ? 'plaintext' : 'json';
    mountMonacoField('entryBodyEditor', 'entry-body-editor', 'entry-body', readMonacoField('entryBodyEditor', 'entry-body'), mode);
  });
  stopModalClickBubble();
  selectors.loginOverlay.classList.remove('hidden');
  (async () => {
    await fetchEndpoints();
    await tryResumeSession();
  })();
});

window.addEventListener('resize', () => {
  Object.values(state.monacoEditors).forEach((editor) => editor.layout());
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
  const view = views[tab];
  if (view && typeof view.onShow === 'function') {
    view.onShow();
  }
}

async function ensureBackupLoaded() {
  if (state.backupLoaded) {
    loadBackupSettings();
    loadBackupActivity();
    startBackupAutoRefresh();
    return;
  }

  try {
    const host = document.getElementById('backup-host');
    if (!host) return;
    const res = await fetch('/fragments/backup.html', { credentials: 'same-origin' });
    if (!res.ok) {
      setStatus('Failed to load backup blade (auth?)', 'danger');
      return;
    }
    host.innerHTML = await res.text();
    wireBackupHandlers();
    state.backupLoaded = true;
    loadBackupSettings();
    loadBackupActivity();
    startBackupAutoRefresh();
  } catch (err) {
    console.error('backup load failed', err);
    setStatus('Failed to load backup blade', 'danger');
  }
}

function wireBackupHandlers() {
  document.getElementById('backup-save')?.addEventListener('click', saveBackupSettings);
  document.getElementById('backup-probe')?.addEventListener('click', probeBackupTarget);
  document.getElementById('backup-activity-refresh')?.addEventListener('click', loadBackupActivity);
  document.getElementById('backup-activity-filter')?.addEventListener('input', () => renderBackupActivity(state.backupActivity || []));
  document.getElementById('backup-run')?.addEventListener('click', runManualBackup);
  document.getElementById('backup-auto-refresh')?.addEventListener('change', toggleBackupAutoRefresh);
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
  confirmDeleteProject(endpoint);
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
      confirmDeleteEntry(entryId, () => deleteEntry(entryId));
    }
  }
}

function handleEntitySelectionChange(event) {
  const checkbox = event.target.closest('.entity-select');
  if (!checkbox) {
    return;
  }

  const entryId = checkbox.dataset.entryId;
  if (!entryId) {
    return;
  }

  if (checkbox.checked) {
    state.selectedEntryIds.add(entryId);
  } else {
    state.selectedEntryIds.delete(entryId);
  }

  syncEntitiesSelectAllCheckbox();
  updateEntitiesSelectionSummary();
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
    renderProjectPermissionsList();
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
  const slugInput = document.getElementById('project-slug').value.trim();
  const description = document.getElementById('project-description').value.trim();
  const storageInput = normalizeStoragePath(document.getElementById('project-storage').value);
  const includeInSearch = document.getElementById('project-include-shared').checked;
  const inheritShared = document.getElementById('project-inherit-shared').checked;

  if (!key || !name) {
    setStatus('Project key and name are required', 'danger');
    return;
  }

  const slug = slugInput || key;
  const storagePath = storageInput || `MemoryStores/${key}`;

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
  const importSelect = document.getElementById('import-project');
  if (!select) {
    return;
  }

  if (!state.allowedEndpoints.length) {
    select.innerHTML = '<option value="">Select a project</option>';
    if (entrySelect) {
      entrySelect.innerHTML = '<option value="">Select a project</option>';
    }
    if (importSelect) {
      importSelect.innerHTML = '<option value=\"\">Select a project</option>';
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

  if (importSelect) {
    importSelect.innerHTML = options;
    importSelect.value = state.allowedEndpoints[0].key;
  }
}

function updatePermissionsEndpointSelect() {
  const select = document.getElementById('project-permission-filter');
  if (!select) return;
  // used only for filtering text input; ensure list renders with latest endpoints
  renderProjectPermissionsList();
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
    if (applyChanges) {
      state.lastConfigText = content;
    }
    setConfigStatus(applyChanges ? 'Saved successfully' : 'Valid TOML', 'success');
    renderConfigDiff();
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

async function loadCodexWorkspaceHelp() {
  if (!ensureAuth()) {
    return;
  }

  const response = await fetch('/admin/help/codex-workspace', { headers: authHeaders(false) });
  if (!response.ok) {
    setStatus('Unable to load Codex workspace guide', 'danger');
    return;
  }

  const html = await response.text();
  document.getElementById('codex-workspace-help-content').innerHTML = html;
}

function renderEntryDetail(entry) {
  state.lastDetailEntry = entry;
  const bodyValue = entry.body
    ? typeof entry.body === 'string'
      ? entry.body
      : JSON.stringify(entry.body, null, 2)
    : '';

  const container = document.getElementById('entity-detail');
  const updated = entry.timestamps?.updatedUtc ? formatDate(entry.timestamps.updatedUtc) : 'never';
  const isPromptsRepo = entry.project === 'prompts-repository';
  container.innerHTML = `
    <div class="d-flex justify-content-between">
      <div>
        <h4 class="mb-1">${escapeHtml(entry.title || entry.id)}</h4>
        <p class="text-muted small mb-0">
          ${escapeHtml(entry.kind)} • Project: ${escapeHtml(entry.project)} • Updated: ${updated}
        </p>
      </div>
    </div>
    ${isPromptsRepo ? `
    <div class="alert alert-warning mt-2 mb-0">
      <strong>System prompts:</strong> entries in <code>prompts-repository</code> back MCP prompt templates.
      Avoid deleting them; edit carefully and keep <code>kind = \"prompt\"</code>, the <code>prompt-template</code> tag,
      and any <code>prompt-args</code> blocks valid.
    </div>` : ''}
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
            id="detail-tags"
            name="tags"
            class="form-control"
            value="${(entry.tags || []).join(', ')}"
          />
        </div>
        <div class="col-md-6">
          <div class="d-flex justify-content-between align-items-center">
            <label class="form-label mb-0">Body</label>
            <select id="detail-body-mode" class="form-select form-select-sm body-mode-select">
              <option value="json" selected>JSON</option>
              <option value="text">Plain text</option>
            </select>
          </div>
          <div id="detail-body-editor" class="monaco-field"></div>
          <textarea id="detail-body" name="body" class="form-control d-none" rows="4">${escapeHtml(bodyValue)}</textarea>
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
          <label class="form-label">Relations</label>
          <div id="detail-relations" class="relations-editor"></div>
        </div>
        <div class="col-md-6">
          <label class="form-label">Source metadata</label>
          <div id="detail-source" class="source-editor"></div>
        </div>
      </div>
      <div class="mt-3 d-flex gap-2">
        <button type="button" class="btn btn-success" data-action="update-entry">Save entry</button>
        ${isPromptsRepo
          ? '<button type="button" class="btn btn-outline-danger" disabled title="Prompts in prompts-repository cannot be hard-deleted; retire or edit them instead.">Delete entry</button>'
          : `<button type="button" class="btn btn-outline-danger" data-action="delete-entry" data-entry-id="${entry.id}">Delete entry</button>`}
      </div>
    </form>
  `;

  renderRelationsControl(document.getElementById('detail-relations'), entry.relations || []);
  renderSourceControl(document.getElementById('detail-source'), entry.source || null);
  enhanceTagsInput('detail-tags');
  mountMonacoField('detailBodyEditor', 'detail-body-editor', 'detail-body', bodyValue);

  const modeSelect = document.getElementById('detail-body-mode');
  if (modeSelect) {
    modeSelect.addEventListener('change', (event) => {
      const mode = event.target.value === 'text' ? 'plaintext' : 'json';
      mountMonacoField('detailBodyEditor', 'detail-body-editor', 'detail-body', readMonacoField('detailBodyEditor', 'detail-body'), mode);
    });
  }
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
  const bodyText = readMonacoField('detailBodyEditor', 'detail-body').trim();
  const kind = form.querySelector('[name="kind"]').value.trim();
  const confidenceValue = parseFloat(form.querySelector('[name="confidence"]').value);
  const isPermanent = form.querySelector('[name="isPermanent"]').checked;
  const pinned = form.querySelector('[name="pinned"]').checked;
  const epicSlug = form.querySelector('[name="epicSlug"]').value.trim();
  const epicCase = form.querySelector('[name="epicCase"]').value.trim();
  const relationsRoot = document.getElementById('detail-relations');
  const sourceRoot = document.getElementById('detail-source');

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

  const nextRelations = collectRelations(relationsRoot);
  const prevRelations = state.lastDetailEntry.relations || [];
  if (JSON.stringify(nextRelations) !== JSON.stringify(prevRelations)) {
    payload.relations = nextRelations;
  }

  const nextSource = collectSource(sourceRoot);
  const prevSource = state.lastDetailEntry.source || null;
  if (JSON.stringify(nextSource ?? null) !== JSON.stringify(prevSource ?? null)) {
    payload.source = nextSource ?? {};
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
    state.selectedEntryIds.clear();
    body.innerHTML = '<tr><td colspan="7" class="text-muted">No entries found.</td></tr>';
    updateEntitiesSelectionSummary();
    return;
  }

  state.selectedEntryIds.clear();

  body.innerHTML = state.entries
    .map((result) => {
      const entry = result.entry;
      const updated = entry.timestamps?.updatedUtc ? formatDate(entry.timestamps.updatedUtc) : 'never';
      return `
        <tr>
          <td><input type="checkbox" class="entity-select form-check-input" data-entry-id="${escapeHtml(entry.id)}" /></td>
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
  const tags = getTagValues('entry-tags');
  const rawBody = readMonacoField('entryBodyEditor', 'entry-body').trim();
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

  if (project === 'prompts-repository') {
    setStatus('Prompts in prompts-repository cannot be hard-deleted; retire or edit them instead.', 'warning');
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
  renderProjectPermissionsPanels();
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

  state.users = await response.json();
  renderUsersTable();
  renderProjectPermissionsPanels();
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
  renderProjectPermissionsPanels();
}

function renderUsersTable() {
  const tbody = document.querySelector('#users-table tbody');
  if (!tbody) {
    return;
  }

  if (!state.users.length) {
    tbody.innerHTML = '<tr><td colspan="4" class="text-center text-muted">No users configured.</td></tr>';
    return;
  }

  tbody.innerHTML = state.users
    .map((user) => {
      return `
        <tr>
          <td>${escapeHtml(user.username)}</td>
          <td>${escapeHtml(user.defaultTier)}</td>
          <td><code class="text-break">${escapeHtml(user.apiKey)}</code></td>
          <td><button class="btn btn-sm btn-danger" onclick="removeUser('${user.username}')">Delete</button></td>
        </tr>`;
    })
    .join('');
}

function renderProjectPermissionsPanels() {
  renderProjectPermissionsList();
  renderProjectPermissionsDetail();
  renderProjectPermissionsBulkControls();
}

function renderProjectPermissionsList() {
  const list = document.getElementById('project-permissions-list');
  if (!list) {
    return;
  }

  if (!state.endpoints.length) {
    list.innerHTML = '<div class="text-muted small">No projects configured.</div>';
    state.selectedProjects.clear();
    state.activeProjectKey = null;
    updateProjectSelectionCount();
    return;
  }

  const filter = (state.projectFilter || '').toLowerCase();
  const filtered = state.endpoints.filter((endpoint) => {
    if (!filter) {
      return true;
    }
    const haystack = `${endpoint.key} ${endpoint.name ?? ''} ${endpoint.slug ?? ''}`.toLowerCase();
    return haystack.includes(filter);
  });

  const validKeys = new Set(filtered.map((endpoint) => endpoint.key));
  state.selectedProjects = new Set([...state.selectedProjects].filter((key) => validKeys.has(key)));

  if (!state.activeProjectKey || !validKeys.has(state.activeProjectKey)) {
    state.activeProjectKey = filtered.length ? filtered[0].key : null;
  }

  if (state.activeProjectKey) {
    state.selectedProjects.add(state.activeProjectKey);
  }

  if (!filtered.length) {
    list.innerHTML = '<div class="text-muted small">No projects match that filter.</div>';
    updateProjectSelectionCount();
    return;
  }

  list.innerHTML = filtered
    .map((endpoint) => {
      const inheritLabel = endpoint.inheritShared ? 'Inherits shared memory' : 'Isolated from shared memory';
      const isActive = endpoint.key === state.activeProjectKey ? 'active' : '';
      const checked = state.selectedProjects.has(endpoint.key) ? 'checked' : '';
      return `
        <div class="list-group-item d-flex justify-content-between align-items-start ${isActive}" data-project-key="${endpoint.key}">
          <div class="flex-grow-1 pe-2" data-project-key="${endpoint.key}">
            <strong>${escapeHtml(endpoint.name || endpoint.key)}</strong>
            <div class="text-muted small">Key: ${escapeHtml(endpoint.key)} • ${inheritLabel}</div>
          </div>
          <div class="form-check mb-0">
            <input class="form-check-input" type="checkbox" data-project-select="${endpoint.key}" ${checked} />
          </div>
        </div>`;
    })
    .join('');

  updateProjectSelectionCount();
}

function renderProjectPermissionsDetail() {
  const container = document.getElementById('project-permissions-detail');
  const saveButton = document.getElementById('project-permissions-save');
  if (!container || !saveButton) {
    return;
  }

  if (!state.activeProjectKey) {
    container.innerHTML = '<p class="text-muted mb-0">Select a project to view overrides.</p>';
    saveButton.disabled = true;
    return;
  }

  const project = state.endpoints.find((endpoint) => endpoint.key === state.activeProjectKey);
  if (!project) {
    container.innerHTML = '<p class="text-muted mb-0">Project not found.</p>';
    saveButton.disabled = true;
    return;
  }

  if (!state.users.length) {
    container.innerHTML = '<p class="text-muted mb-0">Add a user before configuring project permissions.</p>';
    saveButton.disabled = true;
    return;
  }

  const overrides = state.permissions[state.activeProjectKey] || {};
  let hasAdmin = false;

  const rows = state.users
    .map((user) => {
      const override = overrides[user.username] || '';
      const effective = override || user.defaultTier;
      if ((effective || '').toLowerCase() === 'admin') {
        hasAdmin = true;
      }
      const badge = override ? '<span class="badge bg-info ms-2">Override</span>' : '';
      return `
        <tr data-permission-row>
          <td>${escapeHtml(user.username)}</td>
          <td>${escapeHtml(user.defaultTier)}</td>
          <td>
            <select class="form-select form-select-sm" data-user-tier="${escapeHtml(user.username)}" data-default-tier="${escapeHtml(user.defaultTier)}">
              <option value="">Use default (${escapeHtml(user.defaultTier)})</option>
              ${TIER_OPTIONS.map((tier) => `<option value="${tier}" ${tier === override ? 'selected' : ''}>${tier}</option>`).join('')}
            </select>
          </td>
          <td>${escapeHtml(effective || user.defaultTier)}${badge}</td>
        </tr>`;
    })
    .join('');

  const inheritLabel = project.inheritShared ? 'Inherits shared memory' : 'Does not inherit shared memory';
  const warning = hasAdmin
    ? ''
    : '<div class="alert alert-warning mt-3 mb-0">This project currently has no Admin-tier access.</div>';

  container.innerHTML = `
    <div class="d-flex justify-content-between align-items-start">
      <div>
        <h5 class="mb-1">${escapeHtml(project.name || project.key)}</h5>
        <p class="text-muted small mb-0">Slug: ${escapeHtml(project.slug || project.key)} • ${inheritLabel}</p>
      </div>
    </div>
    <div class="table-responsive mt-3">
      <table class="table table-sm align-middle">
        <thead>
          <tr>
            <th>User</th>
            <th>Default tier</th>
            <th>Override</th>
            <th>Effective tier</th>
          </tr>
        </thead>
        <tbody>
          ${rows}
        </tbody>
      </table>
    </div>
    ${warning}
  `;

  saveButton.disabled = false;
}

function renderProjectPermissionsBulkControls() {
  const userSelect = document.getElementById('project-bulk-user');
  const applyButton = document.getElementById('project-bulk-apply');
  if (!userSelect || !applyButton) {
    return;
  }

  const currentValue = userSelect.value;
  if (!state.users.length) {
    userSelect.innerHTML = '<option value="">No users available</option>';
    userSelect.disabled = true;
  } else {
    userSelect.disabled = false;
    const options = ['<option value="">Select user</option>']
      .concat(state.users.map((user) => `<option value="${user.username}">${escapeHtml(user.username)}</option>`))
      .join('');
    userSelect.innerHTML = options;
    if (state.users.some((user) => user.username === currentValue)) {
      userSelect.value = currentValue;
    }
  }

  applyButton.disabled = !state.selectedProjects.size || !state.users.length;
}

function updateProjectSelectionCount() {
  const target = document.getElementById('project-selection-count');
  if (target) {
    target.textContent = `${state.selectedProjects.size} selected`;
  }
  const applyButton = document.getElementById('project-bulk-apply');
  if (applyButton) {
    applyButton.disabled = !state.selectedProjects.size || !state.users.length;
  }
}

function handleProjectPermissionsListClick(event) {
  const item = event.target.closest('[data-project-key]');
  if (!item) {
    return;
  }

  const key = item.dataset.projectKey;
  if (!key) {
    return;
  }

  state.activeProjectKey = key;
  state.selectedProjects.add(key);
  renderProjectPermissionsList();
  renderProjectPermissionsDetail();
  renderProjectPermissionsBulkControls();
}

function updateEntitiesSelectionSummary() {
  const summary = document.getElementById('entities-selection-summary');
  const bulkDelete = document.getElementById('entities-bulk-delete');
  const count = state.selectedEntryIds.size;

  if (summary) {
    summary.textContent = `${count} selected`;
  }

  if (bulkDelete) {
    bulkDelete.disabled = count === 0;
  }
}

function syncEntitiesSelectAllCheckbox() {
  const selectAll = document.getElementById('entities-select-all');
  if (!selectAll) {
    return;
  }

  const checkboxes = Array.from(document.querySelectorAll('#entities-body .entity-select'));
  if (!checkboxes.length) {
    selectAll.checked = false;
    selectAll.indeterminate = false;
    return;
  }

  const selectedCount = checkboxes.filter((cb) => cb.checked).length;
  selectAll.checked = selectedCount === checkboxes.length;
  selectAll.indeterminate = selectedCount > 0 && selectedCount < checkboxes.length;
}

function handleEntitiesSelectAll(event) {
  const checked = event.target.checked;
  const checkboxes = Array.from(document.querySelectorAll('#entities-body .entity-select'));

  state.selectedEntryIds.clear();
  checkboxes.forEach((cb) => {
    cb.checked = checked;
    const id = cb.dataset.entryId;
    if (checked && id) {
      state.selectedEntryIds.add(id);
    }
  });

  updateEntitiesSelectionSummary();
  syncEntitiesSelectAllCheckbox();
}

async function bulkDeleteEntries() {
  if (!ensureAuth()) {
    return;
  }

  const toDelete = Array.from(state.selectedEntryIds);
  if (!toDelete.length) {
    return;
  }

  const projectSelect = document.getElementById('entity-project');
  const project = (projectSelect && projectSelect.value) || state.allowedEndpoints[0]?.key;
  if (!project) {
    setStatus('No accessible project for bulk delete', 'danger');
    return;
  }

  const confirmMessage = `Delete ${toDelete.length} selected entr${toDelete.length === 1 ? 'y' : 'ies'} from project ${project}?`;
  if (window.Swal) {
    const result = await Swal.fire({
      title: 'Delete selected entries?',
      text: confirmMessage,
      icon: 'warning',
      showCancelButton: true,
      confirmButtonText: 'Yes, delete',
      cancelButtonText: 'Cancel',
      confirmButtonColor: '#d33'
    });
    if (!result.isConfirmed) {
      return;
    }
  } else if (!window.confirm(confirmMessage)) {
    return;
  }

  setStatus('Deleting selected entries...', 'info');

  let failures = 0;
  for (const id of toDelete) {
    try {
      const response = await fetch(`/mcp/${encodeURIComponent(project)}/entries/${encodeURIComponent(id)}?force=true`, {
        method: 'DELETE',
        headers: authHeaders(false)
      });

      if (!response.ok) {
        failures++;
      }
    } catch {
      failures++;
    }
  }

  if (failures > 0) {
    setStatus(`Deleted ${toDelete.length - failures} entries; ${failures} failed.`, 'warning');
  } else {
    setStatus(`Deleted ${toDelete.length} entries.`, 'info');
  }

  state.selectedEntryIds.clear();
  const selectAll = document.getElementById('entities-select-all');
  if (selectAll) {
    selectAll.checked = false;
    selectAll.indeterminate = false;
  }

  await loadEntities();
}

function handleProjectSelectionChange(event) {
  const checkbox = event.target.closest('input[data-project-select]');
  if (!checkbox) {
    return;
  }

  event.stopPropagation();
  const key = checkbox.dataset.projectSelect;
  if (!key) {
    return;
  }

  if (checkbox.checked) {
    state.selectedProjects.add(key);
  } else {
    state.selectedProjects.delete(key);
    if (state.activeProjectKey === key) {
      state.activeProjectKey = state.selectedProjects.values().next().value || null;
    }
  }

  updateProjectSelectionCount();
  renderProjectPermissionsDetail();
  renderProjectPermissionsBulkControls();
}

function handleProjectFilterInput(event) {
  state.projectFilter = event.target.value || '';
  renderProjectPermissionsList();
}

function setProjectPermissionsStatus(message, tone = 'muted') {
  const target = document.getElementById('project-permissions-status');
  if (!target) {
    return;
  }

  target.textContent = message || '';
  target.className = `small mt-2 text-${tone}`;
}

function setBulkPermissionsStatus(message, tone = 'muted') {
  const target = document.getElementById('project-bulk-status');
  if (!target) {
    return;
  }

  target.textContent = message || '';
  target.className = `small mt-2 text-${tone}`;
}

async function saveProjectPermissions() {
  if (!ensureAuth()) {
    return;
  }

  const projectKey = state.activeProjectKey;
  if (!projectKey) {
    return;
  }

  const detail = document.getElementById('project-permissions-detail');
  if (!detail) {
    return;
  }

  const rows = Array.from(detail.querySelectorAll('[data-permission-row]'));
  if (!rows.length) {
    return;
  }

  const assignments = {};
  let hasAdmin = false;

  rows.forEach((row) => {
    const select = row.querySelector('[data-user-tier]');
    if (!select) {
      return;
    }

    const user = select.dataset.userTier;
    const defaultTier = select.dataset.defaultTier;
    const value = select.value;
    if (value) {
      assignments[user] = value;
    }

    const effective = value || defaultTier;
    if ((effective || '').toLowerCase() === 'admin') {
      hasAdmin = true;
    }
  });

  if (!hasAdmin) {
    setProjectPermissionsStatus('Each project must retain at least one Admin-tier user.', 'danger');
    return;
  }

  setProjectPermissionsStatus('Saving...', 'info');

  const response = await fetch(`/admin/projects/${encodeURIComponent(projectKey)}/permissions`, {
    method: 'PATCH',
    headers: authHeaders(true),
    body: JSON.stringify({ assignments })
  });

  if (!response.ok) {
    const err = await response.text().catch(() => '');
    setProjectPermissionsStatus(`Failed to save permissions (${response.status}). ${err}`, 'danger');
    return;
  }

  setProjectPermissionsStatus('Permissions updated', 'success');
  await loadPermissions();
}

async function applyBulkPermissions() {
  if (!ensureAuth()) {
    return;
  }

  const projects = Array.from(state.selectedProjects);
  if (!projects.length) {
    setBulkPermissionsStatus('Select at least one project.', 'danger');
    return;
  }

  if (!state.users.length) {
    setBulkPermissionsStatus('Add a user before applying overrides.', 'danger');
    return;
  }

  const user = document.getElementById('project-bulk-user').value;
  const tier = document.getElementById('project-bulk-tier').value;

  if (!user) {
    setBulkPermissionsStatus('Choose a user to override.', 'danger');
    return;
  }

  const overridePatch = { [user]: tier || null };
  for (const key of projects) {
    if (!willProjectHaveAdmin(key, overridePatch)) {
      setBulkPermissionsStatus(`Applying this change would leave ${key} without an Admin.`, 'danger');
      return;
    }
  }

  setBulkPermissionsStatus('Applying overrides...', 'info');

  const response = await fetch('/admin/projects/permissions/bulk', {
    method: 'POST',
    headers: authHeaders(true),
    body: JSON.stringify({ projects, overrides: overridePatch })
  });

  if (!response.ok) {
    const err = await response.text().catch(() => '');
    setBulkPermissionsStatus(`Failed to apply overrides (${response.status}). ${err}`, 'danger');
    return;
  }

  setBulkPermissionsStatus('Overrides applied', 'success');
  await loadPermissions();
}

function willProjectHaveAdmin(projectKey, patch) {
  if (!state.users.length) {
    return true;
  }

  const overrides = { ...(state.permissions[projectKey] || {}) };
  if (patch) {
    Object.entries(patch).forEach(([user, tier]) => {
      if (!tier) {
        delete overrides[user];
      } else {
        overrides[user] = tier;
      }
    });
  }

  return state.users.some((user) => (
    (overrides[user.username] || user.defaultTier || '').toLowerCase() === 'admin'
  ));
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
  renderConfigInstructions();

  // Initialize import editor with Monaco in plain-text mode
  mountMonacoField('importContentEditor', 'import-content-editor', 'import-content', '');
  loadOverview();
  loadAdminData();
}

function logout() {
  state.apiKey = null;
  state.user = null;
  state.tier = null;
  state.allowedEndpoints = [];
  state.permissions = {};
  state.users = [];
  state.selectedProjects.clear();
  state.activeProjectKey = null;
  state.projectFilter = '';
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
    renderVersion(report);
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

function renderVersion(report) {
  const target = document.getElementById('overview-version');
  if (!target) return;
  const version = report?.version ?? 'unknown';
  const build = report?.buildDateUtc ? ` | Built: ${report.buildDateUtc}` : '';
  target.textContent = `Version: ${version}${build}`;
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
  if (variant === 'danger') { toast(message, 'error'); }
}

function formatApiError(err) {
  if (!err) return 'unknown error';
  if (err.error) return err.error;
  if (typeof err === 'string') return err;
  return JSON.stringify(err);
}

async function loadBackupSettings() {
  setBackupBusy('loading', true);
  try {
    const res = await apiGet('/admin/backup/settings');
    document.getElementById('backup-target').value = res.settings.targetPath || '';
    document.getElementById('backup-diff-cron').value = res.settings.differentialCron;
    document.getElementById('backup-full-cron').value = res.settings.fullCron;
    document.getElementById('backup-retention').value = res.settings.retentionDays;
    document.getElementById('backup-full-retention').value = res.settings.fullRetentionDays;
    renderNextRun(res.preview);
    refreshBackupHealthChip();
    await loadEndpointsForBackup();
  } catch (err) {
    setBackupStatus(`Failed to load settings: ${formatApiError(err)}`, true);
  } finally {
    setBackupBusy('loading', false);
  }
}

async function saveBackupSettings() {
  if (state.backupBusy.saving) return;
  setBackupBusy('saving', true);
  const payload = {
    differentialCron: document.getElementById('backup-diff-cron').value.trim(),
    fullCron: document.getElementById('backup-full-cron').value.trim(),
    retentionDays: parseInt(document.getElementById('backup-retention').value, 10),
    fullRetentionDays: parseInt(document.getElementById('backup-full-retention').value, 10),
    targetPath: document.getElementById('backup-target').value.trim() || null
  };

  try {
    const res = await apiPost('/admin/backup/settings', payload);
    renderNextRun(res.preview);
    setBackupStatus('Settings saved', false);
  } catch (err) {
    setBackupStatus(`Save failed: ${formatApiError(err)}`, true);
  } finally {
    setBackupBusy('saving', false);
  }
}

async function probeBackupTarget() {
  if (state.backupBusy.probing) return;
  setBackupBusy('probing', true);
  const target = document.getElementById('backup-target').value.trim();
  try {
    const res = await apiPost('/admin/backup/probe', { targetPath: target || null });
    setBackupStatus(`Probe OK for ${res.targetPath}`, false);
  } catch (err) {
    setBackupStatus(`Probe failed: ${formatApiError(err)}`, true);
  } finally {
    setBackupBusy('probing', false);
  }
}

async function loadBackupActivity() {
  try {
    const res = await apiGet('/admin/backup/activity?take=100');
    state.backupActivity = res.items || [];
    renderBackupActivity(state.backupActivity);
  } catch (err) {
    setBackupRunStatus(`Activity load failed: ${formatApiError(err)}`, true);
  }
}

async function loadEndpointsForBackup() {
  const select = document.getElementById('backup-run-endpoint');
  if (!select) return;
  select.innerHTML = '';
  try {
    const res = await apiGet('/admin/endpoints/manage');
    (res.endpoints || []).forEach((e) => {
      const opt = document.createElement('option');
      opt.value = e.key;
      opt.textContent = e.key;
      select.appendChild(opt);
    });
  } catch {
    // fall back to any endpoints already loaded in state (may be limited by permissions)
    (state.endpoints || []).forEach((e) => {
      const opt = document.createElement('option');
      opt.value = e.key;
      opt.textContent = e.key;
      select.appendChild(opt);
    });
    if (!select.options.length) {
      setBackupRunStatus('No endpoints available (admin key required)', true);
    }
  }
}

async function runManualBackup() {
  if (state.backupBusy.running) return;
  setBackupBusy('running', true);
  const endpoint = document.getElementById('backup-run-endpoint').value;
  const mode = document.getElementById('backup-run-mode').value;
  try {
    await apiPost('/admin/backup/run', { endpoint, mode });
    setBackupRunStatus(`Queued ${mode} backup for ${endpoint}`, false);
    loadBackupActivity();
  } catch (err) {
    setBackupRunStatus(`Run failed: ${formatApiError(err)}`, true);
  } finally {
    setBackupBusy('running', false);
  }
}

function renderNextRun(preview) {
  const el = document.getElementById('backup-next-run');
  if (!el) return;
  const diff = preview?.differentialNextUtc ? `Diff: ${preview.differentialNextUtc}` : 'Diff: n/a';
  const full = preview?.fullNextUtc ? `Full: ${preview.fullNextUtc}` : 'Full: n/a';
  el.textContent = `${diff} | ${full}`;
}

function renderBackupActivity(items) {
  const filter = document.getElementById('backup-activity-filter')?.value?.toLowerCase() || '';
  const body = document.getElementById('backup-activity-body');
  const empty = document.getElementById('backup-activity-empty');
  if (!body) return;
  body.innerHTML = '';
  const filtered = items.filter((i) => !filter || i.endpoint.toLowerCase().includes(filter));
  if (!filtered.length) {
    empty?.classList.remove('d-none');
    return;
  }
  empty?.classList.add('d-none');
  filtered.forEach((item) => {
    const tr = document.createElement('tr');
    tr.innerHTML = `<td>${item.timestampUtc}</td><td>${item.endpoint}</td><td>${item.mode}</td><td>${renderStatusChip(item.status)}</td><td>${item.durationMs?.toFixed?.(1) || ''} ms</td><td>${escapeHtml(item.message || '')}</td><td>${escapeHtml(item.initiatedBy || '')}</td>`;
    body.appendChild(tr);
  });
}

function renderStatusChip(status) {
  const map = { Success: 'bg-success', Failure: 'bg-danger', Skipped: 'bg-secondary' };
  const cls = map[status] || 'bg-secondary';
  return `<span class=\"badge ${cls}\">${status}</span>`;
}

function refreshBackupHealthChip() {
  const chip = document.getElementById('backup-health-chip');
  if (!chip) return;
  apiGet('/health')
    .then((res) => {
      const backupIssues = (res.issues || []).filter((i) => i.toLowerCase().includes('backup'));
      if (backupIssues.length) {
        chip.className = 'badge bg-danger';
        chip.textContent = 'Degraded';
      } else {
        chip.className = 'badge bg-success';
        chip.textContent = 'Healthy';
      }
    })
    .catch(() => {
      chip.className = 'badge bg-secondary';
      chip.textContent = 'Unknown';
    });
}

function setBackupStatus(text, isError) {
  const el = document.getElementById('backup-settings-status');
  if (!el) return;
  el.textContent = text;
  el.className = isError ? 'text-danger small' : 'text-success small';
}

function setBackupRunStatus(text, isError) {
  const el = document.getElementById('backup-run-status');
  if (!el) return;
  el.textContent = text;
  el.className = isError ? 'text-danger small' : 'text-success small';
}

function setBackupBusy(key, value) {
  state.backupBusy[key] = value;
  document.getElementById('backup-save')?.toggleAttribute('disabled', state.backupBusy.saving);
  document.getElementById('backup-probe')?.toggleAttribute('disabled', state.backupBusy.probing);
  document.getElementById('backup-run')?.toggleAttribute('disabled', state.backupBusy.running);
}

function startBackupAutoRefresh() {
  if (state.backupRefreshTimer) {
    clearInterval(state.backupRefreshTimer);
  }
  const auto = document.getElementById('backup-auto-refresh');
  if (auto && auto.checked) {
    state.backupRefreshTimer = setInterval(() => {
      loadBackupActivity();
      refreshBackupHealthChip();
    }, 15000);
  }
}

function toggleBackupAutoRefresh() {
  startBackupAutoRefresh();
}

function toast(message, icon = 'success') {
  if (!window.Swal) return;
  Swal.fire({
    toast: true,
    position: 'top-end',
    icon,
    title: message,
    showConfirmButton: false,
    timer: 2000,
    timerProgressBar: true
  });
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

function mountMonacoField(stateKey, containerId, fallbackId, value, language = 'json') {
  const fallback = document.getElementById(fallbackId);
  if (fallback) {
    fallback.value = value || '';
  }

  ensureMonaco()
    .then((monaco) => {
      const container = document.getElementById(containerId);
      if (!container) {
        return;
      }

      let editor = state.monacoEditors[stateKey];
      if (editor && typeof editor.dispose === 'function') {
        editor.dispose();
      }

      editor = monaco.editor.create(container, {
        value: value || '',
        language,
        theme: 'vs-light',
        minimap: { enabled: false },
        automaticLayout: true,
        wordWrap: 'on',
        scrollBeyondLastLine: false
      });
      state.monacoEditors[stateKey] = editor;
    })
    .catch((err) => console.error('monaco field failed', err));
}

function readMonacoField(stateKey, fallbackId) {
  const editor = state.monacoEditors[stateKey];
  if (editor) {
    return editor.getValue();
  }
  const fallback = document.getElementById(fallbackId);
  return fallback ? fallback.value : '';
}

async function runImport() {
  if (!ensureAuth()) {
    return;
  }

  const projectSelect = document.getElementById('import-project');
  const project = (projectSelect && projectSelect.value) || state.allowedEndpoints[0]?.key;
  if (!project) {
    setStatus('Select a project for import', 'danger');
    return;
  }

  const mode = document.getElementById('import-mode').value || 'upsert';
  const dryRun = document.getElementById('import-dryrun').checked;
  const content = readMonacoField('importContentEditor', 'import-content');

  if (!content || !content.trim()) {
    setStatus('Paste JSONL or JSON content to import', 'danger');
    return;
  }

  setStatus(`Running ${dryRun ? 'dry-run ' : ''}import...`, 'info');

  const url = `/admin/import/${encodeURIComponent(project)}?mode=${encodeURIComponent(mode)}&dryRun=${dryRun}`;
  const response = await fetch(url, {
    method: 'POST',
    headers: authHeaders(true),
    body: content
  });

  if (response.status === 401) {
    promptLogin('Insufficient privileges for admin operations.');
    return;
  }

  let payload;
  try {
    payload = await response.json();
  } catch {
    const text = await response.text().catch(() => '');
    setStatus(`Import failed (${response.status}). ${text}`, 'danger');
    return;
  }

  if (!response.ok) {
    setStatus(`Import failed: ${payload.error || 'unknown error'}`, 'danger');
    return;
  }

  const { processed = 0, imported = 0, skipped = 0, errorCount = 0 } = payload;
  const summary = `${dryRun ? 'Dry-run' : 'Import'} for ${project} (${mode}): processed ${processed}, imported ${imported}, skipped ${skipped}, errors ${errorCount}.`;

  setStatus(summary, errorCount ? 'warning' : 'success');

  if (!dryRun) {
    loadEntities();
  }
}

function enhanceTagsInput(id) {
  const el = document.getElementById(id);
  if (!el || !window.Choices) return el;
  if (el._choices) return el;
  el._choices = new Choices(el, {
    addChoices: true,
    removeItemButton: true,
    duplicateItemsAllowed: false,
    delimiter: ',',
    placeholder: true,
    placeholderValue: 'tags',
    shouldSort: false,
    shouldSortItems: false
  });
  return el;
}

function clearTagsInput(id) {
  const el = document.getElementById(id);
  if (!el) return;

  if (el._choices) {
    el._choices.removeActiveItems();
    el._choices.clearInput();
    return;
  }

  if (el.tagName === 'SELECT') {
    Array.from(el.options).forEach((option) => { option.selected = false; });
    return;
  }

  el.value = '';
}

function getTagValues(id) {
  const el = document.getElementById(id);
  if (!el) return [];
  if (el._choices) {
    return el._choices.getValue(true) || [];
  }
  return el.value.split(',').map((p) => p.trim()).filter(Boolean);
}

async function confirmDeleteEntry(entryId, onConfirm) {
  if (!window.Swal) { onConfirm(); return; }
  const result = await Swal.fire({
    title: 'Delete entry?',
    text: entryId,
    icon: 'warning',
    showCancelButton: true,
    confirmButtonText: 'Yes, delete',
    cancelButtonText: 'Cancel',
    confirmButtonColor: '#d33'
  });
  if (result.isConfirmed) { onConfirm(); }
}

async function confirmDeleteProject(endpoint) {
  if (!window.Swal) { deleteProject(endpoint); return; }
  const result = await Swal.fire({
    title: 'Delete project?',
    text: endpoint,
    icon: 'warning',
    showCancelButton: true,
    confirmButtonText: 'Yes, delete',
    cancelButtonText: 'Cancel',
    confirmButtonColor: '#d33'
  });
  if (result.isConfirmed) { deleteProject(endpoint); }
}

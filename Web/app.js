'use strict';
const $ = id => document.getElementById(id);
let state, selectedId = location.hash.slice(1), selectedBranch = '', busy = false, editingHash;
let selectedTab = 'history';

function node(tag, className, text) {
  const element = document.createElement(tag);
  if (className) element.className = className;
  if (text !== undefined) element.textContent = text;
  return element;
}
function notify(message, error = false) {
  $('notice').textContent = message;
  $('notice').classList.toggle('error', error);
  $('notice').hidden = false;
}
async function api(path, body) {
  const response = await fetch(path, body ? {
    method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Terragit-Token': state.token },
    body: JSON.stringify(body)
  } : { cache: 'no-store' });
  const data = await response.json();
  if (!response.ok) throw new Error(data.error || 'Request failed.');
  return data;
}
function currentRepo() { return state?.repositories.find(repo => repo.guid === selectedId); }
function worldEnabled() { return !busy && !state.restoreInProgress && selectedId === state.activeRepositoryId; }
async function refresh() {
  const next = await api('/api/state');
  const changed = JSON.stringify(next) !== JSON.stringify(state);
  state = next;
  if (!state.repositories.some(repo => repo.guid === selectedId)) {
    selectedId = state.repositories.find(repo => repo.guid === state.activeRepositoryId)?.guid || state.repositories[0]?.guid;
    selectedBranch = '';
  }
  if (changed) render();
}
async function action(kind, fields = {}) {
  if (busy) return;
  busy = true;
  const request = { action: kind, repoId: selectedId, branch: selectedBranch, ...fields };
  render();
  try {
    const result = await api('/api/action', request);
    if (kind === 'diff') {
      notify(result.matches ? 'No differences. The saved world matches this branch’s latest commit.'
        : 'Saved world: ' + result.currentSize.toLocaleString() + ' bytes · Snapshot: ' + result.commitSize.toLocaleString() + ' bytes\nSaved hash: ' + result.currentHash + '\nCommit hash: ' + result.commitHash);
    } else {
      notify(result.message);
      if (kind === 'commit') $('commit-message').value = '';
      if (kind === 'branch') $('branch-name').value = '';
    }
    await refresh();
    return true;
  } catch (error) { notify(error.message, true); return false; }
  finally { busy = false; render(); }
}
function selectRepo(id) {
  selectedId = id;
  selectedBranch = '';
  history.replaceState(null, '', '#' + id);
  $('commit-message').value = '';
  $('branch-name').value = '';
  $('rename').value = '';
  render();
}
function renderSidebar() {
  const query = $('search').value.toLowerCase();
  $('repo-count').textContent = state.repositories.length;
  const links = state.repositories.filter(repo => repo.name.toLowerCase().includes(query)).map(repo => {
    const button = node('button', 'repo-link' + (repo.guid === selectedId ? ' selected' : ''));
    button.append(node('span', 'repo-symbol', '▣'), node('span', '', repo.name));
    button.setAttribute('aria-current', repo.guid === selectedId ? 'page' : 'false');
    button.onclick = () => selectRepo(repo.guid);
    return button;
  });
  $('repo-list').replaceChildren(...(links.length ? links : [node('p', 'muted', 'No repositories found.')]));
}
function render() {
  if (!state) return;
  renderSidebar();
  $('world-status').textContent = state.restoreInProgress ? 'Restoring world…'
    : state.activeWorld ? state.activeWorld + ' · local single player' : 'Load a local single-player world to commit or restore snapshots.';
  if (state.restoreStatus) $('world-status').textContent += ' · ' + state.restoreStatus;
  $('init').hidden = !state.activeRepositoryId || state.repositories.some(repo => repo.guid === state.activeRepositoryId);
  $('init').disabled = busy || state.restoreInProgress;
  const repo = currentRepo();
  $('repository').hidden = !repo;
  $('empty').hidden = !!repo;
  if (!repo) return;
  if (!repo.branches.some(branch => branch.name === selectedBranch)) selectedBranch = repo.currentBranch;
  $('repo-name').textContent = repo.name;
  $('repo-owner').textContent = repo.name;
  $('repo-id').textContent = repo.guid;
  $('current-branch').textContent = repo.currentBranch;
  $('active-badge').hidden = repo.guid !== state.activeRepositoryId;
  $('commit-count').textContent = repo.commits.length;
  $('branch-count').textContent = repo.branches.length;
  $('branch-select').replaceChildren(...repo.branches.map(branch => {
    const option = node('option', '', branch.name);
    option.value = branch.name;
    option.selected = branch.name === selectedBranch;
    return option;
  }));
  $('branch-note').textContent = selectedBranch === repo.currentBranch ? 'current branch' : 'viewing branch';
  const canEdit = !busy && !state.restoreInProgress;
  const canWorld = worldEnabled();
  const head = repo.branches.find(branch => branch.name === selectedBranch)?.latestCommitHash;
  ['pull', 'checkout', 'diff'].forEach(id => $(id).disabled = !canWorld || !head);
  $('branch-select').disabled = !canEdit;
  $('world-hint').textContent = repo.guid !== state.activeRepositoryId
    ? 'You can manage this repository here. Load its world in tModLoader to commit, compare, or restore.'
    : 'Commit and compare read the saved file. Save in Terraria first. Restoring replaces that file and reloads the world.';
  $('commit-form').querySelectorAll('input, button').forEach(element => element.disabled = !canWorld);
  $('commit-form').querySelector('button').textContent = 'Commit to ' + repo.currentBranch;
  ['branch-form', 'rename-form'].forEach(id => $(id).querySelectorAll('input, button').forEach(element => element.disabled = !canEdit));
  if (document.activeElement !== $('rename') && !$('rename').value) $('rename').value = repo.name;
  renderCommits(repo, canWorld, canEdit);
  renderBranches(repo, canEdit);
  setTab(selectedTab);
}
function button(label, callback, disabled = false) {
  const element = node('button', '', label);
  element.type = 'button';
  element.disabled = disabled;
  element.onclick = callback;
  return element;
}
function renderCommits(repo, canWorld, canEdit) {
  const rows = [...repo.commits].reverse().map(commit => {
    const row = node('article', 'commit-row');
    const main = node('div', 'row-main');
    main.append(node('h3', 'commit-title', commit.message));
    const meta = node('div', 'commit-meta');
    meta.append(node('code', '', commit.commitHash.slice(0, 8)), node('span', '', commit.commitTimestamp));
    repo.branches.filter(branch => branch.latestCommitHash === commit.commitHash).forEach(branch => meta.append(node('span', 'badge', branch.name)));
    main.append(meta);
    const actions = node('div', 'row-actions');
    const download = node('a', '', 'Download');
    download.href = '/api/snapshot?repo=' + encodeURIComponent(repo.guid) + '&hash=' + encodeURIComponent(commit.commitHash);
    download.download = commit.commitHash + '.bin';
    actions.append(download, button('Edit message', () => {
      editingHash = commit.commitHash;
      $('edit-message').value = commit.message;
      $('message-dialog').showModal();
    }, !canEdit), button('Restore', () => confirmAction('Restore this snapshot?',
      'This replaces the loaded world file and reloads the world. A backup is created first. Mod-data sidecars are not rolled back. The branch head stays unchanged.',
      () => action('restore', { hash: commit.commitHash, repoId: repo.guid })), !canWorld));
    row.append(node('div', 'commit-avatar', '◷'), main, actions);
    return row;
  });
  $('commits').replaceChildren(...(rows.length ? rows : [node('div', 'blank-panel', 'No commits yet. Save your world, then make your first snapshot.')]));
}
function renderBranches(repo, canEdit) {
  $('branches-list').replaceChildren(...repo.branches.map(branch => {
    const row = node('div', 'branch-row'), main = node('div', 'row-main');
    main.append(node('h3', 'commit-title', branch.name), node('code', '', branch.latestCommitHash?.slice(0, 8) || 'No commits'));
    row.append(node('div', 'commit-avatar', '⑂'), main);
    if (branch.name === repo.currentBranch) row.append(node('span', 'badge', 'current'));
    else row.append(button('Delete branch', () => confirmAction('Delete ' + branch.name + '?',
      'The branch reference will be removed. Its commits and snapshot files will stay in this repository.',
      () => action('deleteBranch', { branch: branch.name, repoId: repo.guid })), !canEdit));
    return row;
  }));
}
function setTab(name) {
  selectedTab = name;
  ['history', 'branches', 'settings'].forEach(tab => {
    $(tab).hidden = tab !== name;
    $('tab-' + tab).setAttribute('aria-selected', String(tab === name));
  });
}
function confirmAction(title, text, callback) {
  const dialog = $('confirm-dialog');
  $('confirm-title').textContent = title;
  $('confirm-text').textContent = text;
  dialog.returnValue = 'cancel';
  dialog.onclose = () => { if (dialog.returnValue === 'confirm') callback(); };
  dialog.showModal();
}
$('refresh').onclick = () => refresh().catch(error => notify(error.message, true));
$('search').oninput = renderSidebar;
$('init').onclick = () => action('init');
$('branch-select').onchange = event => { selectedBranch = event.target.value; render(); };
$('diff').onclick = () => action('diff');
['pull', 'checkout'].forEach(kind => $(kind).onclick = () => {
  const repoId = selectedId, branch = selectedBranch;
  confirmAction(kind === 'checkout' ? 'Check out ' + branch + '?' : 'Pull snapshot from ' + branch + '?',
    'Your world will save, a backup will be created, and the branch snapshot will replace the world file. The world then reloads. Mod-data sidecars are not rolled back.',
    () => action(kind, { repoId, branch }));
});
$('commit-form').onsubmit = event => { event.preventDefault(); action('commit', { message: $('commit-message').value }); };
$('branch-form').onsubmit = event => { event.preventDefault(); action('branch', { name: $('branch-name').value }); };
$('rename-form').onsubmit = async event => { event.preventDefault(); if (await action('rename', { name: $('rename').value })) $('rename').value = currentRepo().name; };
$('message-form').onsubmit = async event => {
  event.preventDefault();
  $('message-dialog').close();
  await action('message', { hash: editingHash, message: $('edit-message').value });
};
$('cancel-message').onclick = () => $('message-dialog').close();
document.querySelectorAll('[data-tab]').forEach(tab => tab.onclick = () => setTab(tab.dataset.tab));
async function poll() {
  try { if (!busy && !document.querySelector('dialog[open]')) await refresh(); }
  catch (error) { notify('Cannot reach Terragit. Keep tModLoader running and return to its window if it is paused. ' + error.message, true); }
  finally { setTimeout(poll, 5000); }
}
poll();

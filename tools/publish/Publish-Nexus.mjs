// Restored from _build/nexus-current-changes-20261025/publish.mjs.
// First native-release file groups use the multipart flow and API schema in
// the Nexus upload action checkout; later versions use its unchanged dist/index.js.
import { readFile, writeFile, mkdir, rename, stat } from 'node:fs/promises';
import { spawn, spawnSync } from 'node:child_process';
import { homedir } from 'node:os';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const root = resolve(repo, '_build/nexus-official-sync');
const mappings = JSON.parse(await readFile(new URL('./nexus-mods.json', import.meta.url), 'utf8'));
const [mode = 'plan', ...requested] = process.argv.slice(2);
if (!['pages', 'plan', 'upload', 'verify'].includes(mode)) throw new Error('Use pages, plan, upload or verify, followed by Mod folders or all');
const selected = requested.length && !requested.includes('all')
  ? mappings.filter(item => requested.includes(item.folder)) : mappings;
if (!selected.length || requested.some(name => name !== 'all' && !mappings.some(item => item.folder === name)))
  throw new Error('Unknown Mod folder; mod-manager is excluded');
await mkdir(root, { recursive: true });

if (mode === 'pages') {
  const currentSteamState = resolve(repo, '.local/steam-workshop');
  const legacySteamState = resolve(repo, '.steam-workshop');
  const readState = path => stat(path).catch(error => {
    if (error.code !== 'ENOENT') throw error;
    return null;
  });
  const [currentState, legacyState] = await Promise.all([readState(currentSteamState), readState(legacySteamState)]);
  if (currentState && legacyState) throw new Error('Workshop state exists in both .local/steam-workshop and .steam-workshop');
  const steamState = legacyState ? legacySteamState : currentSteamState;
  const pages = [];
  for (const mapping of selected) {
    const source = resolve(repo, 'mods', mapping.folder);
    const manifest = await readFile(resolve(source, 'manifest.xml'), 'utf8');
    const version = /<ModVersion>([^<]+)<\/ModVersion>/.exec(manifest)?.[1];
    let summary = /<Description>([\s\S]*?)<\/Description>/.exec(manifest)?.[1].trim();
    if (!version || !summary) throw new Error(`Missing page source: ${mapping.folder}`);
    if (mapping.folder === 'nico-item-manager') summary = '正式版物品图鉴与管理器：中英搜索、生成、编辑及复制自有物品，支持液体、模组、数值与标签。Home 开关面板。\nFull-release item browser and manager with bilingual search, spawning, editing and copying owned items, plus liquids, modules, values and tags. Press Home to open.';
    let zh = `[b]当前正式版：${version} · 原生 ModLoader[/b]\n\n正式版请下载名称含 Full Release 的文件，将整个 Mod 文件夹放入游戏 Mods 目录，在主菜单 Mods 中启用并重启。`;
    let en = `[b]Current full release: ${version} · native ModLoader[/b]\n\nFor the full game, download the file named Full Release. Extract its Mod folder into Probably Stolen/Mods, enable it in the game's Mods menu, and restart.`;
    zh += '旧 Demo/MelonLoader 文件仅供 Demo 使用，不要与正式版原生包混装。';
    en += ' Demo/MelonLoader files are for the demo only. Do not mix them with the native release.';
    if (mapping.requiresForge) {
      zh += '\n需要单独安装[url=https://www.nexusmods.com/probablystolen/mods/270]正式版 NicoKobo Forge[/url]。';
      en += '\nRequires the full-release [url=https://www.nexusmods.com/probablystolen/mods/270]NicoKobo Forge[/url], installed separately.';
    }
    zh += '\n\n' + (await readFile(resolve(source, 'workshop-description.zh-CN.txt'), 'utf8')).replaceAll('订阅后', '安装后').replaceAll('请同时订阅并启用', '请同时安装并启用');
    en += '\n\n' + (await readFile(resolve(source, 'workshop-description.txt'), 'utf8')).replaceAll('After subscribing,', 'After installing,').replaceAll('Subscribe to and enable', 'Install and enable');
    let body = [zh, en].join('\n\n');
    body = body.replaceAll('https://steamcommunity.com/sharedfiles/filedetails/?id=3831025262', 'https://www.nexusmods.com/probablystolen/mods/270')
      .replace(/\[h([1-6])\]([\s\S]*?)\[\/h\1\]/g, (_, level, text) => `[size=${Math.max(3, 7 - Number(level))}][b]${text}[/b][/size]`)
      .replaceAll('[olist]', '[list=1]').replaceAll('[/olist]', '[/list]')
      .replace(/\[table\]([\s\S]*?)\[\/table\]/g, (_, table) => '[list]\n' + [...table.matchAll(/\[tr\]([\s\S]*?)\[\/tr\]/g)]
        .filter(row => !row[1].includes('[th]')).map(row => '[*]' + [...row[1].matchAll(/\[td\]([\s\S]*?)\[\/td\]/g)].map(cell => cell[1]).join(' — ')).join('\n') + '\n[/list]');
    const vdf = await readFile(resolve(steamState, `${mapping.manifestId}.vdf`), 'utf8');
    const steamId = /"publishedfileid"\s+"(\d+)"/.exec(vdf)?.[1];
    pages.push({ ...mapping, version, summary, body, steamId,
      preview: resolve(source, 'preview.png') });
  }
  await writeFile(resolve(root, 'ui-copy.json'), JSON.stringify(pages, null, 2) + '\n');
  console.log(JSON.stringify(pages.map(page => ({ folder: page.folder, version: page.version, name: page.name, steamId: page.steamId, bodyChars: page.body.length }))));
  process.exit(0);
}

// The retired website loader used this same CurrentUser DPAPI file. Only the
// Nexus key is used; decrypted configuration is never written or logged.
const decrypt = "$ErrorActionPreference='Stop';Add-Type -AssemblyName System.Security;$bytes=[Convert]::FromBase64String([Console]::In.ReadToEnd());$out=[Security.Cryptography.ProtectedData]::Unprotect($bytes,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser);[Console]::Out.Write([Text.Encoding]::UTF8.GetString($out))";
const decrypted = spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', decrypt], {
  input: await readFile(resolve(homedir(), '.probably-stolen-wiki/nexus-mod-local/config.dpapi'), 'utf8'),
  encoding: 'utf8', windowsHide: true,
});
if (decrypted.status !== 0) throw new Error('Cannot read the existing Nexus DPAPI configuration');
const { nexusApiKey: key } = JSON.parse(decrypted.stdout);
if (!key) throw new Error('No configured Nexus API key');
const normalize = text => (Array.isArray(text) ? text.join('\n') : String(text)).replace(/\s+/g, ' ').trim();
async function api(path, body) {
  const response = await fetch('https://api.nexusmods.com' + path, {
    method: body === undefined ? 'GET' : 'POST',
    headers: { apikey: key, ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) },
    ...(body === undefined ? {} : { body: JSON.stringify(body) }),
    signal: AbortSignal.timeout(30000),
  });
  if (!response.ok) throw new Error(`Nexus HTTP ${response.status}: ${path}`);
  return response.json();
}
const account = await api('/v1/users/validate.json');
if (account.user_id !== 202095293 || account.name.toLowerCase() !== 'nico1314') throw new Error('Unexpected Nexus account');
console.log(JSON.stringify({ account: account.name, mode, folders: selected.map(item => item.folder) }));

const planPath = resolve(root, 'plan.json');
const statePath = resolve(root, 'state.json');
let state = {};
try { state = JSON.parse(await readFile(statePath, 'utf8')); }
catch (error) { if (error.code !== 'ENOENT') throw error; }
async function save() {
  await writeFile(statePath + '.tmp', JSON.stringify(state, null, 2) + '\n');
  await rename(statePath + '.tmp', statePath);
}
async function remote(id) {
  const global = await api(`/v3/games/probablystolen/mods/${id}`);
  if (global.data.uploader?.id !== '202095293') throw new Error(`Unexpected owner of Mod ${id}`);
  // The v1 directory can still reject a newly published page while its v3 page
  // and individual v1 file details are already current. List through v3.
  const groups = await api(`/v3/mods/${global.data.id}/files`);
  const lists = await Promise.all(groups.data.mod_files.map(group => api(`/v3/mod-files/${group.id}/versions`)));
  const files = lists.flatMap(list => list.data.versions.map(file => ({
    file_id: Number(file.game_scoped_id), version_id: file.id, fileGroupId: file.file.id,
    name: file.name, version: file.version, category_name: file.category.toUpperCase(),
    is_primary: file.is_primary, uploaded_timestamp: Date.parse(file.uploaded_at) / 1000,
  })));
  return { mod: global.data, files, globalId: global.data.id };
}
function compareVersions(a, b) {
  const x = a.split('.').map(Number), y = b.split('.').map(Number);
  if (![...x, ...y].every(Number.isInteger)) throw new Error('Non-numeric native release version');
  for (let i = 0; i < Math.max(x.length, y.length); i++) {
    if ((x[i] || 0) !== (y[i] || 0)) return Math.sign((x[i] || 0) - (y[i] || 0));
  }
  return 0;
}
if (mode === 'plan') {
  let plan = [];
  try { plan = JSON.parse(await readFile(planPath, 'utf8')); }
  catch (error) { if (error.code !== 'ENOENT') throw error; }
  for (const mapping of selected) {
    const source = resolve(repo, 'mods', mapping.folder);
    const manifest = await readFile(resolve(source, 'manifest.xml'), 'utf8');
    const version = /<ModVersion>([^<]+)<\/ModVersion>/.exec(manifest)?.[1];
    const modId = /<ID>([^<]+)<\/ID>/.exec(manifest)?.[1];
    if (!version || modId !== mapping.manifestId) throw new Error(`Manifest identity mismatch: ${mapping.folder}`);
    const archive = resolve(repo, 'dist', `${mapping.folder}-${version}.zip`);
    const archiveSize = (await stat(archive)).size;
    const log = await readFile(resolve(source, 'change.log'), 'utf8');
    const lines = log.replace(/\r\n/g, '\n').trim().split('\n');
    if (!lines[0].startsWith(version)) throw new Error(`Current changelog missing: ${mapping.folder}`);
    const end = lines.findIndex((line, i) => i > 0 && /^\d+\.\d+\.\d+(?:\.\d+)?\s*[-|·(]/.test(line));
    const changelog = `[b]正式版 / Full release · ${version}[/b]\n\n` + lines.slice(0, end < 0 ? undefined : end).join('\n');
    const item = { ...mapping, targetVersion: version, archive, archiveSize,
      displayName: `${mapping.name} - Full Release`, changelog,
      description: `Probably Stolen full release, native ModLoader. Extract the ${mapping.folder} folder into the game's Mods directory, enable it in Mods, and restart. ` +
        (mapping.requiresForge ? 'Requires the full-release NicoKobo Forge; install it separately. ' : '') +
        `正式版原生 ModLoader：将 ${mapping.folder} 文件夹放入游戏 Mods 目录，启用并重启。` +
        (mapping.requiresForge ? '需要单独安装正式版 NicoKobo Forge。' : '') +
        ' Demo / MelonLoader files remain available separately; do not mix the two loaders.' };
    if (mapping.id) {
      const current = await remote(mapping.id);
      const nativeFiles = current.files.filter(file => mapping.fileGroupId ? file.fileGroupId === mapping.fileGroupId : file.name === item.displayName);
      if (nativeFiles.some(file => compareVersions(file.version, version) > 0)) throw new Error(`Native release is newer online: ${mapping.folder}`);
      item.globalId = current.globalId;
      item.pageVersionBefore = current.mod.version;
      item.previousFiles = current.files.map(file => ({ id: file.file_id, name: file.name, version: file.version, category: file.category_name }));
      const latest = nativeFiles.filter(file => file.category_name === 'MAIN').sort((a, b) => b.uploaded_timestamp - a.uploaded_timestamp)[0];
      if (latest) item.fileGroupId = latest.fileGroupId;
    }
    plan = plan.filter(old => old.folder !== item.folder);
    plan.push(item);
    console.log(JSON.stringify({ folder: item.folder, id: item.id, version, bytes: archiveSize,
      action: !item.id ? 'needs-new-page' : item.fileGroupId ? 'append-native-version' : 'create-native-file-group', preserveOldFiles: true }));
  }
  await writeFile(planPath, JSON.stringify(plan, null, 2) + '\n');
  process.exit(0);
}
const plan = JSON.parse(await readFile(planPath, 'utf8'));

// Same multipart upload/finalise flow as Nexus-Mods/upload-action. Its standard
// entry point adds versions only; a first native group needs POST /mod-files.
async function createNativeGroup(item) {
  const bytes = await readFile(item.archive);
  const { data: upload } = await api('/v3/uploads/multipart', { filename: item.archive.split(/[\\/]/).at(-1), size_bytes: String(bytes.length) });
  state[item.folder].uploadId = upload.id;
  await save();
  const parts = await Promise.all(upload.part_presigned_urls.map(async (url, i) => {
    const part = bytes.subarray(i * upload.part_size_bytes, (i + 1) * upload.part_size_bytes);
    const response = await fetch(url, { method: 'PUT', headers: { 'Content-Type': 'application/octet-stream', 'Content-Length': String(part.length) }, body: part, signal: AbortSignal.timeout(180000) });
    if (!response.ok || !response.headers.get('ETag')) throw new Error(`Storage upload failed: HTTP ${response.status}`);
    return { number: i + 1, etag: response.headers.get('ETag').replace(/"/g, '') };
  }));
  const response = await fetch(upload.complete_presigned_url, { method: 'POST', headers: { 'Content-Type': 'application/xml' },
    body: '<CompleteMultipartUpload>\n' + parts.map(part => `<Part><PartNumber>${part.number}</PartNumber><ETag>${part.etag}</ETag></Part>`).join('\n') + '\n</CompleteMultipartUpload>', signal: AbortSignal.timeout(30000) });
  if (!response.ok) throw new Error(`Storage finalise failed: HTTP ${response.status}`);
  await api(`/v3/uploads/${upload.id}/finalise`, {});
  for (let attempt = 0; ; attempt++) {
    const { data } = await api(`/v3/uploads/${upload.id}`);
    if (data.state === 'available') break;
    if (attempt === 59) throw new Error('Upload processing timed out; inspect the saved upload ID before resuming');
    await new Promise(resolve => setTimeout(resolve, 2000));
  }
  const { data } = await api('/v3/mod-files', { upload_id: upload.id, mod_id: item.globalId, name: item.displayName,
    version: item.targetVersion, description: item.description, file_category: 'main', primary_mod_manager_download: true,
    allow_mod_manager_download: false, show_requirements_pop_up: item.requiresForge, update_mod_version: true });
  state[item.folder].fileGroupId = data.id;
  await save();
}
async function appendNativeVersion(item) {
  const outputPath = resolve(root, `output-${item.id}.txt`);
  await writeFile(outputPath, '');
  const env = { ...process.env, NODE_USE_ENV_PROXY: '1', ACTIONS_STEP_DEBUG: 'false', GITHUB_OUTPUT: outputPath,
    INPUT_API_KEY: key, INPUT_FILE_ID: item.fileGroupId, INPUT_FILENAME: item.archive,
    INPUT_VERSION: item.targetVersion, INPUT_DISPLAY_NAME: item.displayName, INPUT_DESCRIPTION: item.description,
    INPUT_CATEGORY: 'main', INPUT_ARCHIVE_EXISTING_VERSION: 'false', INPUT_UPDATE_MOD_VERSION: 'true',
    INPUT_PRIMARY_MOD_MANAGER_DOWNLOAD: 'true', INPUT_ALLOW_MOD_MANAGER_DOWNLOAD: 'false',
    INPUT_SHOW_REQUIREMENTS_POP_UP: String(item.requiresForge), INPUT_MOD_ID: item.globalId, INPUT_CHANGELOG: '' };
  const code = await new Promise((resolve, reject) => {
    const child = spawn(process.execPath, ['dist/index.js'], { cwd: process.env.NEXUS_UPLOAD_ACTION_DIR || 'D:/software/nexus-upload-action', env, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
    for (const stream of [child.stdout, child.stderr]) {
      let buffer = '';
      const log = line => { if (!/::debug::|::add-mask::/.test(line) && line.trim()) console.log(`[${item.folder}] ${line.split(key).join('[REDACTED]').replace(/https?:\/\/[^\s<>"']+/g, '[URL]')}`); };
      stream.setEncoding('utf8').on('data', chunk => { buffer += chunk; const lines = buffer.split(/\r?\n/); buffer = lines.pop(); lines.forEach(log); });
      stream.on('end', () => log(buffer));
    }
    child.on('error', reject); child.on('close', resolve);
  });
  const output = await readFile(outputPath, 'utf8');
  state[item.folder].actionExitCode = code;
  state[item.folder].versionId = /^version_id=(\S+)/m.exec(output)?.[1] || /^version_id<<[^\r\n]+\r?\n([^\r\n]+)/m.exec(output)?.[1];
  await save();
  if (code !== 0) throw new Error('Official uploader failed; inspect remote state before retrying');
}
async function verify(item) {
  const current = await remote(item.id);
  const logs = await api(`/v1/games/probablystolen/mods/${item.id}/changelogs.json`);
  const file = current.files.find(file => (item.fileGroupId ? file.fileGroupId === item.fileGroupId : file.name === item.displayName) && file.version === item.targetVersion);
  const detail = file ? await api(`/v1/games/probablystolen/mods/${item.id}/files/${file.file_id}.json`) : null;
  const checks = { mainFile: file?.category_name === 'MAIN' && file?.is_primary === true,
    size: detail?.size_in_bytes === item.archiveSize, pageVersion: current.mod.version === item.targetVersion,
    published: current.mod.status === 'published',
    bilingualChangelog: normalize(logs[item.targetVersion] || '').includes(normalize(item.changelog)),
    previousFilesPreserved: item.previousFiles.every(old => current.files.some(file => file.file_id === old.id && file.name === old.name && file.version === old.version && file.category_name === old.category)) };
  if (file) {
    checks.version = file.version === item.targetVersion && file.category_name === 'MAIN' && file.is_primary === true;
    state[item.folder].versionId = file.version_id;
    state[item.folder].fileGroupId = file.fileGroupId;
  }
  Object.assign(state[item.folder], { fileId: file?.file_id, version: item.targetVersion, checks,
    status: Object.values(checks).every(Boolean) ? 'complete' : 'pending-readback', verifiedAt: new Date().toISOString() });
  await save();
  console.log(JSON.stringify({ folder: item.folder, id: item.id, version: item.targetVersion, fileId: file?.file_id, ...state[item.folder] }));
}
for (const mapping of selected) {
  const item = plan.find(item => item.folder === mapping.folder);
  if (!item?.id || item.id !== mapping.id) { console.error(`${mapping.folder}: create/confirm its Nexus page, then run plan again`); process.exitCode = 1; continue; }
  state[item.folder] ||= {};
  try {
    if (mode === 'upload') {
      const current = await remote(item.id);
      const existing = current.files.filter(file => (item.fileGroupId ? file.fileGroupId === item.fileGroupId : file.name === item.displayName) && file.version === item.targetVersion);
      for (const file of existing) {
        const detail = await api(`/v1/games/probablystolen/mods/${item.id}/files/${file.file_id}.json`);
        if (detail.size_in_bytes !== item.archiveSize) throw new Error('Same native version already has a different archive');
      }
      if (!existing.length) {
        if (state[item.folder].status === 'uploading') throw new Error('An upload was interrupted; inspect its saved upload ID before resuming');
        if (current.mod.version !== item.pageVersionBefore) throw new Error('Page version changed since plan; refresh the plan');
        if ((await stat(item.archive)).size !== item.archiveSize) throw new Error('Release archive changed');
        Object.assign(state[item.folder], { status: 'uploading', startedAt: new Date().toISOString() }); await save();
        console.log(`Uploading ${item.folder} ${item.targetVersion} (${item.archiveSize} bytes); retaining all old files`);
        await (item.fileGroupId ? appendNativeVersion(item) : createNativeGroup(item));
        state[item.folder].status = 'uploaded'; await save();
      }
      const logs = await api(`/v1/games/probablystolen/mods/${item.id}/changelogs.json`);
      if (!normalize(logs[item.targetVersion] || '').includes(normalize(item.changelog)))
        await api(`/v3/mods/${item.globalId}/changelogs`, { version: item.targetVersion, changelog: item.changelog });
    }
    await verify(item);
  } catch (error) {
    state[item.folder].error = String(error).split(key).join('[REDACTED]').replace(/https?:\/\/[^\s<>"']+/g, '[URL]');
    await save(); console.error(JSON.stringify({ folder: item.folder, error: state[item.folder].error })); process.exitCode = 1;
  }
}

'use strict';
const { app, BrowserWindow, ipcMain, dialog, shell, Menu } = require('electron');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const net = require('node:net');
const { spawn } = require('node:child_process');
const { Speech } = require('./speech.cjs');
const { QQ } = require('./qq.cjs');
const { bundledVoices } = require('./voices.cjs');
const testRoot = process.env.YUKKURI_UI_TEST_ROOT;
if (testRoot) app.setPath('userData', testRoot);
const root = app.getPath('userData'), audioRoot = path.join(root, 'Audio');
fs.mkdirSync(audioRoot, { recursive: true });
const speech = new Speech(), qq = new QQ(path.join(root, 'QQAudio'));
let mainWindow, miniWindow, timer, revision = 0, current = null, generating = 0;
function readJson(name, fallback) {
    try { return JSON.parse(fs.readFileSync(path.join(root, name), 'utf8')); } catch (_) { return fallback; }
}
function saveJson(name, value) {
    const file = path.join(root, name), temporary = file + '.tmp';
    fs.writeFileSync(temporary, JSON.stringify(value, null, 2)); fs.renameSync(temporary, file);
}
const storedSettings = readJson('settings.json', {});
const saved = storedSettings && typeof storedSettings === 'object' ? storedSettings : {};
const imported = Array.isArray(saved.voices) ? saved.voices.filter(v =>
    v?.kind === 'native' && typeof v.id === 'string' && typeof v.path === 'string' && fs.existsSync(v.path)) : [];
const state = {
    version: app.getVersion(), platform: process.platform, voices: [...bundledVoices, ...imported],
    text: typeof saved.text === 'string' ? saved.text.slice(0, 240) : '慢慢来，今天也要度过悠闲的一天。',
    voiceId: typeof saved.voiceId === 'string' ? saved.voiceId : 'f1',
    speed: Number.isFinite(saved.speed) ? Math.min(1.6, Math.max(0.65, saved.speed)) : 1,
    autoGenerate: saved.autoGenerate !== false, keepHistory: saved.keepHistory !== false,
    busy: false, sending: false, status: '输入就绪', detail: '本地合成', clip: null,
    qqLabel: 'QQ 未连接', qqConnected: false, history: []
};
if (!state.voices.some(v => v.id === state.voiceId)) state.voiceId = 'f1';
function validId(id) { return typeof id === 'string' && /^[a-f0-9]{32}$/.test(id); }
const storedHistory = readJson('history.json', []);
state.history = (Array.isArray(storedHistory) ? storedHistory : []).filter(clip => validId(clip?.id) &&
    typeof clip.text === 'string' && Number.isFinite(clip.seconds) &&
    fs.existsSync(path.join(audioRoot, clip.id + '.wav'))).slice(0, 60);
function preferences() {
    saveJson('settings.json', { text: state.text, voiceId: state.voiceId, speed: state.speed,
        autoGenerate: state.autoGenerate, keepHistory: state.keepHistory, voices: state.voices.filter(v => v.kind === 'native') });
}
function broadcast() {
    for (const window of [mainWindow, miniWindow])
        if (window && !window.isDestroyed()) window.webContents.send('state', state);
}
function report(error) { state.status = '操作未完成'; state.detail = error.message; broadcast(); }
function clipPayload(clip) {
    return { ...clip, audio: fs.readFileSync(path.join(audioRoot, clip.id + '.wav')).toString('base64') };
}
function invalidate() {
    revision++; clearTimeout(timer); releaseTemporary(); state.clip = null; current = null;
    state.status = '文字或参数已更新'; state.detail = '';
    if (state.autoGenerate && state.text.trim()) timer = setTimeout(() => generate().catch(report), 1200);
    preferences(); broadcast();
}
async function generate() {
    clearTimeout(timer);
    const stamp = ++revision, text = state.text, voice = state.voices.find(v => v.id === state.voiceId), speed = state.speed;
    if (!voice || !text.trim()) throw new Error('请先输入文字并选择音色。');
    state.busy = true; generating++; state.status = '正在合成…'; state.detail = voice.name; broadcast();
    try {
        const result = await speech.request({ text, voice, speed });
        if (revision !== stamp) return null;
        const id = crypto.randomBytes(16).toString('hex');
        fs.writeFileSync(path.join(audioRoot, id + '.wav'), Buffer.from(result.wav));
        releaseTemporary();
        current = { id, text, voiceName: voice.name, seconds: result.seconds, rate: result.rate, createdAt: Date.now() };
        state.clip = clipPayload(current);
        if (state.keepHistory) {
            state.history.unshift(current);
            for (const clip of state.history.splice(60)) {
                try { fs.unlinkSync(path.join(audioRoot, clip.id + '.wav')); } catch (_) {}
            }
            saveJson('history.json', state.history);
        }
        state.status = '语音已就绪'; state.detail = `${result.seconds.toFixed(1)} 秒 · ${voice.name}`;
        return current;
    } finally {
        generating--; state.busy = generating > 0; broadcast();
    }
}
function releaseTemporary() {
    if (current && !state.history.some(clip => clip.id === current.id)) {
        try { fs.unlinkSync(path.join(audioRoot, current.id + '.wav')); } catch (_) {}
    }
}
function createWindow(mini = false) {
    const window = new BrowserWindow({
        width: mini ? 420 : 1180, height: mini ? 360 : 800,
        minWidth: mini ? 380 : 760, minHeight: mini ? 330 : 620,
        title: mini ? '油库里 · 悬浮窗' : '油库里语音 · YukkuriVoice',
        backgroundColor: '#ffffff', alwaysOnTop: mini, autoHideMenuBar: true,
        icon: path.join(__dirname, '../assets/app.png'), show: false,
        webPreferences: { preload: path.join(__dirname, 'preload.cjs'), contextIsolation: true,
            nodeIntegration: false, sandbox: true }
    });
    window.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));
    window.webContents.on('will-navigate', event => event.preventDefault());
    window.webContents.session.setPermissionRequestHandler((_contents, _permission, callback) => callback(false));
    window.loadFile(path.join(__dirname, 'ui/index.html'), { query: mini ? { mini: '1' } : {} });
    window.once('ready-to-show', () => { window.show(); broadcast(); });
    if (mini) {
        miniWindow = window; window.on('closed', () => { miniWindow = null; });
    } else {
        mainWindow = window; window.on('closed', () => { mainWindow = null; miniWindow?.close(); });
    }
    return window;
}
function handle(name, action) {
    ipcMain.handle(name, async (event, ...args) => {
        if (![mainWindow?.webContents, miniWindow?.webContents].includes(event.sender))
            throw new Error('无效的窗口请求。');
        try { return await action(...args); }
        catch (error) { report(error); throw error; }
    });
}
handle('state', () => state);
handle('edit', changes => {
    if (state.sending) return;
    if (!changes || typeof changes !== 'object') return;
    let edited = false;
    if (typeof changes.text === 'string' && changes.text.length <= 240) { state.text = changes.text; edited = true; }
    if (typeof changes.voiceId === 'string' && state.voices.some(v => v.id === changes.voiceId)) {
        state.voiceId = changes.voiceId; edited = true;
    }
    if (Number.isFinite(changes.speed) && changes.speed >= 0.65 && changes.speed <= 1.6) {
        state.speed = changes.speed; edited = true;
    }
    if (typeof changes.autoGenerate === 'boolean') { state.autoGenerate = changes.autoGenerate; edited = true; }
    if (typeof changes.keepHistory === 'boolean') state.keepHistory = changes.keepHistory;
    if (edited) invalidate(); else { preferences(); broadcast(); }
});
handle('generate', () => generate());
handle('floating', () => {
    if (miniWindow) { miniWindow.show(); miniWindow.focus(); } else createWindow(true);
});
handle('main-window', () => { mainWindow?.show(); mainWindow?.focus(); });
handle('export', async () => {
    if (!current) await generate();
    if (!current) return;
    const result = await dialog.showSaveDialog({ title: '导出语音',
        defaultPath: `油库里_${new Date().toISOString().slice(0, 10)}.wav`,
        filters: [{ name: 'WAV 音频', extensions: ['wav'] }] });
    if (result.canceled || !result.filePath) return;
    fs.copyFileSync(path.join(audioRoot, current.id + '.wav'), result.filePath);
    state.status = '已导出 WAV'; state.detail = path.basename(result.filePath); broadcast();
});
handle('history-use', id => {
    if (state.sending || !validId(id)) return;
    const clip = state.history.find(c => c.id === id);
    if (!clip) throw new Error('语音记录已不存在。');
    revision++; clearTimeout(timer); releaseTemporary(); current = clip; state.text = clip.text; state.clip = clipPayload(clip);
    state.status = '已载入语音'; state.detail = clip.voiceName; preferences(); broadcast();
});
handle('history-delete', async id => {
    if (state.sending || !validId(id)) return;
    const result = await dialog.showMessageBox({ type: 'question', title: '删除语音',
        message: '删除这条本地语音记录？', buttons: ['取消', '删除'], defaultId: 0, cancelId: 0 });
    if (result.response !== 1) return;
    state.history = state.history.filter(c => c.id !== id);
    if (current?.id === id) { current = null; state.clip = null; }
    try { fs.unlinkSync(path.join(audioRoot, id + '.wav')); } catch (_) {}
    saveJson('history.json', state.history); broadcast();
});
handle('import-engine', async () => {
    const result = await dialog.showOpenDialog({ title: '选择当前系统的授权 AquesTalk 引擎',
        properties: ['openFile'], filters: [{ name: 'AquesTalk 本机引擎', extensions:
            process.platform === 'darwin' ? ['dylib', 'so'] : process.platform === 'win32' ? ['dll'] : ['so'] }] });
    if (result.canceled) return;
    const voices = await speech.request({ action: 'discover', path: result.filePaths[0] });
    for (const voice of voices) if (!state.voices.some(v => v.id === voice.id)) state.voices.push(voice);
    state.voiceId = voices[0].id; invalidate();
});
handle('open-data', () => shell.openPath(root));
handle('qq-refresh', async () => {
    try {
        const peer = await qq.active();
        state.qqConnected = true; state.qqLabel = (peer.chatType === 2 ? '群 · ' : '好友 · ') + peer.name;
        broadcast(); return state.qqLabel;
    } catch (error) {
        state.qqConnected = false; state.qqLabel = 'QQ 未连接'; broadcast(); throw error;
    }
});
handle('qq-launch', async () => {
    if (testRoot) throw new Error('界面检查不会启动 QQ。');
    const candidates = process.platform === 'darwin' ? ['/Applications/QQ.app/Contents/MacOS/QQ',
        path.join(app.getPath('home'), 'Applications/QQ.app/Contents/MacOS/QQ')] :
        ['/opt/QQ/qq', '/opt/qq/qq', '/usr/bin/qq'];
    let executable = candidates.find(file => fs.existsSync(file));
    if (!executable) {
        const result = await dialog.showOpenDialog({ title: '选择已安装的 QQ 可执行文件', properties: ['openFile'] });
        if (result.canceled) return;
        executable = result.filePaths[0];
    }
    if (!/^qq(?:\.exe)?$/i.test(path.basename(executable))) throw new Error('请选择 QQ 的主程序。');
    qq.port = await new Promise((resolve, reject) => {
        const server = net.createServer(); server.on('error', reject);
        server.listen(0, '127.0.0.1', () => { const port = server.address().port; server.close(() => resolve(port)); });
    });
    const child = spawn(executable, [`--remote-debugging-port=${qq.port}`, '--remote-debugging-address=127.0.0.1'],
        { detached: true, stdio: 'ignore', windowsHide: true });
    child.on('error', report); child.unref();
    state.status = '正在启动 QQ'; state.detail = '登录后点击“刷新会话”'; broadcast();
});
handle('qq-send', async () => {
    if (state.sending || testRoot) throw new Error(testRoot ? '界面检查不会发送 QQ 消息。' : '正在发送。');
    state.sending = true; clearTimeout(timer); broadcast();
    try {
        const peer = await qq.active();
        if (!current) await generate();
        if (!current) throw new Error('文字已变化，请重新生成后发送。');
        state.status = '正在提交 QQ 语音…'; broadcast();
        const result = await qq.send(peer, fs.readFileSync(path.join(audioRoot, current.id + '.wav')));
        state.status = 'QQ 已接受语音消息'; state.detail = '消息编号 ' + result.messageId;
    } finally { state.sending = false; broadcast(); }
});
if (!app.requestSingleInstanceLock()) app.quit();
else {
    app.on('second-instance', () => { mainWindow?.show(); mainWindow?.focus(); });
    app.whenReady().then(() => {
        Menu.setApplicationMenu(Menu.buildFromTemplate([
            ...(process.platform === 'darwin' ? [{ role: 'appMenu' }] : []),
            { role: 'editMenu' }, { role: 'windowMenu' }
        ]));
        createWindow();
    });
    app.on('activate', () => { if (!mainWindow) createWindow(); });
    app.on('window-all-closed', () => { if (process.platform !== 'darwin' || testRoot) app.quit(); });
    app.on('before-quit', () => { clearTimeout(timer); releaseTemporary(); speech.dispose(); });
}

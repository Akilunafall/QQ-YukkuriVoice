'use strict';
const api = window.yukkuri, byId = id => document.getElementById(id);
const mini = new URLSearchParams(location.search).has('mini');
if (mini) { document.body.classList.add('mini-mode'); byId('mini').hidden = false; }
window.lucide.createIcons();
let state, audio, audioUrl, clipId = '', audioContext;
const invoke = action => Promise.resolve().then(action).catch(() => {});
function icon(name) {
    const span = document.createElement('i'); span.dataset.lucide = name; return span;
}
function stop() { if (audio) { audio.pause(); audio.currentTime = 0; } }
function wave(bytes) {
    const canvas = byId('waveform'), context = canvas.getContext('2d');
    context.clearRect(0, 0, canvas.width, canvas.height);
    context.fillStyle = '#197e76';
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    let offset = 12, samples;
    while (offset + 8 < bytes.length) {
        const size = view.getUint32(offset + 4, true);
        if (String.fromCharCode(...bytes.slice(offset, offset + 4)) === 'data') {
            samples = new Int16Array(bytes.buffer.slice(bytes.byteOffset + offset + 8, bytes.byteOffset + offset + 8 + size)); break;
        }
        offset += 8 + size + (size & 1);
    }
    for (let i = 0; i < 180; i++) {
        let peak = .04;
        if (samples) {
            const start = Math.floor(i * samples.length / 180), end = Math.floor((i + 1) * samples.length / 180);
            for (let j = start; j < end; j++) peak = Math.max(peak, Math.abs(samples[j]) / 32768);
        }
        const height = Math.max(2, peak * 39);
        context.fillRect(i * 5, (42 - height) / 2, 2.5, height);
    }
}
function updateHistory() {
    const list = byId('history-list'); list.replaceChildren();
    byId('history-empty').hidden = state.history.length > 0;
    for (const clip of state.history) {
        const row = document.createElement('div'); row.className = 'history-row';
        const copy = document.createElement('div'); copy.className = 'history-copy';
        const text = document.createElement('p'); text.textContent = clip.text;
        const meta = document.createElement('span'); meta.className = 'muted';
        meta.textContent = `${clip.voiceName} · ${clip.seconds.toFixed(1)} 秒 · ${new Date(clip.createdAt).toLocaleString()}`;
        copy.append(text, meta);
        const tools = document.createElement('div'); tools.className = 'history-tools';
        const use = document.createElement('button'); use.className = 'icon'; use.title = '载入语音';
        use.setAttribute('aria-label', '载入语音'); use.append(icon('play'));
        use.onclick = () => invoke(() => api.useHistory(clip.id));
        const remove = document.createElement('button'); remove.className = 'icon'; remove.title = '删除记录';
        remove.setAttribute('aria-label', '删除记录'); remove.append(icon('trash-2'));
        remove.onclick = () => invoke(() => api.deleteHistory(clip.id));
        tools.append(use, remove); row.append(copy, tools); list.append(row);
    }
    window.lucide.createIcons();
}
function render(next) {
    const previous = state; state = next;
    const names = { darwin: 'macOS', linux: 'Linux', win32: 'Windows · 开发检查' };
    byId('platform-version').textContent = `${names[state.platform] || state.platform} / v${state.version}`;
    if (!previous || previous.voices.length !== state.voices.length) {
        byId('voice').replaceChildren();
        for (const engine of [...new Set(state.voices.map(v => v.engine))]) {
            const group = document.createElement('optgroup'); group.label = engine;
            for (const voice of state.voices.filter(v => v.engine === engine)) {
                const option = document.createElement('option'); option.value = voice.id; option.textContent = voice.name;
                group.append(option);
            }
            byId('voice').append(group);
        }
    }
    for (const id of ['text', 'mini-text']) {
        if (byId(id).value !== state.text) byId(id).value = state.text;
        byId(id).readOnly = state.sending;
    }
    byId('voice').value = state.voiceId;
    byId('speed').value = state.speed;
    byId('speed-value').textContent = state.speed.toFixed(2) + '×';
    byId('auto-generate').checked = state.autoGenerate; byId('keep-history').checked = state.keepHistory;
    byId('count').textContent = state.text.length + ' / 240';
    byId('engine-label').textContent = state.voices.find(v => v.id === state.voiceId)?.engine || '';
    for (const id of ['qq-label', 'recipient', 'mini-peer', 'settings-peer']) byId(id).textContent = state.qqLabel;
    byId('voice-summary').textContent = state.voices.length + ' 个可用音色';
    for (const id of ['duration', 'mini-duration']) byId(id).textContent = (state.clip?.seconds || 0).toFixed(1) + ' 秒';
    byId('clip-name').textContent = state.clip?.voiceName || '等待生成';
    for (const id of ['status', 'mini-status']) byId(id).textContent = state.status;
    byId('detail').textContent = state.detail;
    for (const id of ['generate', 'export', 'import-engine']) byId(id).disabled = state.busy || state.sending;
    for (const id of ['send', 'mini-send']) byId(id).disabled = state.busy || state.sending || !state.text.trim() || !state.qqConnected;
    for (const id of ['play', 'mini-play']) byId(id).disabled = !state.clip || state.sending;
    for (const id of ['voice', 'speed']) byId(id).disabled = state.sending;
    if ((state.clip?.id || '') !== clipId) {
        stop(); clipId = state.clip?.id || '';
        if (audioUrl) URL.revokeObjectURL(audioUrl);
        if (state.clip) {
            const bytes = Uint8Array.from(atob(state.clip.audio), value => value.charCodeAt(0));
            audioUrl = URL.createObjectURL(new Blob([bytes], { type: 'audio/wav' }));
            audio = new Audio(audioUrl); wave(bytes);
        } else { audio = null; wave(new Uint8Array()); }
    }
    if (!previous || JSON.stringify(previous.history) !== JSON.stringify(state.history)) updateHistory();
}
for (const button of document.querySelectorAll('[data-page]')) button.onclick = () => {
    for (const page of document.querySelectorAll('.page')) page.hidden = page.id !== button.dataset.page;
    for (const nav of document.querySelectorAll('.nav')) nav.classList.toggle('active', nav === button);
    byId('page-title').textContent = button.textContent;
};
for (const id of ['text', 'mini-text']) byId(id).oninput = event => invoke(() => api.edit({ text: event.target.value }));
byId('voice').onchange = event => invoke(() => api.edit({ voiceId: event.target.value }));
byId('speed').oninput = event => invoke(() => api.edit({ speed: Number(event.target.value) }));
byId('auto-generate').onchange = event => invoke(() => api.edit({ autoGenerate: event.target.checked }));
byId('keep-history').onchange = event => invoke(() => api.edit({ keepHistory: event.target.checked }));
byId('clear').onclick = () => invoke(() => api.edit({ text: '' }));
byId('generate').onclick = () => invoke(() => api.generate());
for (const id of ['play', 'mini-play']) byId(id).onclick = () => { if (audio) invoke(() => audio.play()); };
for (const id of ['stop', 'mini-stop']) byId(id).onclick = stop;
for (const id of ['send', 'mini-send']) byId(id).onclick = () => invoke(() => api.sendQQ());
byId('export').onclick = () => invoke(() => api.export());
byId('floating').onclick = () => invoke(() => api.floating());
byId('restore').onclick = () => invoke(() => api.mainWindow());
byId('launch-qq').onclick = () => invoke(() => api.launchQQ());
byId('refresh-qq').onclick = () => invoke(() => api.refreshQQ());
byId('import-engine').onclick = () => invoke(() => api.importEngine());
byId('open-data').onclick = () => invoke(() => api.openData());
document.addEventListener('keydown', event => {
    if ((event.ctrlKey || event.metaKey) && event.key === 'Enter') {
        event.preventDefault(); invoke(() => api.generate());
    }
});
api.onState(render); invoke(async () => render(await api.state()));

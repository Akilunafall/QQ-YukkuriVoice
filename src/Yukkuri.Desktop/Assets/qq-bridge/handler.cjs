'use strict';

const path = require('node:path');
const fs = require('node:fs');
const crypto = require('node:crypto');

function authenticate(request) {
    const config = JSON.parse(fs.readFileSync(path.join(__dirname, 'connection.json'), 'utf8'));
    const expected = Buffer.from(config.secret || '');
    const supplied = Buffer.from(request.secret || '');
    if (expected.length !== 64 || expected.length !== supplied.length ||
        !crypto.timingSafeEqual(expected, supplied)) throw new Error('本机连接校验失败，请重启本软件。');
}

async function callRenderer(window, renderer, operation, parameters) {
    return window.webContents.executeJavaScript('(' + renderer.toString() + ')(' +
        JSON.stringify(operation) + ',' + JSON.stringify({ ...parameters, webContentsId: window.webContents.id }) + ')');
}

module.exports = async function handle(state, request) {
    authenticate(request);
    if (!['active', 'probe', 'probeRpc', 'encode', 'sendVoice'].includes(request.action))
        throw new Error('不支持的本地操作。');
    const rendererPath = path.join(__dirname, 'renderer.cjs');
    delete require.cache[require.resolve(rendererPath)];
    const renderer = require(rendererPath);
    const isChat = window => /#\/(?:main\/message|chat)(?:[/?]|$)/.test(window.webContents.getURL());
    const track = window => {
        if (!window.__yukkuriFocusTracking) {
            window.__yukkuriFocusTracking = true;
            window.on('focus', () => { if (isChat(window)) state.lastWindowId = window.id; });
        }
    };
    for (const window of state.windows()) track(window);
    if (!state.tracksNewWindows) {
        state.tracksNewWindows = true;
        require('electron').app.on('browser-window-created', (_event, window) => track(window));
    }
    const windows = state.windows().filter(w => w.isVisible() && isChat(w));
    const focused = windows.find(w => w.isFocused());
    if (focused) state.lastWindowId = focused.id;
    const requestedHandle = String(request.parameters?.windowHandle || '0');
    const requested = requestedHandle !== '0' ? windows.find(w => {
        const handle = w.getNativeWindowHandle();
        return (handle.length === 8 ? handle.readBigUInt64LE(0).toString() : String(handle.readUInt32LE(0))) === requestedHandle;
    }) : null;
    if (requestedHandle !== '0' && !requested) throw new Error('上次 QQ 窗口已关闭或不是聊天窗口，请点一下目标聊天后重试。');
    const chosen = requested || focused || windows.find(w => w.id === state.lastWindowId) ||
        (windows.length === 1 ? windows[0] : null);
    if (!chosen) throw new Error('请打开目标 QQ 聊天窗口；多个聊天窗口时请先点一下目标窗口。');
    if (requested) state.lastWindowId = chosen.id;
    if (request.action === 'sendVoice' || request.action === 'encode') {
        const params = request.parameters || {};
        const owned = path.join(__dirname, 'Audio');
        const input = path.resolve(String(params.audioPath || ''));
        if (path.dirname(input).toLowerCase() !== owned.toLowerCase() ||
            !/^[a-f0-9]{32}\.wav$/i.test(path.basename(input))) throw new Error('语音来源不是本软件的本机音频目录。');
        if (fs.statSync(input).size > 3000000) throw new Error('语音文件过大。');
        const { encode, isSilk, getWavFileInfo } = require('./silk/index.cjs');
        const wav = fs.readFileSync(input);
        const info = getWavFileInfo(wav);
        const chunk = info.chunkInfo.find(c => c.chunkId === 'data');
        if (!chunk || info.fmt.numberOfChannels !== 1 || info.fmt.bitsPerSample !== 16)
            throw new Error('语音 WAV 格式无效。');
        const encoded = await encode(wav, 0);
        if (!isSilk(encoded.data) || encoded.duration <= 0 || encoded.duration > 58080)
            throw new Error('QQ 语音编码失败或时长超限。');
        if (request.action === 'encode') return { bytes: encoded.data.length, duration: encoded.duration };
        const active = await callRenderer(chosen, renderer, 'active', {});
        if (chosen.id !== params.WindowId || active.chatType !== params.ChatType || active.peerUid !== params.PeerUid)
            throw new Error('QQ 当前聊天已切换，已取消发送；请确认目标后重试。');
        if (!/^[a-f0-9]{32}$/.test(params.requestId || '')) throw new Error('无效的发送请求。');
        state.sends ||= new Map();
        if (state.sends.has(params.requestId)) return state.sends.get(params.requestId);
        if (state.sends.size >= 512) state.sends.delete(state.sends.keys().next().value);
        const bytes = Buffer.from(encoded.data);
        const md5 = crypto.createHash('md5').update(bytes).digest('hex');
        const output = path.join(owned, md5 + '.silk');
        fs.writeFileSync(output, bytes);
        const count = Math.floor(chunk.dataLength / 2);
        const amplitudes = Array.from({ length: 32 }, (_, i) => {
            const start = Math.floor(count * i / 32), end = Math.floor(count * (i + 1) / 32);
            let sum = 0;
            for (let j = start; j < end; j++) sum += Math.abs(wav.readInt16LE(chunk.dataOffset + j * 2));
            return Math.min(100, Math.round(sum / Math.max(1, end - start) / 32768 * 250));
        });
        const element = { elementType: 4, elementId: '', pttElement: {
            filePath: output, fileName: md5 + '.silk', fileSize: String(bytes.length), md5HexStr: md5,
            duration: Math.max(1, Math.ceil(encoded.duration / 1000)), formatType: 1, voiceType: 1, voiceChangeType: 0,
            canConvert2Text: true, fileId: 0, fileSubId: '0', fileUuid: '', fileBizId: null,
            invalidState: 0, playState: 0, progress: 0, text: '', transferStatus: 0, translateStatus: 0,
            waveAmplitudes: amplitudes, autoConvertText: 0, storeID: 0,
            otherBusinessInfo: { aiVoiceType: 0 }
        } };
        const promise = callRenderer(chosen, renderer, 'sendVoice', {
            expected: active, element, requestId: params.requestId
        });
        state.sends.set(params.requestId, promise);
        // Keep SILK until QQ has finished uploading; clean old files next launch.
        return promise;
    }
    const operation = request.action === 'probe' ? 'capabilities' : request.action;
    if (!state.cleaned) {
        state.cleaned = true;
        const directory = path.join(__dirname, 'Audio');
        if (fs.existsSync(directory)) for (const name of fs.readdirSync(directory)) {
            if (!/^[a-f0-9]{32}\.(silk|wav)$/i.test(name)) continue;
            const file = path.join(directory, name);
            try { if (Date.now() - fs.statSync(file).mtimeMs > 7 * 86400000) fs.unlinkSync(file); } catch (_) {}
        }
    }
    const result = await callRenderer(chosen, renderer, operation, {});
    return { windowId: chosen.id, ...result };
};

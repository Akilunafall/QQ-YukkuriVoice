'use strict';
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const sharedRenderer = require('../assets/qq-bridge/renderer.cjs');
const { resampleForQQ, readWav } = require('./wav.cjs');
class Cdp {
    constructor(url) { this.url = url; this.pending = new Map(); this.id = 0; }
    async open() {
        this.socket = new WebSocket(this.url);
        await new Promise((resolve, reject) => {
            const timer = setTimeout(() => { this.close(); reject(new Error('QQ 本机连接超时。')); }, 5000);
            this.socket.addEventListener('open', () => { clearTimeout(timer); resolve(); }, { once: true });
            this.socket.addEventListener('error', () => { clearTimeout(timer); reject(new Error('QQ 本机连接失败。')); }, { once: true });
        });
        this.socket.addEventListener('message', event => {
            let message;
            try { message = JSON.parse(event.data); } catch (_) { return; }
            const entry = this.pending.get(message.id);
            if (!entry) return;
            clearTimeout(entry.timer); this.pending.delete(message.id);
            message.error ? entry.reject(new Error(message.error.message)) : entry.resolve(message.result);
        });
        this.socket.addEventListener('close', () => this.close());
        return this;
    }
    request(method, params, timeout = 10000) {
        return new Promise((resolve, reject) => {
            const id = ++this.id, timer = setTimeout(() => {
                this.pending.delete(id); reject(new Error('QQ 响应超时，结果未知，请先检查聊天记录。'));
            }, timeout);
            this.pending.set(id, { resolve, reject, timer });
            this.socket.send(JSON.stringify({ id, method, params }));
        });
    }
    async evaluate(expression, timeout = 10000) {
        const result = await this.request('Runtime.evaluate', {
            expression, awaitPromise: true, returnByValue: true
        }, timeout);
        if (result.exceptionDetails) throw new Error(
            result.exceptionDetails.exception?.description || result.exceptionDetails.text || 'QQ 内部操作失败。');
        return result.result.value;
    }
    close() {
        const socket = this.socket; this.socket = null;
        if (socket && socket.readyState < 2) socket.close();
        for (const entry of this.pending.values()) { clearTimeout(entry.timer); entry.reject(new Error('QQ 连接已关闭。')); }
        this.pending.clear();
    }
}
class QQ {
    constructor(directory) { this.directory = directory; this.port = 0; this.lastTarget = ''; this.sending = false; }
    async targets() {
        if (!Number.isInteger(this.port) || this.port < 1024 || this.port > 65535)
            throw new Error('QQ 尚未连接，请通过“启动 QQ”建立本机连接。');
        let response;
        try { response = await fetch(`http://127.0.0.1:${this.port}/json/list`, { signal: AbortSignal.timeout(2500) }); }
        catch (_) { throw new Error('QQ 连接不可用。若 QQ 已运行，请先退出 QQ，再点击“启动 QQ”。'); }
        if (!response.ok) throw new Error('无法读取 QQ 当前窗口。');
        const targets = await response.json();
        if (!Array.isArray(targets)) throw new Error('无效的 QQ 本机响应。');
        return targets.filter(target => {
            try {
                const ws = new URL(target.webSocketDebuggerUrl);
                return target.type === 'page' && /^app:\/\/\.\/renderer\/index\.html/.test(target.url) &&
                    /#\/(?:main\/message|chat)(?:[/?]|$)/.test(target.url) &&
                    ws.protocol === 'ws:' && ws.hostname === '127.0.0.1' && Number(ws.port) === this.port;
            } catch (_) { return false; }
        });
    }
    async inspect(target) {
        const client = await new Cdp(target.webSocketDebuggerUrl).open();
        try {
            const result = await client.evaluate(`(async()=>({peer:await (${sharedRenderer.toString()})('active',{}),focused:document.hasFocus()}))()`);
            const id = Number(new URL(target.url).searchParams.get('webcontentsid'));
            if (!Number.isSafeInteger(id) || id < 1) throw new Error('此 QQ 版本未提供兼容的会话接口。');
            return { ...result.peer, focused: result.focused, targetId: target.id, webContentsId: id, url: target.webSocketDebuggerUrl };
        } finally { client.close(); }
    }
    async active() {
        const results = await Promise.allSettled((await this.targets()).map(target => this.inspect(target)));
        const peers = results.filter(result => result.status === 'fulfilled').map(result => result.value);
        const focused = peers.filter(peer => peer.focused);
        const peer = focused.length === 1 ? focused[0] : peers.find(p => p.targetId === this.lastTarget) ||
            (peers.length === 1 ? peers[0] : null);
        if (!peer) throw new Error('请在 QQ 打开目标聊天；多个聊天窗口时先点一下目标窗口。');
        this.lastTarget = peer.targetId;
        return peer;
    }
    async send(peer, original) {
        if (this.sending) throw new Error('上一条语音仍在发送。');
        this.sending = true;
        let client;
        try {
            const sendingCopy = resampleForQQ(original);
            const { encode, isSilk } = require('../assets/qq-bridge/silk/index.cjs');
            const encoded = await encode(sendingCopy, 0);
            if (!isSilk(encoded.data)) throw new Error('QQ 语音编码失败。');
            const current = await this.active();
            if (current.targetId !== peer.targetId || current.peerUid !== peer.peerUid || current.chatType !== peer.chatType)
                throw new Error('QQ 当前聊天已切换，已取消发送。');
            const bytes = Buffer.from(encoded.data), md5 = crypto.createHash('md5').update(bytes).digest('hex');
            fs.mkdirSync(this.directory, { recursive: true });
            const file = path.join(this.directory, md5 + '.silk');
            fs.writeFileSync(file, bytes);
            const wav = readWav(sendingCopy), samples = wav.data.length / 2;
            const amplitudes = Array.from({ length: 32 }, (_, index) => {
                let sum = 0;
                const start = Math.floor(samples * index / 32), end = Math.floor(samples * (index + 1) / 32);
                for (let i = start; i < end; i++) sum += Math.abs(wav.data.readInt16LE(i * 2));
                return Math.min(100, Math.round(sum / Math.max(1, end - start) / 32768 * 250));
            });
            const element = { elementType: 4, elementId: '', pttElement: {
                filePath: file, fileName: md5 + '.silk', fileSize: String(bytes.length), md5HexStr: md5,
                duration: Math.max(1, Math.ceil(encoded.duration / 1000)), formatType: 1, voiceType: 1, voiceChangeType: 0,
                canConvert2Text: true, fileId: 0, fileSubId: '0', fileUuid: '', fileBizId: null,
                invalidState: 0, playState: 0, progress: 0, text: '', transferStatus: 0, translateStatus: 0,
                waveAmplitudes: amplitudes, autoConvertText: 0, storeID: 0, otherBusinessInfo: { aiVoiceType: 0 }
            } };
            client = await new Cdp(peer.url).open();
            const result = await client.evaluate(`(${sharedRenderer.toString()})('sendVoice',${JSON.stringify({
                webContentsId: peer.webContentsId, expected: peer, requestId: crypto.randomUUID(), element
            })})`, 45000);
            if (!result?.messageId || result.messageId === '0') throw new Error('QQ 尚未确认发送，请检查聊天记录。');
            return result;
        } finally { client?.close(); this.sending = false; }
    }
}
module.exports = { QQ, Cdp };

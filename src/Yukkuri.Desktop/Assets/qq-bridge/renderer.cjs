'use strict';

// Runs in QQ's existing renderer. Do not enumerate contacts or message text.
module.exports = async function renderer(operation, args) {
    const root = document.getElementById('app');
    const vue = root?.__vue_app__;
    const seen = new Set(), stores = new Set(), services = new Map();
    function visit(node, depth) {
        if (!node || depth > 45 || seen.has(node) || seen.size > 5000) return;
        seen.add(node);
        const c = node.component;
        if (c && !c.isDeactivated && !c.isUnmounted) {
            const ctx = c.ctx || {};
            for (const key of ['commonAioStore', 'aioStore']) {
                const store = ctx[key];
                if (store?.curAioData?.header) stores.add(store);
            }
            for (const key of ['messageSendService', 'msgService']) {
                if (ctx[key]) services.set(key, ctx[key]);
            }
            visit(c.subTree, depth + 1);
        }
        if (Array.isArray(node.children)) node.children.forEach(n => visit(n, depth + 1));
    }
    visit(vue?._instance?.subTree || root?._vnode, 0);
    function currentPeer(refresh = false) {
        if (refresh) {
            seen.clear(); stores.clear(); services.clear();
            visit(vue?._instance?.subTree || root?._vnode, 0);
        }
        const peers = new Map();
        for (const store of stores) {
            const data = store.curAioData, header = data.header;
            const chatType = Number(data.chatType);
            const peerUid = String(header?.uid || header?.uin || '');
            if (![1, 2].includes(chatType) || !peerUid) continue;
            peers.set(chatType + ':' + peerUid, { chatType, peerUid, guildId: '',
                name: String(header.peerName || ''), uin: String(header.uin || '') });
        }
        if (peers.size !== 1) throw new Error('无法唯一确定当前 QQ 会话，请打开目标聊天后重试。');
        return [...peers.values()][0];
    }
    const peer = currentPeer();
    if (operation === 'active') return peer;
    if (operation === 'capabilities') return {
        chatType: peer.chatType, activePeerDetected: true,
        ipcSend: typeof window.ipcImpl?.send === 'function', ipcOn: typeof window.ipcImpl?.on === 'function'
    };
    const stateKey = '__yukkuriVoiceTransportV6';
    if (!window[stateKey]) {
        const transport = { pending: new Map(), confirmations: new Map() };
        window.ipcImpl.on('RM_IPCFROM_MAIN' + args.webContentsId, (_event, envelope, body) => {
            const pending = transport.pending.get(envelope?.callbackId);
            if (pending && envelope.type === 'response') {
                transport.pending.delete(envelope.callbackId);
                clearTimeout(pending.timer);
                if (envelope.promiseStatue === 'full') pending.resolve(body);
                else {
                    const detail = typeof body === 'string' ? body :
                        body?.message || body?.error || body?.stack || JSON.stringify(body);
                    pending.reject(new Error('QQ 拒绝 ' + pending.command + '：' + String(detail).slice(0, 1800)));
                }
            }
            const payload = body?.payload ?? body;
            const messages = payload?.msgList || (Array.isArray(payload) ? payload.flatMap(p => p?.msgList || []) : []);
            for (const confirmation of transport.confirmations.values()) {
                for (const msg of Array.isArray(messages) ? messages : []) {
                    if (String(msg.peerUid) !== confirmation.peer.peerUid || Number(msg.chatType) !== confirmation.peer.chatType) continue;
                    const elements = msg.elements || msg.msgElements || [];
                    if (!elements.some(e => e.pttElement?.md5HexStr?.toLowerCase() === confirmation.md5)) continue;
                    if (confirmation.messageId && String(msg.msgId) !== confirmation.messageId) continue;
                    if (Number(msg.sendStatus) === 2 && msg.msgId && msg.msgId !== '0')
                        confirmation.finish({ messageId: String(msg.msgId) });
                    else if (Number(msg.sendStatus) === 0)
                        confirmation.fail(new Error('QQ 返回语音发送失败，请检查网络或聊天权限。'));
                }
            }
        });
        window[stateKey] = transport;
    }
    const transport = window[stateKey];
    function rpc(command, payload, timeout = 8000) {
        const id = 'yukkuri-' + crypto.randomUUID();
        return new Promise((resolve, reject) => {
            const timer = setTimeout(() => {
                transport.pending.delete(id);
                reject(new Error('QQ 内部接口超时，结果未知；请检查 QQ，避免重复发送。'));
            }, timeout);
            transport.pending.set(id, { resolve, reject, timer, command });
            try {
                window.ipcImpl.send('RM_IPCFROM_RENDERER' + args.webContentsId,
                    { type: 'request', eventName: 'ntApi', callbackId: id, peerId: args.webContentsId },
                    { cmdName: command, cmdType: 'invoke', payload });
            } catch (error) {
                clearTimeout(timer);
                transport.pending.delete(id);
                reject(error);
            }
        });
    }
    if (operation === 'probeRpc') {
        const result = await rpc('nodeIKernelMsgService/generateMsgUniqueId',
            [{ chatType: peer.chatType, time: String(Date.now()) }]);
        return { confirmed: typeof result === 'string' && /^[1-9][0-9]*$/.test(result), resultType: typeof result };
    }
    if (operation === 'sendVoice') {
        const rechecked = currentPeer();
        if (rechecked.chatType !== args.expected.chatType || rechecked.peerUid !== args.expected.peerUid)
            throw new Error('QQ 当前聊天已切换，已取消发送。');
        const internalPeer = { chatType: peer.chatType, peerUid: peer.peerUid, guildId: '' };
        const generated = await rpc('nodeIKernelMsgService/generateMsgUniqueId',
            [{ chatType: peer.chatType, time: String(Date.now()) }]);
        if (typeof generated !== 'string' || !/^[1-9][0-9]*$/.test(generated))
            throw new Error('QQ 未能建立语音消息编号，已取消发送。');
        const finalPeer = currentPeer(true);
        if (finalPeer.chatType !== args.expected.chatType || finalPeer.peerUid !== args.expected.peerUid)
            throw new Error('QQ 当前聊天已切换，已取消发送。');
        const md5 = args.element.pttElement.md5HexStr;
        return new Promise((resolve, reject) => {
            const cleanup = () => { clearTimeout(timer); transport.confirmations.delete(args.requestId); };
            const finish = value => { cleanup(); resolve(value); };
            const fail = error => { cleanup(); reject(error); };
            const timer = setTimeout(() => fail(new Error('QQ 尚未确认语音发送，结果未知；请先检查聊天记录，勿重复发送。')), 35000);
            transport.confirmations.set(args.requestId, { peer, md5, messageId: generated, finish, fail });
            rpc('nodeIKernelMsgService/sendMsg', [{
                msgId: generated, peer: internalPeer,
                msgElements: [args.element], msgAttributeInfos: new Map()
            }, undefined], 30000).then(result => {
                if (result && Number(result.result) !== 0) return fail(new Error('QQ 未接受语音发送请求。'));
                if (Number(result?.sendStatus) === 2 && String(result.msgId) === generated)
                    finish({ messageId: String(result.msgId) });
            }, fail);
        });
    }
    throw new Error('不支持的本地操作。');
};

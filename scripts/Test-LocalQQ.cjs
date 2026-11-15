'use strict';

const assert = require('node:assert/strict');
const path = require('node:path');
const vm = require('node:vm');
const fs = require('node:fs');
const renderer = require('../src/Yukkuri.Desktop/Assets/qq-bridge/renderer.cjs');
const script = '(' + renderer.toString() + ')';
let checks = 0;
function check(value, label) { assert(value, label); checks++; console.log('PASS ' + label); }

function fixture(options = {}) {
    const callbacks = [];
    const calls = [];
    const data = { chatType: 2, header: { uid: '456', uin: '456', peerName: 'Fixture group' } };
    const component = { ctx: { commonAioStore: { curAioData: data } }, subTree: {} };
    const root = { __vue_app__: { _instance: { subTree: { component } } } };
    const context = vm.createContext({
        document: { getElementById: () => root },
        crypto: require('node:crypto').webcrypto,
        setTimeout: (fn, delay) => setTimeout(fn, Math.min(delay, options.timeout ? 25 : 5000)),
        clearTimeout,
        window: { ipcImpl: {
            on: (_channel, callback) => callbacks.push(callback),
            send: (channel, envelope, body) => {
                calls.push({ channel, envelope, body });
                if (options.timeout || (options.sendTimeout && body.cmdName.endsWith('/sendMsg'))) return;
                setTimeout(() => {
                    if (options.nativeError && body.cmdName.endsWith('/sendMsg')) {
                        callbacks.forEach(callback => callback({}, { ...envelope, type: 'response',
                            promiseStatue: 'rejected' }, new Error('Cannot convert undefined or null to object')));
                        return;
                    }
                    if (options.strictVoice && body.cmdName.endsWith('/sendMsg')) {
                        const attributes = body.payload[0].msgAttributeInfos;
                        const ptt = body.payload[0].msgElements[0].pttElement;
                        if (Object.prototype.toString.call(attributes) !== '[object Map]' ||
                            body.payload[1] !== undefined || ptt?.voiceType !== 1 ||
                            !ptt.otherBusinessInfo || typeof ptt.otherBusinessInfo.aiVoiceType !== 'number') {
                            callbacks.forEach(callback => callback({}, { ...envelope, type: 'response',
                                promiseStatue: 'rejected' }, new Error('Invalid native voice arguments')));
                            return;
                        }
                    }
                    if (options.switchDuringPreflight && body.cmdName.endsWith('generateMsgUniqueId')) data.header.uid = '999';
                    const response = body.cmdName.endsWith('generateMsgUniqueId') ? '123456789' : { result: options.reject ? 100 : 0 };
                    callbacks.forEach(callback => callback({}, { ...envelope, type: 'response', promiseStatue: 'full' }, response));
                    if (body.cmdName.endsWith('/sendMsg') && !options.reject) {
                        const msg = { peerUid: options.wrongPeer ? '999' : '456', chatType: 2,
                            sendStatus: 2, msgId: body.payload[0].msgId, elements: body.payload[0].msgElements };
                        const payload = { msgList: [msg] };
                        callbacks.forEach(callback => callback({}, { type: 'event' }, { cmdName: 'onMsgInfoListUpdate', payload }));
                    }
                }, 0);
            }
        } }
    });
    const fn = vm.runInContext(script, context);
    return { run: (op, args = {}) => fn(op, { webContentsId: 2, ...args }), data, component, calls, callbacks, context };
}

async function main() {
    let f = fixture();
    const peer = await f.run('active');
    check(peer.peerUid === '456' && peer.chatType === 2, 'Reads exact active-chat store rather than recent contacts');
    check(f.calls.length === 0, 'Active-chat inspection sends no QQ network message');
    const probe = await f.run('probeRpc');
    check(probe.confirmed && f.calls.length === 1, 'Read-only RPC correlates the exact callback');
    await f.run('probeRpc');
    check(f.callbacks.length === 1, 'Repeated requests do not add extra IPC listeners');
    const element = { elementType: 4, elementId: '', pttElement: { md5HexStr: 'abc123' } };
    const sendArgs = { expected: peer, element, requestId: 'fixture' };
    const result = await f.run('sendVoice', sendArgs);
    check(result.messageId === '123456789', 'Voice success requires matching QQ confirmation and message ID');
    const sent = f.calls.at(-1);
    check(sent.body.cmdName.endsWith('/sendMsg') && sent.body.payload[0].msgElements[0].elementType === 4,
        'Submits a native PTT voice element, not an attachment');
    check(sent.body.payload[0].peer.peerUid === '456', 'Send call retains captured exact peer');
    check(Object.prototype.toString.call(sent.body.payload[0].msgAttributeInfos) === '[object Map]' &&
        sent.body.payload[1] === undefined, 'Preserves native Map and undefined argument types observed in real QQ');
    f.data.header.uid = '999';
    const count = f.calls.length;
    await assert.rejects(f.run('sendVoice', sendArgs), /切换/);
    check(f.calls.length === count, 'Changing the active peer cancels before any send call');
    f = fixture({ reject: true });
    await assert.rejects(f.run('sendVoice', sendArgs), /未接受/);
    check(f.calls.filter(c => c.body.cmdName.endsWith('/sendMsg')).length === 1, 'QQ rejection fails once without retry');
    f = fixture({ nativeError: true });
    await assert.rejects(f.run('sendVoice', sendArgs), /sendMsg.*Cannot convert undefined or null to object/);
    check(f.calls.filter(c => c.body.cmdName.endsWith('/sendMsg')).length === 1,
        'Native Error details reach the caller without becoming an empty object or retrying');
    f = fixture({ timeout: true });
    await assert.rejects(f.run('sendVoice', sendArgs), /未知/);
    check(f.calls.filter(c => c.body.cmdName.endsWith('/sendMsg')).length === 0, 'Unconfirmed preflight prevents sending');
    f = fixture({ wrongPeer: true });
    await assert.rejects(f.run('sendVoice', sendArgs), /未知/);
    check(f.calls.filter(c => c.body.cmdName.endsWith('/sendMsg')).length === 1, 'Confirmation from another peer is ignored');
    f = fixture({ sendTimeout: true });
    await assert.rejects(f.run('sendVoice', sendArgs), /未知/);
    check(f.calls.filter(c => c.body.cmdName.endsWith('/sendMsg')).length === 1, 'Send timeout does not retry');
    f = fixture({ switchDuringPreflight: true });
    await assert.rejects(f.run('sendVoice', sendArgs), /切换/);
    check(f.calls.filter(c => c.body.cmdName.endsWith('/sendMsg')).length === 0, 'Peer is rechecked after asynchronous preflight');
    const other = { component: { ctx: { commonAioStore: { curAioData: { chatType: 1,
        header: { uid: 'other', peerName: 'Other' } } } }, subTree: {} } };
    f = fixture();
    f.component.subTree = { children: [other] };
    await assert.rejects(f.run('active'), /唯一/);
    check(f.calls.length === 0, 'Ambiguous active peers fail closed');
    const handlerPath = path.join(__dirname, '../src/Yukkuri.Desktop/Assets/qq-bridge/handler.cjs');
    const handlerModule = { exports: {} };
    const writes = [];
    const localRequire = name => {
        if (name === 'node:fs') return {
            readFileSync: file => file.endsWith('.wav') ? Buffer.alloc(128) : JSON.stringify({ secret: 'a'.repeat(64) }),
            existsSync: () => false, statSync: () => ({ size: 128 }),
            writeFileSync: (file, data) => writes.push({ file, data })
        };
        if (name === './silk/index.cjs') return {
            encode: async () => ({ data: Buffer.from('\x02#!SILK_V3'), duration: 1000 }),
            isSilk: () => true,
            getWavFileInfo: () => ({ chunkInfo: [{ chunkId: 'data', dataOffset: 0, dataLength: 128 }],
                fmt: { numberOfChannels: 1, bitsPerSample: 16 } })
        };
        if (name.endsWith('renderer.cjs')) return renderer;
        if (name === 'electron') return { app: { on: () => {} } };
        return require(name);
    };
    localRequire.resolve = name => name;
    localRequire.cache = {};
    vm.runInNewContext(fs.readFileSync(handlerPath, 'utf8'), {
        require: localRequire, module: handlerModule, exports: handlerModule.exports,
        __dirname: path.dirname(handlerPath), Buffer, console
    });
    const makeWindow = (id, handle) => ({ id, on: () => {}, isFocused: () => false, isVisible: () => true,
        getNativeWindowHandle: () => { const b = Buffer.alloc(8); b.writeBigUInt64LE(BigInt(handle)); return b; },
        webContents: { id, getURL: () => 'app://./renderer/index.html#/chat',
            executeJavaScript: async () => ({ ...peer, name: 'Window ' + id }) } });
    const windows = [makeWindow(2, 222), makeWindow(3, 333)];
    const state = { windows: () => windows, lastWindowId: 2 };
    const request = { action: 'active', parameters: { windowHandle: '333' }, secret: 'a'.repeat(64) };
    const target = await handlerModule.exports(state, request);
    check(target.windowId === 3 && state.lastWindowId === 3, 'Exact native QQ window overrides older focused-window cache');
    await assert.rejects(handlerModule.exports(state, { ...request, parameters: { windowHandle: '999' } }), /关闭/);
    check(true, 'Closed QQ window never falls back to another chat');
    await assert.rejects(handlerModule.exports(state, { ...request, secret: '' }), /校验/);
    check(true, 'Local connection rejects missing automatic secret');
    await assert.rejects(handlerModule.exports(state, { ...request, action: 'eval' }), /不支持/);
    check(true, 'Local bridge has no arbitrary evaluation command');
    await assert.rejects(handlerModule.exports(state, { ...request, action: 'captureStart' }), /不支持/);
    check(true, 'Temporary IPC capture entry points are excluded from the release');
    const voiceFixture = fixture({ strictVoice: true });
    const voiceWindow = makeWindow(2, 222);
    voiceWindow.webContents.executeJavaScript = source => vm.runInContext(source, voiceFixture.context);
    const voiceState = { windows: () => [voiceWindow] };
    const voiceRequest = { action: 'sendVoice', secret: 'a'.repeat(64), parameters: {
        WindowId: 2, ChatType: 2, PeerUid: '456', requestId: 'b'.repeat(32),
        audioPath: path.join(path.dirname(handlerPath), 'Audio', 'b'.repeat(32) + '.wav')
    } };
    const delivered = await handlerModule.exports(voiceState, voiceRequest);
    check(delivered.messageId === '123456789', 'Handler-built voice passes native field and transport validation');
    const submittedPtt = voiceFixture.calls.at(-1).body.payload[0].msgElements[0].pttElement;
    check(submittedPtt.otherBusinessInfo.aiVoiceType === 0 && submittedPtt.autoConvertText === 0 &&
        submittedPtt.storeID === 0, 'Includes the business metadata required by the real native voice API');
    check(writes.length === 1 && submittedPtt.filePath === writes[0].file &&
        submittedPtt.fileSize === String(writes[0].data.length), 'Native voice references the exact encoded file and size');
    await handlerModule.exports(voiceState, voiceRequest);
    check(voiceFixture.calls.filter(c => c.body.cmdName.endsWith('/sendMsg')).length === 1,
        'Repeating the same request ID does not submit another native message');
    console.log('All ' + checks + ' local QQ transport checks passed. No real messages sent.');
}

main().catch(error => { console.error(error); process.exitCode = 1; });

'use strict';
const { parentPort } = require('node:worker_threads');
const { toKana } = require('./kana.cjs');
const { readWav } = require('./wav.cjs');
const { bundledVoices } = require('./voices.cjs');
const path = require('node:path');
const engines = new Map();
let chain = Promise.resolve();
async function run(request) {
    if (request.action === 'discover') return require('./native-engine.cjs').discover(request.path);
    const { text, voice, speed } = request;
    if (typeof text !== 'string' || !text.trim() || text.length > 240) throw new Error('请输入 1 至 240 个字符。');
    if (!Number.isFinite(speed) || speed < 0.65 || speed > 1.6) throw new Error('语速超出范围。');
    const kana = toKana(text);
    if (!kana.trim()) throw new Error('文字中没有可合成的内容。');
    let wav;
    if (voice.kind === 'bundled') {
        if (!bundledVoices.some(v => v.id === voice.id)) throw new Error('无效音色。');
        if (!engines.has(voice.id)) {
            if (engines.size >= 2) {
                const oldest = engines.keys().next().value;
                await engines.get(oldest).destroy(); engines.delete(oldest);
            }
            const { loadAquesTalk } = await import('aquestalk.js');
            engines.set(voice.id, await loadAquesTalk(path.join(__dirname, '../assets/voices', voice.id + '.zip'),
                voice.id + '/AquesTalk.dll', { wasmPath: path.join(__dirname, '../assets/v86.wasm') }));
        }
        wav = Buffer.from(engines.get(voice.id).run(kana, Math.round(speed * 100)));
    } else if (voice.kind === 'native') {
        wav = require('./native-engine.cjs').synthesize(voice, kana, Math.round(speed * 100));
    } else throw new Error('无效音色。');
    const { seconds, rate } = readWav(wav);
    return { wav, seconds, rate, kana };
}
parentPort.on('message', message => {
    chain = chain.then(async () => {
        try { parentPort.postMessage({ id: message.id, ok: true, result: await run(message) }); }
        catch (error) { parentPort.postMessage({ id: message.id, ok: false, error: error.message }); }
    });
});

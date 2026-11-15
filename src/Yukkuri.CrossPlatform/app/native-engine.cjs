'use strict';
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const koffi = require('koffi');
const encoding = require('encoding-japanese');
const { presets, voice10 } = require('./voices.cjs');
const libraries = new Map();
const parameter10 = koffi.struct('YukkuriVoice10', {
    bas: 'int', spd: 'int', vol: 'int', pit: 'int', acc: 'int', lmd: 'int', fsc: 'int'
});
function find(library, names, result, parameters) {
    for (const name of names) {
        try { return library.func(name, result, parameters); } catch (_) {}
    }
    return null;
}
function openLibrary(file) {
    if (libraries.has(file)) return libraries.get(file);
    if (!path.isAbsolute(file) || !fs.statSync(file).isFile()) throw new Error('请选择有效的本机引擎文件。');
    let library;
    try { library = koffi.load(file); }
    catch (_) { throw new Error('无法加载此引擎，请选择适用于当前系统及 CPU 架构的 64 位版本。'); }
    const utf8 = find(library, ['AquesTalk_Synthe_Utf8', 'AquesTalk10_Synthe_Utf8'], 'void *',
        [koffi.pointer(parameter10), 'void *', koffi.out(koffi.pointer('int'))]);
    const synth2 = find(library, ['AquesTalk2_Synthe_Utf8'], 'void *',
        ['void *', 'int', koffi.out(koffi.pointer('int')), 'void *']);
    const is10 = !synth2 && /aq(?:ues)?talk10|aqtk10/i.test(file);
    if (utf8 && !synth2 && !is10) {
        library.unload();
        throw new Error('无法唯一确认此引擎接口版本。请保留官方 AquesTalk1 / 2 / 10 SDK 的原始目录及文件名。');
    }
    const synth = synth2 || utf8 || find(library, is10 ? ['AquesTalk10_Synthe', 'AquesTalk_Synthe'] :
        ['AquesTalk_Synthe'], 'void *', is10 ? [koffi.pointer(parameter10), 'void *',
            koffi.out(koffi.pointer('int'))] : ['void *', 'int', koffi.out(koffi.pointer('int'))]);
    const free = find(library, synth2 ? ['AquesTalk2_FreeWave'] : ['AquesTalk10_FreeWave', 'AquesTalk_FreeWave'],
        'void', ['void *']);
    if (!synth || !free) { library.unload(); throw new Error('此文件不提供受支持的 AquesTalk 接口。'); }
    const engine = synth2 ? 'AquesTalk2' : is10 ? 'AquesTalk10' : 'AquesTalk1';
    for (const kind of ['DEV', 'USR']) {
        const prefix = engine === 'AquesTalk10' ? 'AQTK10' : 'AQTK1';
        const key = process.env[prefix + '_' + kind + '_KEY'];
        if (!key) continue;
        const setter = find(library, [(synth2 ? 'AquesTalk2_' : 'AquesTalk_') +
            (kind === 'DEV' ? 'SetDevKey' : 'SetUsrKey')], 'int', ['str']);
        if (setter && setter(key) !== 0) throw new Error('AquesTalk 授权密钥无效。');
    }
    const value = { engine, synth, free, utf8: !!utf8 };
    libraries.set(file, value);
    return value;
}
function discover(file) {
    const { engine } = openLibrary(file);
    const key = crypto.createHash('sha256').update(file).digest('hex').slice(0, 16);
    const profile = { kind: 'native', path: file, engine };
    if (engine === 'AquesTalk10') return presets.map((name, preset) => ({
        ...profile, id: `${key}-${preset}`, name: 'AT10-' + name, preset
    }));
    if (engine === 'AquesTalk2') {
        const parent = path.dirname(file), root = /^(lib(?:64)?|x64|arm64)$/i.test(path.basename(parent))
            ? path.dirname(parent) : parent;
        const phonts = [];
        function visit(folder, depth = 0) {
            if (depth > 4) return;
            for (const entry of fs.readdirSync(folder, { withFileTypes: true })) {
                const full = path.join(folder, entry.name);
                if (entry.isDirectory()) visit(full, depth + 1);
                else if (/\.phont$/i.test(entry.name)) phonts.push(full);
            }
        }
        visit(root);
        if (!phonts.length) throw new Error('此 SDK 目录中没有找到 AT2 的 .phont 音色文件。');
        return phonts.sort().map((phont, index) => ({ ...profile, id: `${key}-${index}`, phont,
            name: 'AT2-' + path.basename(phont, '.phont').replace(/^(aq|ar)_/, '').toUpperCase() }));
    }
    return [{ ...profile, id: key, name: 'AT1-' + path.basename(path.dirname(file)).toUpperCase() }];
}
function synthesize(voice, kana, speed) {
    const native = openLibrary(voice.path), size = [0];
    const bytes = native.engine === 'AquesTalk2' || native.utf8 ? Buffer.from(kana + '\0', 'utf8') :
        Buffer.from(encoding.convert(encoding.stringToCode(kana + '\0'), { to: 'SJIS', from: 'UNICODE' }));
    let pointer;
    if (native.engine === 'AquesTalk2') pointer = native.synth(bytes, speed, size, fs.readFileSync(voice.phont));
    else if (native.engine === 'AquesTalk10') {
        const values = [...voice10[voice.preset || 0]]; values[1] = speed;
        pointer = native.synth(Object.fromEntries(['bas', 'spd', 'vol', 'pit', 'acc', 'lmd', 'fsc']
            .map((key, index) => [key, values[index]])), bytes, size);
    } else pointer = native.synth(bytes, speed, size);
    if (!pointer) throw new Error('AquesTalk 合成失败，错误码 ' + size[0] + '。');
    try {
        if (size[0] < 44 || size[0] > 30000000) throw new Error('引擎返回了无效音频。');
        return Buffer.from(koffi.decode(pointer, 'uint8_t', size[0]));
    } finally { native.free(pointer); }
}
module.exports = { discover, synthesize };

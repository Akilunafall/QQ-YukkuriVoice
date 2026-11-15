'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const JSZip = require('jszip');
const { Speech } = require('../app/speech.cjs');
const { bundledVoices } = require('../app/voices.cjs');
const { toKana } = require('../app/kana.cjs');
const { readWav, resampleForQQ } = require('../app/wav.cjs');
const engine = new Speech();
test.after(() => engine.dispose());
test('Chinese, kana, digits and punctuation stay local and retain the Windows reading', () => {
    assert.equal(toKana('慢慢来，今天也要度过悠闲的一天。'), 'マンマンライ、ジンティエンイエヤオドゥーグオヨウシエンドーイーティエン。');
    assert.equal(toKana('ゆっくり 12 AB！'), 'ゆっくり イチニ エービー！');
});
test('Every bundled voice preserves its original DLL and companion license bytes', async () => {
    for (const voice of bundledVoices) {
        const zip = await JSZip.loadAsync(fs.readFileSync(path.join(__dirname, '../assets/voices', voice.id + '.zip')));
        for (const file of ['AquesTalk.dll', 'AqLicence.txt']) {
            const actual = await zip.file(voice.id + '/' + file).async('nodebuffer');
            const expected = fs.readFileSync(path.resolve(__dirname, '../../Yukkuri.Desktop/Assets/voices', voice.id, file));
            assert.deepEqual(actual, expected, voice.id + '/' + file);
        }
    }
});
test('Eight actual voices produce audible PCM with working speed control and QQ copies', async () => {
    for (const voice of bundledVoices) {
        const fast = await engine.request({ text: '慢慢来，今天也要度过悠闲的一天。', voice, speed: 1.6 });
        const slow = await engine.request({ text: '慢慢来，今天也要度过悠闲的一天。', voice, speed: .65 });
        const wav = readWav(slow.wav);
        assert(wav.seconds > fast.seconds * 1.4, voice.id + ' changes speed');
        let peak = 0;
        for (let offset = 0; offset < wav.data.length; offset += 2)
            peak = Math.max(peak, Math.abs(wav.data.readInt16LE(offset)));
        assert(peak > 600 && peak <= 32768, voice.id + ' is audible');
        const original = crypto.createHash('sha256').update(slow.wav).digest('hex');
        const converted = readWav(resampleForQQ(slow.wav));
        assert.equal(converted.rate, 24000);
        assert(Math.abs(converted.seconds - wav.seconds) < .002);
        assert.equal(crypto.createHash('sha256').update(slow.wav).digest('hex'), original);
    }
});
test('Invalid parameters and malformed WAV fail clearly', async () => {
    const voice = bundledVoices[0];
    await assert.rejects(engine.request({ text: '', voice, speed: 1 }), /240/);
    await assert.rejects(engine.request({ text: '字'.repeat(241), voice, speed: 1 }), /240/);
    await assert.rejects(engine.request({ text: '你好', voice, speed: NaN }), /语速/);
    await assert.rejects(engine.request({ text: '你好', voice: { ...voice, id: 'outside' }, speed: 1 }), /音色/);
    assert.throws(() => readWav(Buffer.alloc(50)), /WAV/);
});

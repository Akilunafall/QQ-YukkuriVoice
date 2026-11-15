'use strict';
function readWav(input) {
    const bytes = Buffer.from(input);
    if (bytes.length < 44 || bytes.toString('ascii', 0, 4) !== 'RIFF' ||
        bytes.toString('ascii', 8, 12) !== 'WAVE') throw new Error('无效的 WAV 音频。');
    let fmt, data;
    for (let offset = 12; offset + 8 <= bytes.length;) {
        const size = bytes.readUInt32LE(offset + 4), start = offset + 8;
        if (start + size > bytes.length) throw new Error('WAV 数据不完整。');
        const name = bytes.toString('ascii', offset, offset + 4);
        if (name === 'fmt ') {
            if (size < 16) throw new Error('WAV 格式信息不完整。');
            fmt = { encoding: bytes.readUInt16LE(start), channels: bytes.readUInt16LE(start + 2),
                rate: bytes.readUInt32LE(start + 4), bits: bytes.readUInt16LE(start + 14) };
        }
        if (name === 'data') data = bytes.subarray(start, start + size);
        offset = start + size + (size & 1);
    }
    if (!fmt || !data || fmt.encoding !== 1 || fmt.channels !== 1 || fmt.bits !== 16 ||
        fmt.rate < 4000 || fmt.rate > 192000 || !data.length || data.length % 2)
        throw new Error('需要 16 位单声道 PCM WAV。');
    return { ...fmt, data, seconds: data.length / 2 / fmt.rate };
}
function writeWav(samples, rate) {
    const data = Buffer.isBuffer(samples) ? samples : Buffer.from(samples.buffer, samples.byteOffset, samples.byteLength);
    const output = Buffer.alloc(44 + data.length);
    output.write('RIFF'); output.writeUInt32LE(output.length - 8, 4); output.write('WAVE', 8);
    output.write('fmt ', 12); output.writeUInt32LE(16, 16); output.writeUInt16LE(1, 20);
    output.writeUInt16LE(1, 22); output.writeUInt32LE(rate, 24); output.writeUInt32LE(rate * 2, 28);
    output.writeUInt16LE(2, 32); output.writeUInt16LE(16, 34); output.write('data', 36);
    output.writeUInt32LE(data.length, 40); data.copy(output, 44);
    return output;
}
function resampleForQQ(input) {
    const wav = readWav(input);
    if (wav.seconds > 58) throw new Error('QQ 单条语音最多 58 秒，请缩短文字。');
    if (wav.rate === 24000) return Buffer.from(input);
    const count = Math.round(wav.data.length / 2 * 24000 / wav.rate);
    const output = Buffer.alloc(count * 2), last = wav.data.length / 2 - 1;
    // Linear interpolation is applied only to the QQ sending copy.
    for (let index = 0; index < count; index++) {
        const position = index * wav.rate / 24000, left = Math.min(last, Math.floor(position));
        const fraction = position - left, right = Math.min(last, left + 1);
        output.writeInt16LE(Math.round(wav.data.readInt16LE(left * 2) * (1 - fraction) +
            wav.data.readInt16LE(right * 2) * fraction), index * 2);
    }
    return writeWav(output, 24000);
}
module.exports = { readWav, writeWav, resampleForQQ };

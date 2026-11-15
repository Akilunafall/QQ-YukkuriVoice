'use strict';
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const assert = require('node:assert/strict');
const { Readable, Writable } = require('node:stream');
const { pipeline } = require('node:stream/promises');
const zlib = require('node:zlib');
const tar = require('tar-stream');
const JSZip = require('jszip');
const plist = require('plist');
const root = path.resolve(__dirname, '../../..');
const output = path.join(root, 'dist/YukkuriVoice-1.3.0-CrossPlatform');
function machine(bytes, platform, arch) {
    if (platform === 'darwin') {
        assert.equal(bytes.readUInt32LE(0), 0xfeedfacf, 'Mach-O 64-bit executable');
        assert.equal(bytes.readUInt32LE(4), arch === 'arm64' ? 0x100000c : 0x1000007, 'Matching macOS CPU');
    } else {
        assert.equal(bytes.subarray(0, 4).toString('hex'), '7f454c46', 'ELF executable');
        assert.equal(bytes[4], 2, 'ELF 64-bit');
        assert.equal(bytes.readUInt16LE(18), arch === 'arm64' ? 183 : 62, 'Matching Linux CPU');
    }
}
function required(application) {
    const files = ['package.json', 'app/main.cjs', 'app/preload.cjs', 'app/speech-worker.cjs',
        'app/ui/index.html', 'app/ui/style.css', 'app/ui/renderer.js', 'assets/v86.wasm',
        'assets/qq-bridge/silk/silk.wasm', 'assets/pinyin-kana.tsv', 'assets/使用说明.md',
        'node_modules/aquestalk.js/dist/index.js', 'node_modules/v86/build/libv86.mjs',
        'node_modules/koffi/src/koffi/index.cjs', 'node_modules/koffi/src/koffi/src/static.cjs',
        'node_modules/lucide/dist/umd/lucide.min.js', 'node_modules/pinyin-pro/dist/index.js'];
    for (const file of files) assert(application.has(file), 'Required application file: ' + file);
    assert.equal(JSON.parse(application.get('package.json')).version, '1.3.0');
    for (const file of application.keys())
        assert(!/(^|\/)(\.env|connection\.json|history\.json|settings\.json)$/.test(file), 'No personal configuration');
}
async function checkVoices(application) {
    for (const id of ['f1', 'f2', 'm1', 'm2', 'dvd', 'imd1', 'jgr', 'r1']) {
        const zip = await JSZip.loadAsync(application.get('assets/voices/' + id + '.zip'));
        for (const file of ['AquesTalk.dll', 'AqLicence.txt'])
            assert.deepEqual(await zip.file(id + '/' + file).async('nodebuffer'),
                fs.readFileSync(path.join(root, 'src/Yukkuri.Desktop/Assets/voices', id, file)));
    }
}
async function verifyMac(file, arch) {
    const zip = await JSZip.loadAsync(fs.readFileSync(file));
    const prefix = 'YukkuriVoice.app/Contents/';
    machine(await zip.file(prefix + 'MacOS/Electron').async('nodebuffer'), 'darwin', arch);
    assert(Number(zip.file(prefix + 'MacOS/Electron').unixPermissions) & 0o111, 'macOS executable permission');
    const info = plist.parse(await zip.file(prefix + 'Info.plist').async('string'));
    assert.equal(info.CFBundleIdentifier, 'io.github.akilunafall.yukkurivoice');
    assert.equal(info.CFBundleShortVersionString, '1.3.0');
    const application = new Map();
    for (const entry of Object.values(zip.files)) if (entry.name.startsWith(prefix + 'Resources/app/') && !entry.dir)
        application.set(entry.name.slice((prefix + 'Resources/app/').length), await entry.async('nodebuffer'));
    required(application); await checkVoices(application);
    const native = [...application.entries()].find(([name]) =>
        name.startsWith(`node_modules/@koromix/koffi-darwin-${arch}/`) && name.endsWith('.node'));
    assert(native, 'Matching target native engine loader'); machine(native[1], 'darwin', arch);
    console.log('PASS macOS ' + arch + ': CPU, executable bit, app metadata, runtime modules, eight original voices/licenses');
}
async function readTar(stream, visit) {
    const extract = tar.extract();
    extract.on('entry', (header, entry, next) => {
        const chunks = [], capture = visit(header);
        entry.on('data', chunk => { if (capture) chunks.push(chunk); });
        entry.on('end', () => { if (capture) capture(Buffer.concat(chunks)); next(); });
        entry.on('error', error => extract.destroy(error));
    });
    await pipeline(stream, zlib.createGunzip(), extract);
}
async function verifyLinux(file, arch) {
    const application = new Map(), prefix = 'YukkuriVoice/resources/app/';
    let executable, mode, launcher;
    await readTar(fs.createReadStream(file), header => {
        if (header.name.startsWith(prefix)) return bytes => application.set(header.name.slice(prefix.length), bytes);
        if (header.name === 'YukkuriVoice/yukkuri-voice') { mode = header.mode; return bytes => { executable = bytes; }; }
        if (header.name === 'YukkuriVoice/启动油库里语音.sh') return bytes => { launcher = bytes; };
        return null;
    });
    machine(executable, 'linux', arch); assert(mode & 0o111); assert(launcher.toString().startsWith('#!/bin/sh\n'));
    required(application); await checkVoices(application);
    const native = [...application.entries()].find(([name]) =>
        name.startsWith(`node_modules/@koromix/koffi-linux-${arch}/`) && name.endsWith('.node'));
    assert(native); machine(native[1], 'linux', arch);
    console.log('PASS Linux ' + arch + ': CPU, executable bit, launcher, runtime modules, eight original voices/licenses');
}
async function verifyDeb(file, arch) {
    const bytes = fs.readFileSync(file);
    assert.equal(bytes.toString('ascii', 0, 8), '!<arch>\n');
    const members = new Map();
    for (let offset = 8; offset + 60 <= bytes.length;) {
        const name = bytes.toString('ascii', offset, offset + 16).trim().replace(/\/$/, '');
        const size = Number(bytes.toString('ascii', offset + 48, offset + 58).trim());
        assert(Number.isSafeInteger(size) && size > 0 && offset + 60 + size <= bytes.length);
        members.set(name, bytes.subarray(offset + 60, offset + 60 + size));
        offset += 60 + size + (size & 1);
    }
    assert.equal(members.get('debian-binary').toString(), '2.0\n');
    let control, sandbox = false;
    await readTar(Readable.from([members.get('control.tar.gz')]), header =>
        header.name === 'control' ? bytes => { control = bytes.toString(); } : null);
    assert(control.includes('Architecture: ' + (arch === 'x64' ? 'amd64' : 'arm64')));
    assert(control.includes('Version: 1.3.0'));
    await readTar(Readable.from([members.get('data.tar.gz')]), header => {
        if (header.name === 'opt/yukkuri-voice/chrome-sandbox') {
            assert.equal(header.mode, 0o4755); assert.equal(header.uid, 0); sandbox = true;
        }
        return null;
    });
    assert(sandbox, 'Root-owned sandbox helper in system installer');
    console.log('PASS Debian ' + arch + ': ar container, package metadata, root-owned sandbox helper');
}
async function main() {
    const filter = process.argv[2];
    for (const arch of ['x64', 'arm64']) {
        if (!filter || filter === 'mac') await verifyMac(path.join(output, `YukkuriVoice-1.3.0-mac-${arch}.zip`), arch);
        if (!filter || filter === 'linux') {
            await verifyLinux(path.join(output, `YukkuriVoice-1.3.0-linux-${arch}.tar.gz`), arch);
            await verifyDeb(path.join(output, `YukkuriVoice-1.3.0-linux-${arch}.deb`), arch);
        }
    }
}
main().catch(error => { console.error(error); process.exitCode = 1; });

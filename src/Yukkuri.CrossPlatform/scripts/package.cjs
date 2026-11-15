'use strict';
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const zlib = require('node:zlib');
const { Readable } = require('node:stream');
const { pipeline } = require('node:stream/promises');
const { spawnSync } = require('node:child_process');
const JSZip = require('jszip');
const tar = require('tar-stream');
const plist = require('plist');
const root = path.resolve(__dirname, '..');
const repository = path.resolve(root, '../..');
const metadata = require('../package.json');
const lock = require('../package-lock.json');
const electron = require('electron/package.json');
const platform = process.argv[2], arch = process.argv[3];
if (!['darwin', 'linux'].includes(platform) || !['x64', 'arm64'].includes(arch))
    throw new Error('Usage: node scripts/package.cjs <darwin|linux> <x64|arm64>');
const cache = path.join(repository, '.resources/cross-packaging');
const output = path.join(repository, 'dist', 'YukkuriVoice-' + metadata.version + '-CrossPlatform');
fs.mkdirSync(cache, { recursive: true });
fs.mkdirSync(output, { recursive: true });
const releaseTime = new Date('2026-11-15T00:00:00Z');
function command(executable, args) {
    const result = spawnSync(executable, args, { stdio: 'inherit' });
    if (result.error) throw result.error;
    if (result.status !== 0) throw new Error(executable + ' failed with exit code ' + result.status);
}
async function createDiskImage(folder, target) {
    for (let attempt = 1; attempt <= 3; attempt++) {
        const result = spawnSync('/usr/bin/hdiutil', ['create', '-volname', 'YukkuriVoice',
            '-fs', 'HFS+', '-srcfolder', folder, '-ov', '-format', 'UDZO', target],
        { encoding: 'utf8', timeout: 180000 });
        if (result.stdout) process.stdout.write(result.stdout);
        if (result.stderr) process.stderr.write(result.stderr);
        if (result.status === 0) {
            command('/usr/bin/hdiutil', ['verify', target]);
            return;
        }
        if (attempt === 3 || !/Resource busy/i.test(result.stderr || ''))
            throw result.error || new Error('macOS disk image creation failed.');
        console.log('Disk image service was busy; retrying with the same signed application.');
        await new Promise(resolve => setTimeout(resolve, 5000));
    }
}
function hash(bytes, algorithm = 'sha256', format = 'hex') {
    return crypto.createHash(algorithm).update(bytes).digest(format);
}
function addDirectory(files, source, destination, filter = () => true) {
    for (const entry of fs.readdirSync(source, { withFileTypes: true })) {
        const relative = destination + '/' + entry.name, full = path.join(source, entry.name);
        if (!filter(relative, entry)) continue;
        if (entry.isDirectory()) addDirectory(files, full, relative, filter);
        else if (entry.isFile()) files.set(relative, fs.readFileSync(full));
        else throw new Error('Unexpected local symbolic link: ' + relative);
    }
}
function findPackage(name, from) {
    for (let folder = from;; folder = path.dirname(folder)) {
        const candidate = path.join(folder, 'node_modules', name);
        if (fs.existsSync(path.join(candidate, 'package.json'))) return candidate;
        if (folder === path.dirname(folder)) throw new Error('Dependency not found: ' + name);
    }
}
async function applicationFiles() {
    const files = new Map();
    addDirectory(files, path.join(root, 'app'), 'app');
    addDirectory(files, path.join(root, 'assets'), 'assets');
    files.set('package.json', Buffer.from(JSON.stringify({
        name: metadata.name, productName: metadata.productName, version: metadata.version,
        description: metadata.description, author: metadata.author, main: metadata.main
    }, null, 2)));
    const seen = new Map();
    function dependency(name, from = root) {
        const folder = findPackage(name, from);
        if (seen.get(name) === folder) return;
        if (seen.has(name)) throw new Error('Conflicting nested dependency versions: ' + name);
        seen.set(name, folder);
        const info = JSON.parse(fs.readFileSync(path.join(folder, 'package.json'), 'utf8'));
        addDirectory(files, folder, 'node_modules/' + name, relative => {
            if (relative.includes('/node_modules/', 13 + name.length)) return false;
            const local = relative.slice(('node_modules/' + name + '/').length);
            if (/^(test|tests|benchmark|benchmarks|examples|\.github|doc|docs)\//.test(local)) return false;
            if (name === 'aquestalk.js' && /^(src|voices)\//.test(local)) return false;
            if (name === 'koffi' && /^(vendor|lib|build)\//.test(local)) return false;
            if (name === 'koffi' && local.startsWith('src/') && local !== 'src' &&
                local !== 'src/koffi' && !local.startsWith('src/koffi/')) return false;
            if (name === 'koffi' && local.startsWith('src/koffi/') && !/\.cjs$/.test(local) &&
                !['src/koffi/src'].includes(local)) return false;
            return !/\.(map|ts|o|a|lib|exp)$/.test(local) || local.endsWith('.d.ts');
        });
        for (const child of Object.keys(info.dependencies || {})) dependency(child, folder);
    }
    for (const name of Object.keys(metadata.dependencies)) dependency(name);
    const ffiName = `@koromix/koffi-${platform}-${arch}`;
    const ffi = lock.packages['node_modules/' + ffiName];
    if (!ffi?.integrity || !ffi.resolved) throw new Error('Missing pinned target FFI package.');
    const archive = path.join(cache, `koffi-${platform}-${arch}-${ffi.version}.tgz`);
    if (!fs.existsSync(archive)) {
        const response = await fetch(ffi.resolved, { signal: AbortSignal.timeout(120000) });
        if (!response.ok) throw new Error('Unable to download target FFI package: ' + response.status);
        await pipeline(Readable.fromWeb(response.body), fs.createWriteStream(archive));
    }
    const ffiBytes = fs.readFileSync(archive);
    const [algorithm, digest] = ffi.integrity.split('-', 2);
    if (hash(ffiBytes, algorithm, 'base64') !== digest) throw new Error('Target FFI integrity mismatch.');
    const extract = tar.extract();
    const completed = new Promise((resolve, reject) => { extract.on('finish', resolve); extract.on('error', reject); });
    extract.on('entry', (header, stream, next) => {
        const chunks = [];
        stream.on('data', chunk => chunks.push(chunk));
        stream.on('end', () => {
            if (header.type === 'file') {
                const local = header.name.replace(/^package\//, '');
                if (local.includes('..') || path.isAbsolute(local)) return extract.destroy(new Error('Invalid target package path.'));
                files.set('node_modules/' + ffiName + '/' + local, Buffer.concat(chunks));
            }
            next();
        });
        stream.on('error', error => extract.destroy(error));
    });
    extract.end(zlib.gunzipSync(ffiBytes)); await completed;
    files.set('assets/使用说明.md', fs.readFileSync(path.join(repository, 'docs/跨平台版本.md')));
    return files;
}
function tarEntry(pack, name, data, options = {}) {
    return new Promise((resolve, reject) => {
        const bytes = Buffer.isBuffer(data) ? data : Buffer.from(data || '');
        pack.entry({ name, size: bytes.length, mode: 0o644, uid: 0, gid: 0,
            mtime: releaseTime, type: 'file', ...options }, bytes, error => error ? reject(error) : resolve());
    });
}
async function linuxArchive(runtime, application, target, prefix, deb = false) {
    const pack = tar.pack();
    const saved = pipeline(pack, zlib.createGzip({ level: 6 }), fs.createWriteStream(target));
    const launcher = '#!/bin/sh\nset -eu\nAPP_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)\nexec "$APP_DIR/yukkuri-voice" "$@"\n';
    try {
        for (const entry of Object.values(runtime.files)) {
            if (entry.dir || entry.name === 'resources/default_app.asar') continue;
            const local = entry.name === 'electron' ? 'yukkuri-voice' : entry.name;
            const permissions = Number(entry.unixPermissions) || 0o100644;
            const isLink = (permissions & 0o170000) === 0o120000;
            const data = await entry.async('nodebuffer');
            await tarEntry(pack, prefix + '/' + local, isLink ? Buffer.alloc(0) : data, {
                mode: local === 'chrome-sandbox' && deb ? 0o4755 : permissions & 0o777,
                type: isLink ? 'symlink' : 'file', ...(isLink ? { linkname: data.toString('utf8') } : {})
            });
        }
        for (const [name, bytes] of application)
            await tarEntry(pack, prefix + '/resources/app/' + name, bytes);
        await tarEntry(pack, prefix + '/启动油库里语音.sh', launcher, { mode: 0o755 });
        await tarEntry(pack, prefix + '/使用说明.md', fs.readFileSync(path.join(repository, 'docs/跨平台版本.md')));
        await tarEntry(pack, prefix + '/THIRD-PARTY-NOTICES.md', fs.readFileSync(path.join(repository, 'THIRD-PARTY-NOTICES.md')));
        if (deb) {
            await tarEntry(pack, 'usr/bin/yukkuri-voice', '#!/bin/sh\nexec /opt/yukkuri-voice/yukkuri-voice "$@"\n', { mode: 0o755 });
            await tarEntry(pack, 'usr/share/applications/yukkuri-voice.desktop', [
                '[Desktop Entry]', 'Name=YukkuriVoice', 'Name[zh_CN]=油库里语音',
                'Comment=Offline Yukkuri voice synthesis', 'Exec=/usr/bin/yukkuri-voice',
                'Icon=yukkuri-voice', 'Terminal=false', 'Type=Application', 'Categories=AudioVideo;Audio;', ''
            ].join('\n'));
            await tarEntry(pack, 'usr/share/icons/hicolor/256x256/apps/yukkuri-voice.png',
                fs.readFileSync(path.join(root, 'assets/app.png')));
            await tarEntry(pack, 'usr/share/doc/yukkuri-voice/copyright',
                fs.readFileSync(path.join(repository, 'THIRD-PARTY-NOTICES.md')));
        }
        pack.finalize(); await saved;
    } catch (error) { pack.destroy(error); await saved.catch(() => {}); throw error; }
}
async function controlArchive() {
    const pack = tar.pack(), chunks = [];
    const done = new Promise((resolve, reject) => {
        const stream = pack.pipe(zlib.createGzip({ level: 6 }));
        stream.on('data', chunk => chunks.push(chunk)); stream.on('end', () => resolve(Buffer.concat(chunks)));
        stream.on('error', reject);
    });
    await tarEntry(pack, 'control', [
        'Package: yukkuri-voice', `Version: ${metadata.version}`, 'Section: sound', 'Priority: optional',
        'Architecture: ' + (arch === 'x64' ? 'amd64' : 'arm64'), 'Maintainer: Akilunafall',
        'Homepage: https://github.com/Akilunafall/QQ-YukkuriVoice',
        'Depends: libc6 (>= 2.35), libgtk-3-0, libnss3, libnspr4, libatk1.0-0, libatk-bridge2.0-0, libdrm2, libxkbcommon0, libx11-6, libxcb1, libxcomposite1, libxdamage1, libxext6, libxfixes3, libxrandr2, libgbm1, libasound2 | libasound2t64',
        'Description: Offline Yukkuri voice synthesis',
        ' Includes eight original AquesTalk1 voices, a WebAssembly emulator,',
        ' a floating editor and WAV export. QQ integration is experimental.', ''
    ].join('\n'));
    pack.finalize(); return done;
}
function arMember(name, data) {
    const header = Buffer.from((name + '/').padEnd(16) + '0'.padEnd(12) +
        '0'.padEnd(6) + '0'.padEnd(6) + '100644'.padEnd(8) + String(data.length).padEnd(10) + '`\n', 'ascii');
    return Buffer.concat([header, data, data.length % 2 ? Buffer.from('\n') : Buffer.alloc(0)]);
}
async function main() {
    const { downloadArtifact } = await import('@electron/get');
    const archive = await downloadArtifact({
        version: electron.version, artifactName: 'electron', platform, arch,
        cacheRoot: path.join(cache, 'electron'), checksums: require('electron/checksums.json')
    });
    console.log(`Verified Electron ${electron.version} ${platform}-${arch}.`);
    const runtime = await JSZip.loadAsync(fs.readFileSync(archive), { createFolders: false });
    const application = await applicationFiles();
    const label = platform === 'darwin' ? 'mac' : 'linux';
    const name = `YukkuriVoice-${metadata.version}-${label}-${arch}`;
    const artifacts = [];
    if (platform === 'darwin') {
        for (const entry of Object.values(runtime.files)) {
            if (entry.name.endsWith('Resources/default_app.asar') ||
                entry.name.startsWith('Electron.app/Contents/_CodeSignature/')) {
                delete runtime.files[entry.name]; continue;
            }
            if (entry.name.startsWith('Electron.app/')) {
                const old = entry.name;
                entry.name = 'YukkuriVoice.app/' + old.slice('Electron.app/'.length);
                delete runtime.files[old]; runtime.files[entry.name] = entry;
            }
        }
        const infoPath = 'YukkuriVoice.app/Contents/Info.plist';
        const info = plist.parse(await runtime.file(infoPath).async('string'));
        info.CFBundleName = 'YukkuriVoice'; info.CFBundleDisplayName = 'YukkuriVoice';
        info.CFBundleIdentifier = 'io.github.akilunafall.yukkurivoice';
        info.CFBundleShortVersionString = metadata.version; info.CFBundleVersion = metadata.version;
        info.LSMinimumSystemVersion = '12.0'; info.LSApplicationCategoryType = 'public.app-category.utilities';
        runtime.file(infoPath, plist.build(info), { unixPermissions: 0o100644, date: releaseTime });
        for (const [local, bytes] of application)
            runtime.file('YukkuriVoice.app/Contents/Resources/app/' + local, bytes,
                { unixPermissions: 0o100644, date: releaseTime, createFolders: false });
        runtime.file('使用说明.md', fs.readFileSync(path.join(repository, 'docs/跨平台版本.md')),
            { unixPermissions: 0o100644, date: releaseTime });
        runtime.file('THIRD-PARTY-NOTICES.md', fs.readFileSync(path.join(repository, 'THIRD-PARTY-NOTICES.md')),
            { unixPermissions: 0o100644, date: releaseTime });
        const target = path.join(output, name + '.zip');
        if (process.platform === 'darwin') {
            const stage = fs.mkdtempSync(path.join(cache, `mac-${arch}-`));
            command('/usr/bin/ditto', ['-x', '-k', archive, stage]);
            const app = path.join(stage, 'YukkuriVoice.app');
            fs.renameSync(path.join(stage, 'Electron.app'), app);
            fs.rmSync(path.join(app, 'Contents/Resources/default_app.asar'), { force: true });
            fs.writeFileSync(path.join(app, 'Contents/Info.plist'), plist.build(info));
            for (const [local, bytes] of application) {
                const full = path.join(app, 'Contents/Resources/app', local);
                fs.mkdirSync(path.dirname(full), { recursive: true }); fs.writeFileSync(full, bytes);
            }
            command('/usr/bin/codesign', ['--force', '--deep', '--sign', '-', '--timestamp=none',
                '--entitlements', path.join(__dirname, 'entitlements.mac.plist'), app]);
            command('/usr/bin/codesign', ['--verify', '--deep', '--strict', '--verbose=2', app]);
            command('/usr/bin/ditto', ['-c', '-k', '--sequesterRsrc', '--keepParent', app, target]);
            const diskFolder = path.join(stage, 'disk');
            fs.mkdirSync(diskFolder);
            fs.renameSync(app, path.join(diskFolder, 'YukkuriVoice.app'));
            fs.symlinkSync('/Applications', path.join(diskFolder, 'Applications'));
            fs.copyFileSync(path.join(repository, 'docs/跨平台版本.md'), path.join(diskFolder, '使用说明.md'));
            const diskImage = path.join(output, name + '.dmg');
            await createDiskImage(diskFolder, diskImage);
            artifacts.push(diskImage);
        } else {
            await pipeline(runtime.generateNodeStream({ type: 'nodebuffer', platform: 'UNIX',
                compression: 'DEFLATE', compressionOptions: { level: 6 }, streamFiles: true }),
            fs.createWriteStream(target));
        }
        artifacts.push(target);
    } else {
        const portable = path.join(output, name + '.tar.gz');
        await linuxArchive(runtime, application, portable, 'YukkuriVoice');
        artifacts.push(portable);
        const dataFile = path.join(cache, `data-${arch}.tar.gz`);
        await linuxArchive(runtime, application, dataFile, 'opt/yukkuri-voice', true);
        const installer = path.join(output, name + '.deb');
        fs.writeFileSync(installer, Buffer.concat([Buffer.from('!<arch>\n'), arMember('debian-binary', Buffer.from('2.0\n')),
            arMember('control.tar.gz', await controlArchive()), arMember('data.tar.gz', fs.readFileSync(dataFile))]));
        artifacts.push(installer);
    }
    const records = artifacts.map(file => ({ file: path.basename(file), bytes: fs.statSync(file).size,
        sha256: hash(fs.readFileSync(file)), platform, arch, version: metadata.version, electron: electron.version,
        targetRuntimeTested: false, qqTested: false, macAdHocSigned: platform === 'darwin' && process.platform === 'darwin',
        appleNotarized: false }));
    fs.writeFileSync(path.join(output, name + '.json'), JSON.stringify(records, null, 2));
    for (const artifact of records) console.log(`${artifact.file}: ${(artifact.bytes / 1048576).toFixed(1)} MiB SHA256 ${artifact.sha256}`);
}
main().catch(error => { console.error(error); process.exitCode = 1; });

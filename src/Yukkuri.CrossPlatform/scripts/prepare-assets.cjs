'use strict';
const fs = require('node:fs');
const path = require('node:path');
const JSZip = require('jszip');
const root = path.resolve(__dirname, '..');
const source = path.resolve(root, '../Yukkuri.Desktop/Assets');
const output = path.join(root, 'assets');
fs.mkdirSync(output, { recursive: true });
for (const file of ['app.png', 'pinyin-kana.tsv'])
    fs.copyFileSync(path.join(source, file), path.join(output, file));
for (const file of ['renderer.cjs', 'silk/index.cjs', 'silk/silk.wasm', 'silk/LICENSE.txt']) {
    const target = path.join(output, 'qq-bridge', file);
    fs.mkdirSync(path.dirname(target), { recursive: true });
    fs.copyFileSync(path.join(source, 'qq-bridge', file), target);
}
fs.copyFileSync(path.resolve(root, '../../THIRD-PARTY-NOTICES.md'), path.join(output, 'THIRD-PARTY-NOTICES.md'));
fs.copyFileSync(path.join(root, 'node_modules/aquestalk.js/voices/v86.wasm'), path.join(output, 'v86.wasm'));
async function prepareVoices() {
    fs.mkdirSync(path.join(output, 'voices'), { recursive: true });
    for (const id of ['f1', 'f2', 'm1', 'm2', 'dvd', 'imd1', 'jgr', 'r1']) {
        const zip = new JSZip();
        for (const file of ['AquesTalk.dll', 'AqLicence.txt'])
            zip.file(id + '/' + file, fs.readFileSync(path.join(source, 'voices', id, file)));
        fs.writeFileSync(path.join(output, 'voices', id + '.zip'),
            await zip.generateAsync({ type: 'nodebuffer', compression: 'DEFLATE' }));
    }
    console.log('Prepared eight unmodified voices with original licenses, local WASM, SILK and QQ transport.');
}
prepareVoices().catch(error => { console.error(error); process.exitCode = 1; });

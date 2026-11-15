'use strict';
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const assert = require('node:assert/strict');
const root = path.resolve(__dirname, '../../..');
const output = path.join(root, 'dist/YukkuriVoice-1.3.0-CrossPlatform');
const records = [];
for (const platform of ['mac', 'linux']) for (const arch of ['x64', 'arm64']) {
    const manifest = JSON.parse(fs.readFileSync(path.join(output, `YukkuriVoice-1.3.0-${platform}-${arch}.json`)));
    for (const record of manifest) {
        const file = path.join(output, record.file);
        assert.equal(record.version, '1.3.0');
        assert.equal(record.targetRuntimeTested, true, 'Target system launch must have passed: ' + record.file);
        assert.equal(record.qqTested, false, 'This release did not authorize QQ testing');
        if (platform === 'mac') assert.equal(record.macAdHocSigned, true);
        assert.equal(fs.statSync(file).size, record.bytes, 'Artifact size matches');
        assert.equal(crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex'), record.sha256,
            'Artifact hash matches: ' + record.file);
        records.push(record);
    }
}
assert.equal(records.length, 8, 'Expected two archive/install formats for all four platform/CPU targets');
const lines = records.sort((a, b) => a.file.localeCompare(b.file)).map(record => `${record.sha256}  ${record.file}`);
fs.writeFileSync(path.join(output, 'SHA256SUMS.txt'), lines.join('\n') + '\n');
fs.writeFileSync(path.join(output, 'build-manifest.json'), JSON.stringify({
    version: '1.3.0', sourceCommit: '85a2675a9c23c3970ab06520c638a4df4ca61b57',
    workflowRun: 'https://github.com/Akilunafall/QQ-YukkuriVoice/actions/runs/43712949569',
    targetSystemSynthesisAndUiPassed: true, qqTested: false, artifacts: records
}, null, 2));
fs.copyFileSync(path.join(root, 'docs/跨平台版本.md'), path.join(output, '使用说明.md'));
console.log('Verified all eight final artifacts and wrote SHA256SUMS.txt, build-manifest.json and 使用说明.md.');

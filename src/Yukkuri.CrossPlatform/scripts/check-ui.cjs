'use strict';
const { _electron } = require('@playwright/test');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const assert = require('node:assert/strict');
async function main() {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), 'yukkuri-ui-'));
    const output = path.resolve(__dirname, '../../../.test-data/cross-platform-ui');
    fs.mkdirSync(output, { recursive: true });
    const application = await _electron.launch({
        ...(process.env.YUKKURI_PACKAGED_EXECUTABLE ? { executablePath: process.env.YUKKURI_PACKAGED_EXECUTABLE, args: [] } :
            { args: [path.resolve(__dirname, '..')] }),
        env: { ...process.env, YUKKURI_UI_TEST_ROOT: root }
    });
    const errors = [];
    try {
        const page = await application.firstWindow();
        await application.evaluate(({ app }) => {
            process.getBuiltinModule('module').createRequire(app.getAppPath() + '/package.json')('./app/native-engine.cjs');
        });
        page.on('pageerror', error => errors.push(error.message));
        await page.locator('#voice option').first().waitFor({ state: 'attached' });
        assert.equal(await page.locator('#voice option').count(), 8);
        await page.locator('#auto-generate').uncheck();
        await page.locator('#text').fill('慢慢来，今天也要度过悠闲的一天。');
        await page.locator('#generate').click();
        await page.waitForFunction(() => document.querySelector('#status').textContent === '语音已就绪');
        assert.match(await page.locator('#duration').textContent(), /^[1-9]/);
        assert(await page.locator('#send').isDisabled(), 'QQ sending stays unavailable without connection');
        const pixels = await page.locator('#waveform').evaluate(canvas =>
            [...canvas.getContext('2d').getImageData(0, 0, canvas.width, canvas.height).data]
                .filter((value, index) => index % 4 === 3 && value > 0).length);
        assert(pixels > 500, 'waveform contains actual audio data');
        for (const size of [{ width: 1180, height: 800 }, { width: 800, height: 700 }]) {
            await page.setViewportSize(size);
            assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), 'No horizontal overflow');
            await page.screenshot({ path: path.join(output, `compose-${size.width}.png`) });
        }
        const floating = application.waitForEvent('window');
        await page.locator('#floating').click();
        const mini = await floating;
        mini.on('pageerror', error => errors.push(error.message));
        await mini.locator('#mini-text').waitFor();
        await mini.locator('#mini-text').fill('悬浮窗编辑同步测试。');
        await page.waitForFunction(() => document.querySelector('#text').value === '悬浮窗编辑同步测试。');
        await mini.locator('#mini-text').press('Control+Enter');
        await mini.waitForFunction(() => document.querySelector('#mini-status').textContent === '语音已就绪');
        await mini.screenshot({ path: path.join(output, 'floating.png') });
        await mini.close();
        await page.locator('[data-page=history]').click();
        assert.equal(await page.locator('.history-row').count(), 2);
        await page.screenshot({ path: path.join(output, 'history.png') });
        await page.locator('[data-page=settings]').click();
        await page.screenshot({ path: path.join(output, 'settings.png') });
        assert.deepEqual(errors, [], 'No renderer errors');
        console.log('UI passed: actual synthesis, 8 voices, waveform pixels, desktop/narrow layout, editable floating sync, history. QQ was not started or tested.');
    } finally { await application.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });

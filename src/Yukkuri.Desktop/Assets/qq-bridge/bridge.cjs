'use strict';

const fs = require('node:fs');
const net = require('node:net');
const path = require('node:path');
const { BrowserWindow } = require('electron');
const config = JSON.parse(fs.readFileSync(path.join(__dirname, 'connection.json'), 'utf8'));
if (globalThis.__yukkuriVoiceBridge) {
    module.exports = globalThis.__yukkuriVoiceBridge;
} else {
    const state = { version: 2, pid: process.pid, windows: () => BrowserWindow.getAllWindows() };
    const server = net.createServer(socket => {
        let input = '';
        let handled = false;
        socket.setEncoding('utf8');
        socket.setTimeout(60000, () => socket.destroy());
        socket.on('error', () => {});
        socket.on('data', async chunk => {
            if (handled) return;
            input += chunk;
            if (input.length > 65536) return socket.destroy();
            if (!input.includes('\n')) return;
            handled = true;
            socket.pause();
            try {
                const request = JSON.parse(input.slice(0, input.indexOf('\n')));
                const handler = path.join(__dirname, 'handler.cjs');
                delete require.cache[require.resolve(handler)];
                const result = await require(handler)(state, request);
                socket.end(JSON.stringify({ ok: true, data: result }) + '\n');
            } catch (error) {
                socket.end(JSON.stringify({ ok: false, error: error.message }) + '\n');
            }
        });
    });
    server.on('error', error => {
        fs.writeFileSync(path.join(__dirname, 'bridge-error.txt'), error.message, 'utf8');
    });
    server.listen('\\\\.\\pipe\\' + config.pipe);
    state.server = server;
    globalThis.__yukkuriVoiceBridge = state;
    module.exports = state;
}

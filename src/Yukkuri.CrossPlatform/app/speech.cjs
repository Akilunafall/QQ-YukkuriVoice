'use strict';
const path = require('node:path');
const { Worker } = require('node:worker_threads');
class Speech {
    constructor() { this.pending = new Map(); this.sequence = 0; this.worker = null; }
    start() {
        if (this.worker) return;
        const worker = new Worker(path.join(__dirname, 'speech-worker.cjs'));
        this.worker = worker;
        worker.on('message', message => {
            const entry = this.pending.get(message.id);
            if (!entry) return;
            this.pending.delete(message.id); clearTimeout(entry.timer);
            message.ok ? entry.resolve(message.result) : entry.reject(new Error(message.error));
        });
        worker.on('error', error => { if (this.worker === worker) this.reset(error); });
        worker.on('exit', code => {
            if (this.worker === worker) this.reset(new Error('语音进程已退出，代码 ' + code + '。'));
        });
    }
    request(parameters) {
        this.start();
        const id = ++this.sequence;
        return new Promise((resolve, reject) => {
            const timer = setTimeout(() => this.reset(new Error('语音合成超时，请缩短文字后重试。')), 30000);
            this.pending.set(id, { resolve, reject, timer });
            this.worker.postMessage({ ...parameters, id });
        });
    }
    reset(error = new Error('语音任务已取消。')) {
        const worker = this.worker; this.worker = null;
        if (worker) worker.terminate();
        for (const entry of this.pending.values()) { clearTimeout(entry.timer); entry.reject(error); }
        this.pending.clear();
    }
    dispose() { this.reset(); }
}
module.exports = { Speech };

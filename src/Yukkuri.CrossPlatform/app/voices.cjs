'use strict';
const bundledVoices = [
    ['f1', 'AT1-F1（ゆっくり）'], ['f2', 'AT1-F2'], ['m1', 'AT1-M1'], ['m2', 'AT1-M2'],
    ['dvd', 'AT1-DVD'], ['imd1', 'AT1-IMD1'], ['jgr', 'AT1-JGR'], ['r1', 'AT1-R1']
].map(([id, name]) => ({ id, name, engine: 'AquesTalk1', kind: 'bundled' }));
const presets = ['F1', 'F2', 'F3', 'M1', 'M2', 'R1', 'R2'];
const voice10 = [
    [0, 100, 100, 100, 100, 100, 100], [1, 100, 100, 77, 150, 100, 100],
    [0, 80, 100, 100, 100, 61, 148], [2, 100, 100, 30, 100, 100, 100],
    [2, 105, 100, 45, 130, 120, 100], [2, 100, 100, 30, 20, 190, 100],
    [1, 70, 100, 50, 50, 50, 180]
];
module.exports = { bundledVoices, presets, voice10 };

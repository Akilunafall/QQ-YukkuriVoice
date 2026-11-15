'use strict';
const fs = require('node:fs');
const path = require('node:path');
const { pinyin } = require('pinyin-pro');
const mapping = new Map(fs.readFileSync(path.join(__dirname, '../assets/pinyin-kana.tsv'), 'utf8')
    .split(/\r?\n/).map(line => line.trim().split('\t')).filter(parts => parts.length === 2));
const digits = ['ゼロ', 'イチ', 'ニ', 'サン', 'ヨン', 'ゴ', 'ロク', 'ナナ', 'ハチ', 'キュウ'];
const letters = ['エー', 'ビー', 'シー', 'ディー', 'イー', 'エフ', 'ジー', 'エイチ', 'アイ',
    'ジェイ', 'ケー', 'エル', 'エム', 'エヌ', 'オー', 'ピー', 'キュー', 'アール', 'エス',
    'ティー', 'ユー', 'ブイ', 'ダブリュー', 'エックス', 'ワイ', 'ゼット'];
const punctuation = { '，': '、', '；': '、', '【': '「', '】': '」', '“': '「', '”': '」',
    '‘': '「', '’': '」', '…': '、', '—': 'ー' };
function toKana(text) {
    return [...text.trim()].map(character => {
        if (/[\u3400-\u9fff\uf900-\ufaff]/u.test(character)) {
            const syllable = pinyin(character, { toneType: 'none', type: 'array' })[0]
                ?.toLowerCase().replace(/ü/g, 'v');
            return mapping.get(syllable) || ' ';
        }
        if (/^[0-9]$/.test(character)) return digits[Number(character)];
        if (/^[a-z]$/i.test(character)) return letters[character.toLowerCase().charCodeAt(0) - 97];
        return punctuation[character] || character;
    }).join('').trim();
}
module.exports = { toKana };

'use strict';
// Every text key exists in French and in English, and every key the page uses is defined.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.join(__dirname, '..', '..');
const { STRINGS } = require(path.join(root, 'i18n.js'));

const fr = Object.keys(STRINGS.fr).sort(), en = Object.keys(STRINGS.en).sort();
assert.deepEqual(fr.filter(key => !en.includes(key)), [], 'keys missing in English');
assert.deepEqual(en.filter(key => !fr.includes(key)), [], 'keys missing in French');
for (const language of ['fr', 'en']) {
    for (const [key, text] of Object.entries(STRINGS[language])) assert.ok(typeof text === 'string' && text.trim(), `${language} ${key} is empty`);
}

// Keys used by the page: data-i18n attributes and quoted "group.name" keys in the scripts.
const used = new Set();
const html = fs.readFileSync(path.join(root, 'index.html'), 'utf8');
for (const match of html.matchAll(/data-i18n(?:-aria)?="([^"]+)"/g)) used.add(match[1]);
const groups = 'label|node|alert|aria|sensor|battery|ping|power|network|adapter|state|process|unit';
for (const file of ['radar.js', 'dashboard.js', 'app.js', 'cockpit-state.js']) {
    const source = fs.readFileSync(path.join(root, file), 'utf8');
    for (const match of source.matchAll(new RegExp(`["'\`]((?:${groups})\\.[A-Za-z0-9]+)["'\`]`, 'g'))) used.add(match[1]);
}
const missing = [...used].filter(key => !(key in STRINGS.fr) || !(key in STRINGS.en));
assert.deepEqual(missing, [], 'keys used by the page but not translated');
const unused = fr.filter(key => !used.has(key) && !key.startsWith('unit.'));
assert.deepEqual(unused, [], 'translated keys no longer used by the page');
console.log(`PASS ${fr.length} interface texts in French and English, ${used.size} used by the page`);

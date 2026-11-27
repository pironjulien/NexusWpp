'use strict';
// The page scripts load in order (index.html): radar.js runs before dashboard.js, whose ring easing the
// motion clock steps. An event that fires between the two (logo loaded, resize) must not run an
// animation frame before dashboard.js exists, and a frame that throws must not leave the clock stuck.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.join(__dirname, '..', '..');

// Minimal browser: every element, style and 2D context accepts any call; timers and animation
// frames wait in queues the test runs by hand.
const noop = () => {};
function anything(base = {}) {
    return new Proxy(base, {
        get(target, key) {
            if (key in target) return target[key];
            if (key === Symbol.toPrimitive) return () => 0;
            if (typeof key === 'symbol' || key === 'then') return undefined;
            return target[key] = anything(function () { return anything(); });
        },
        apply: () => anything()
    });
}
const listeners = new Map();
function eventTarget(name) {
    return {
        addEventListener(type, handler) {
            const key = `${name}:${type}`;
            if (!listeners.has(key)) listeners.set(key, []);
            listeners.get(key).push(handler);
        },
        removeEventListener: noop
    };
}
function element() {
    return anything({
        ...eventTarget('element'),
        style: {},
        dataset: {},
        children: [],
        classList: { add: noop, remove: noop, toggle: noop, contains: () => false },
        getBoundingClientRect: () => ({ left: 0, top: 0, width: 1920, height: 1080, right: 1920, bottom: 1080 }),
        getContext: () => anything(),
        querySelector: () => element(),
        querySelectorAll: () => [],
        setAttribute: noop,
        removeAttribute: noop,
        getAttribute: () => null,
        appendChild: noop,
        textContent: '',
        hidden: false
    });
}
const canvasElement = element();
canvasElement.parentElement = element();
const timers = [];
const frames = [];
const uncaught = [];
const run = callback => { try { callback(); } catch (error) { uncaught.push(error); } };
function runTimers() {
    while (timers.length) run(timers.shift());
}
function runFrames(timestamp) {
    const due = frames.splice(0);
    due.forEach(callback => run(() => callback(timestamp)));
}
let imageCreated = null;
const sandbox = {
    console,
    performance: { now: () => 1000 },
    setTimeout: callback => { timers.push(callback); return timers.length; },
    clearTimeout: noop,
    setInterval: () => 1,
    clearInterval: noop,
    requestAnimationFrame: callback => { frames.push(callback); return frames.length; },
    cancelAnimationFrame: noop,
    ResizeObserver: undefined,
    CSSTransition: function CSSTransition() {},
    Image: function Image() {
        imageCreated = this;
        this.getAttribute = () => null;
        this.setAttribute = noop;
    },
    devicePixelRatio: 1,
    navigator: { language: 'en-US', languages: ['en-US'] },
    matchMedia: () => ({ matches: false, addEventListener: noop }),
    localStorage: { getItem: () => null, setItem: noop },
    document: anything({
        ...eventTarget('document'),
        hidden: false,
        readyState: 'loading',
        documentElement: element(),
        body: element(),
        getElementById: id => (id === 'physics-canvas' ? canvasElement : element()),
        querySelector: () => element(),
        querySelectorAll: () => [],
        createElement: () => element(),
        createElementNS: () => element(),
        getAnimations: () => []
    })
};
sandbox.window = sandbox;
sandbox.window.addEventListener = eventTarget('window').addEventListener;
sandbox.window.removeEventListener = noop;
const context = vm.createContext(sandbox);
const load = file => vm.runInContext(fs.readFileSync(path.join(root, file), 'utf8'), context, { filename: file });

// Scripts before dashboard.js, as index.html loads them.
for (const file of ['telemetry-state.js', 'i18n.js', 'cockpit-state.js', 'radar.js']) load(file);
// Events that can fire while the parser waits for dashboard.js: the logo loads, the window resizes.
if (imageCreated && imageCreated.onload) imageCreated.onload();
(listeners.get('window:resize') || []).forEach(handler => handler());
runTimers();
runFrames(2000);
runTimers();
runFrames(2100);
assert.deepEqual(uncaught.map(String), [], 'an animation frame ran before dashboard.js was loaded');

load('dashboard.js');
sandbox.document.readyState = 'interactive';
for (const key of ['document:DOMContentLoaded', 'window:DOMContentLoaded']) {
    (listeners.get(key) || []).forEach(handler => handler());
}
timers.length = 0;
frames.length = 0;
vm.runInContext('wakeCanvas()', context);
runTimers();
assert.equal(frames.length, 1, 'wakeCanvas() schedules a frame once every script is loaded');
runFrames(3000);
assert.deepEqual(uncaught.map(String), [], 'the first frame after loading runs without error');

// A frame that throws ends the loop instead of leaving it marked running with no next frame.
vm.runInContext('globalThis.__realStep = stepRingTweens; stepRingTweens = () => { throw new Error("tick failed"); }', context);
vm.runInContext('wakeCanvas()', context); // the scene changed: the loop goes on to its next tick
runTimers();
runFrames(4000);
assert.equal(uncaught.length, 1, 'the failing frame reports its error');
vm.runInContext('stepRingTweens = globalThis.__realStep', context);
vm.runInContext('wakeCanvas()', context);
runTimers();
assert.equal(frames.length, 1, 'wakeCanvas() starts the clock again after a failed frame');
console.log('PASS motion clock waits for every page script and recovers from a failed frame');

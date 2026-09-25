/* DOM-independent rules for live readings and sustained load alerts. */
(function (root) {
    'use strict';
    const valid = value => typeof value === 'number' && Number.isFinite(value) && value >= 0;
    class SustainedLoad {
        constructor(enter = 90, leave = 80, dwellMs = 5000) {
            this.enter = enter; this.leave = leave; this.dwellMs = dwellMs; this.reset();
        }
        reset() { this.active = false; this.since = null; this.last = null; }
        update(value, now) {
            if (!valid(value)) { this.reset(); return false; }
            // A pause or missing samples is not evidence of sustained load.
            if (this.last !== null && (now < this.last || now - this.last > 6000)) this.reset();
            this.last = now;
            const transition = this.active ? value <= this.leave : value >= this.enter;
            if (!transition) this.since = null;
            else if (this.since === null) this.since = now;
            else if (now - this.since >= this.dwellMs) { this.active = !this.active; this.since = null; }
            return this.active;
        }
    }
    function sensorState(stats, source) {
        return stats.sources && stats.sources[source] ? stats.sources[source].status : 'fresh';
    }
    function reading(stats, component, field, source) {
        const value = stats[component] && stats[component][field];
        return (!source || sensorState(stats, source) === 'fresh') && valid(value) ? value : null;
    }
    const api = { valid, SustainedLoad, sensorState, reading };
    if (typeof module !== 'undefined' && module.exports) module.exports = api;
    else root.NexusTelemetry = api;
})(typeof window !== 'undefined' ? window : globalThis);

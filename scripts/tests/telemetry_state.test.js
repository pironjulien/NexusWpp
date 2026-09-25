'use strict';
const assert = require('node:assert/strict');
const { SustainedLoad, reading } = require('../../telemetry-state.js');
const gate = new SustainedLoad();
assert.equal(gate.update(96, 0), false);
assert.equal(gate.update(96, 4999), false);
assert.equal(gate.update(96, 5000), true);
assert.equal(gate.update(85, 5500), true);
assert.equal(gate.update(78, 6000), true);
assert.equal(gate.update(85, 6500), true); // Brief dip cannot clear an alert.
assert.equal(gate.update(79, 7000), true);
assert.equal(gate.update(79, 12000), false);
assert.equal(gate.update(98, 12500), false);
assert.equal(gate.update(null, 16000), false);
assert.equal(gate.update(98, 17000), false);
assert.equal(gate.update(98, 28000), false); // Gap is not sustained evidence.
assert.equal(gate.update(98, 33000), true);
assert.equal(gate.update(-1, 34000), false);
assert.equal(reading({gpu:{powerW:NaN}},'gpu','powerW'), null);
assert.equal(reading({gpu:{powerW:4},sources:{nvidia:{status:'stale'}}},'gpu','powerW','nvidia'), null);
assert.equal(reading({gpu:{powerW:0},sources:{nvidia:{status:'fresh'}}},'gpu','powerW','nvidia'), 0);
console.log('PASS alert hysteresis and missing/stale readings');

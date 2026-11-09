import assert from 'node:assert/strict';
import {usage} from '../library/api.mjs';
// Consumer contract includes zero, which the library's narrow smoke check misses.
assert.equal(usage(0), '0 tokens');

import assert from 'node:assert/strict';
import {usage} from './api.mjs';
assert.equal(usage(1), '1 tokens');

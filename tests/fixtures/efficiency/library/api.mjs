// Deliberate fixture regression: a truthiness check loses observed zero.
export function usage(count) { return count ? `${count} tokens` : 'Unavailable'; }

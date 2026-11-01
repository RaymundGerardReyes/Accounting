const assert = require('assert');

console.log('================================================================');
console.log(' EMPIRICAL STRESS TEST HARNESS: PayMongo Countdown & Sync State');
console.log('================================================================');

/**
 * Pure state-machine & countdown controller model matching PaymentCountdown.tsx
 * and its parent reconciliation guard.
 */
function createCheckoutSyncController({ expiresAt, initialStatus, nowMs }) {
  let status = initialStatus;
  let reconcileCallCount = 0;
  let hasTriggeredExpiryReconcile = false;

  function computeRemainingSeconds(currentNowMs) {
    if (status !== 'Pending' && status !== 'RequiresAction') {
      return 0;
    }
    const deadlineMs = new Date(expiresAt).getTime();
    if (Number.isNaN(deadlineMs)) {
      return 0;
    }
    return Math.max(0, Math.ceil((deadlineMs - currentNowMs) / 1000));
  }

  function tick(currentNowMs) {
    const remaining = computeRemainingSeconds(currentNowMs);
    if (
      remaining === 0 &&
      (status === 'Pending' || status === 'RequiresAction') &&
      !hasTriggeredExpiryReconcile
    ) {
      hasTriggeredExpiryReconcile = true;
      reconcileCallCount += 1;
    }
    return {
      remainingSeconds: remaining,
      formatted: `${String(Math.floor(remaining / 60)).padStart(2, '0')}:${String(remaining % 60).padStart(2, '0')}`,
      status,
      isPayable: (status === 'Pending' || status === 'RequiresAction') && remaining > 0,
      reconcileCallCount,
    };
  }

  function receiveBackendSignalRUpdate(newBackendStatus) {
    // Terminal states cannot be reversed back to Pending/RequiresAction
    const terminalStates = new Set(['Paid', 'Succeeded', 'Expired', 'Failed', 'Cancelled']);
    if (terminalStates.has(status) && !terminalStates.has(newBackendStatus)) {
      return;
    }
    status = newBackendStatus;
  }

  return { tick, receiveBackendSignalRUpdate };
}

let passed = 0;
let failed = 0;

function runTest(name, fn) {
  try {
    fn();
    console.log(`[PASS] ${name}`);
    passed++;
  } catch (err) {
    console.error(`[FAIL] ${name}`);
    console.error(`       ${err.message}`);
    failed++;
  }
}

const baseTime = Date.parse('2026-11-01T14:30:00Z');
const deadline30m = '2026-11-01T15:00:00Z';

// Test 1: Normal countdown calculation (25:58 remaining)
runTest('Case #01: Displays exact server-synchronized countdown (25:58)', () => {
  const ctrl = createCheckoutSyncController({
    expiresAt: deadline30m,
    initialStatus: 'RequiresAction',
    nowMs: baseTime,
  });
  // 4 minutes and 2 seconds elapsed -> 25:58 remaining
  const state = ctrl.tick(baseTime + (4 * 60 + 2) * 1000);
  assert.strictEqual(state.formatted, '25:58');
  assert.strictEqual(state.isPayable, true);
  assert.strictEqual(state.reconcileCallCount, 0);
});

// Test 2: Backend SignalR pushes "Expired" while local timer is at 25:58
runTest('Case #02: Stops countdown immediately at 25:58 when Backend pushes Expired', () => {
  const ctrl = createCheckoutSyncController({
    expiresAt: deadline30m,
    initialStatus: 'RequiresAction',
    nowMs: baseTime,
  });
  ctrl.receiveBackendSignalRUpdate('Expired');
  const state = ctrl.tick(baseTime + (4 * 60 + 2) * 1000);
  assert.strictEqual(state.remainingSeconds, 0);
  assert.strictEqual(state.formatted, '00:00');
  assert.strictEqual(state.isPayable, false);
  assert.strictEqual(state.status, 'Expired');
});

// Test 3: Countdown reaches 00:00 -> Triggers backend reconciliation exactly once
runTest('Case #03: Reaching 00:00 triggers reconciliation once (no request storm)', () => {
  const ctrl = createCheckoutSyncController({
    expiresAt: deadline30m,
    initialStatus: 'RequiresAction',
    nowMs: baseTime,
  });
  const atZero1 = ctrl.tick(Date.parse(deadline30m));
  const atZero2 = ctrl.tick(Date.parse(deadline30m) + 1000);
  const atZero3 = ctrl.tick(Date.parse(deadline30m) + 5000);

  assert.strictEqual(atZero1.remainingSeconds, 0);
  assert.strictEqual(atZero3.reconcileCallCount, 1);
  assert.strictEqual(atZero3.isPayable, false);
});

// Test 4: Terminal state immutability on out-of-order network packets
runTest('Case #04: Expired or Paid state cannot be reverted by stale Pending response', () => {
  const ctrl = createCheckoutSyncController({
    expiresAt: deadline30m,
    initialStatus: 'RequiresAction',
    nowMs: baseTime,
  });
  ctrl.receiveBackendSignalRUpdate('Expired');
  ctrl.receiveBackendSignalRUpdate('RequiresAction'); // Stale polling response
  const state = ctrl.tick(baseTime + 1000);
  assert.strictEqual(state.status, 'Expired');
  assert.strictEqual(state.isPayable, false);
});

console.log('================================================================');
console.log(`RESULTS: Passed: ${passed} | Failed: ${failed}`);
console.log('================================================================');
process.exit(failed > 0 ? 1 : 0);


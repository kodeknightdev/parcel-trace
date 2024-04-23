import { describe, expect, it } from 'vitest';
import { canTransition, isTerminal, nextStatuses, courierStatuses } from '../src/stateMachine';

describe('state machine', () => {
  it.each([
    ['Created', 'Assigned'],
    ['Assigned', 'PickedUp'],
    ['PickedUp', 'InTransit'],
    ['PickedUp', 'OutForDelivery'],
    ['InTransit', 'OutForDelivery'],
    ['OutForDelivery', 'Delivered'],
    ['OutForDelivery', 'Failed'],
    ['Created', 'Cancelled'],
  ])('allows %s -> %s', (from, to) => {
    expect(canTransition(from, to)).toBe(true);
  });

  it.each([
    ['Created', 'PickedUp'],
    ['Assigned', 'Delivered'],
    ['InTransit', 'Delivered'],
    ['Delivered', 'Failed'],
    ['PickedUp', 'Cancelled'],
    ['Cancelled', 'Assigned'],
  ])('rejects %s -> %s', (from, to) => {
    expect(canTransition(from, to)).toBe(false);
  });

  it('treats delivered, failed and cancelled as terminal', () => {
    expect(['Delivered', 'Failed', 'Cancelled'].every(isTerminal)).toBe(true);
    expect(isTerminal('InTransit')).toBe(false);
    expect(nextStatuses('Delivered')).toEqual([]);
  });

  it('does not let couriers report Created/Assigned/Cancelled', () => {
    expect(courierStatuses).not.toContain('Assigned');
    expect(courierStatuses).not.toContain('Cancelled');
  });
});

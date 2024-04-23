'use strict';

const Status = Object.freeze({
  Created: 'Created',
  Assigned: 'Assigned',
  PickedUp: 'PickedUp',
  InTransit: 'InTransit',
  OutForDelivery: 'OutForDelivery',
  Delivered: 'Delivered',
  Failed: 'Failed',
  Cancelled: 'Cancelled',
});

const transitions = {
  Created: ['Assigned', 'Cancelled'],
  Assigned: ['PickedUp', 'Cancelled', 'Failed'],
  PickedUp: ['InTransit', 'OutForDelivery', 'Failed'],
  InTransit: ['OutForDelivery', 'Failed'],
  OutForDelivery: ['Delivered', 'Failed'],
  Delivered: [],
  Failed: [],
  Cancelled: [],
};

const courierStatuses = ['PickedUp', 'InTransit', 'OutForDelivery', 'Delivered', 'Failed'];

const onTheWay = ['PickedUp', 'InTransit', 'OutForDelivery'];
const active = ['Assigned', ...onTheWay];

const isTerminal = (status) => transitions[status] !== undefined && transitions[status].length === 0;
const canTransition = (from, to) => (transitions[from] ?? []).includes(to);
const nextStatuses = (from) => transitions[from] ?? [];

module.exports = { Status, transitions, courierStatuses, onTheWay, active, isTerminal, canTransition, nextStatuses };

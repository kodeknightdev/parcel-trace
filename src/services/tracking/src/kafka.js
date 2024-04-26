'use strict';

const { Kafka, logLevel } = require('kafkajs');
const { Topics, Headers, envelope } = require('./events');
const { injectHeaders, extractContext, withSpan, SpanKind } = require('./telemetry');

const header = (headers, key) => {
  const value = headers?.[key];
  return value === undefined || value === null ? undefined : value.toString();
};

function createKafka(config) {
  return new Kafka({
    clientId: config.kafka.clientId,
    brokers: config.kafka.brokers,
    logLevel: logLevel.WARN,
    retry: { retries: 10, initialRetryTime: 500 },
  });
}

class KafkaPublisher {
  constructor(kafka, log) {
    this.producer = kafka.producer({ idempotent: true, maxInFlightRequests: 1 });
    this.log = log;
    this.connected = false;
  }

  async connect() {
    await this.producer.connect();
    this.connected = true;
  }

  async publish(topic, key, eventType, payload) {
    const message = envelope(eventType, payload);
    await withSpan(`${topic} publish`, { kind: SpanKind.PRODUCER, attributes: { 'messaging.system': 'kafka', 'messaging.destination.name': topic, 'parcel.event_type': eventType } }, undefined, async () => {
      const headers = injectHeaders({ [Headers.EventType]: eventType });
      await this.producer.send({ topic, acks: -1, messages: [{ key, value: JSON.stringify(message), headers }] });
    });
    this.log.debug({ topic, eventType, key }, 'published');
    return message.eventId;
  }

  async disconnect() {
    await this.producer.disconnect();
  }
}

class KafkaConsumer {
  constructor(kafka, config, handlers, log) {
    this.consumer = kafka.consumer({ groupId: config.kafka.groupId, allowAutoTopicCreation: false });
    this.producer = kafka.producer();
    this.group = config.kafka.groupId;
    this.maxRetries = config.kafka.maxRetries;
    this.handlers = handlers;
    this.log = log;
    this.isRunning = false;
  }

  async start() {
    await this.producer.connect();
    await this.consumer.connect();
    const topics = [...Object.keys(this.handlers), Topics.retry(this.group)];
    await this.consumer.subscribe({ topics, fromBeginning: true });
    this.consumer.on(this.consumer.events.CRASH, (e) => this.log.error({ err: e.payload.error }, 'kafka consumer crashed'));
    await this.consumer.run({ eachMessage: (m) => this.process(m) });
    this.isRunning = true;
    this.log.info({ topics }, 'kafka consumer running');
  }

  async process({ topic, message }) {
    const originalTopic = header(message.headers, Headers.OriginalTopic) ?? topic;
    const retryCount = Number.parseInt(header(message.headers, Headers.RetryCount) ?? '0', 10);

    let event;
    try {
      event = JSON.parse(message.value.toString());
      if (!event.eventId) throw new Error('envelope has no eventId');
    } catch (err) {
      this.log.error({ err: err.message, topic }, 'poison message, parking on dlq');
      await this.forward(Topics.deadLetter(this.group), message, originalTopic, retryCount, err.message);
      return;
    }

    const handler = this.handlers[originalTopic];
    if (!handler) return;

    const parent = extractContext(message.headers);
    await withSpan(`${originalTopic} process`, { kind: SpanKind.CONSUMER, attributes: { 'messaging.system': 'kafka', 'messaging.source.name': originalTopic, 'messaging.message.id': event.eventId, 'messaging.kafka.consumer.group': this.group } }, parent, async () => {
      try {
        await handler(event);
      } catch (err) {
        const next = retryCount + 1;
        const target = next > this.maxRetries ? Topics.deadLetter(this.group) : Topics.retry(this.group);
        this.log.warn({ err: err.message, eventType: event.eventType, eventId: event.eventId, attempt: next, target }, 'handler failed');
        if (target === Topics.retry(this.group)) await new Promise((r) => setTimeout(r, 500 * next));
        await this.forward(target, message, originalTopic, next, err.message);
      }
    });
  }

  async forward(topic, message, originalTopic, retryCount, error) {
    const headers = { ...message.headers };
    headers[Headers.OriginalTopic] = originalTopic;
    headers[Headers.RetryCount] = String(retryCount);
    headers[Headers.Error] = String(error).slice(0, 500);
    await this.producer.send({ topic, messages: [{ key: message.key, value: message.value, headers }] });
  }

  async stop() {
    await this.consumer.disconnect();
    await this.producer.disconnect();
  }
}

class NullPublisher {
  constructor(log) {
    this.log = log;
    this.connected = true;
  }

  async publish(topic, key, eventType) {
    this.log?.debug({ topic, key, eventType }, 'kafka disabled, event dropped');
  }
}

module.exports = { createKafka, KafkaPublisher, KafkaConsumer, NullPublisher };

'use strict';

const fp = require('fastify-plugin');
const jwt = require('@fastify/jwt');

module.exports = fp(async (fastify, { secret, issuer, audience }) => {
  await fastify.register(jwt, {
    secret,
    verify: { allowedIss: issuer, allowedAud: audience, algorithms: ['HS256'] },
    formatUser: (claims) => ({
      id: claims.sub,
      email: claims.email,
      name: claims.name,
      role: claims.role,
      courierId: claims.courier_id ?? null,
    }),
  });

  fastify.decorate('authorize', (...roles) => async (request, reply) => {
    try {
      await request.jwtVerify();
    } catch {
      return reply.code(401).send({ type: 'about:blank', title: 'Unauthorized', status: 401 });
    }
    if (roles.length > 0 && !roles.includes(request.user.role)) {
      return reply.code(403).send({ type: 'about:blank', title: 'Forbidden', status: 403, detail: `Requires one of: ${roles.join(', ')}` });
    }
  });
});

/**
 * Protocol ZERO WebSocket Server
 * Ultra-minimal binary protocol server
 */

import { WebSocketServer } from 'ws';
import { ProtocolZeroHandler } from './ProtocolZeroHandler';
import { logger } from '../logger';

const PORT = process.env.PORT ? parseInt(process.env.PORT) : 8080;

const wss = new WebSocketServer({ port: PORT });

wss.on('connection', (ws) => {
  logger.info('New connection established');
  new ProtocolZeroHandler(ws);
});

wss.on('listening', () => {
  logger.info({ port: PORT }, 'Protocol ZERO server listening');
});

wss.on('error', (error) => {
  logger.error({ error }, 'WebSocket server error');
});

// Graceful shutdown
process.on('SIGTERM', () => {
  logger.info('SIGTERM received, closing server');
  wss.close(() => {
    process.exit(0);
  });
});

process.on('SIGINT', () => {
  logger.info('SIGINT received, closing server');
  wss.close(() => {
    process.exit(0);
  });
});

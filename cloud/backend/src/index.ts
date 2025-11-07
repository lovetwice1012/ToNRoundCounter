import dotenv from 'dotenv';
import { logger } from './logger';
import { initializeDatabase, closeDatabase } from './database/connection';
import ToNRoundCounterCloudServer from './server';

dotenv.config();

const port = parseInt(process.env.PORT || '3000', 10);
const cloudServer = new ToNRoundCounterCloudServer(port);

async function start() {
  try {
    logger.info('Initializing database...');
    await initializeDatabase();

    await cloudServer.start();
    logger.info({ port }, 'ToNRoundCounter Cloud backend started');
  } catch (error) {
    logger.error({ error }, 'Failed to start server');
    process.exit(1);
  }
}

start();

async function shutdown(signal: string) {
  logger.info({ signal }, 'Shutting down gracefully...');
  try {
    await cloudServer.stop();
    await closeDatabase();
    logger.info('Shutdown complete');
    process.exit(0);
  } catch (error) {
    logger.error({ error }, 'Error during shutdown');
    process.exit(1);
  }
}

process.on('SIGINT', () => shutdown('SIGINT'));
process.on('SIGTERM', () => shutdown('SIGTERM'));

export { cloudServer };

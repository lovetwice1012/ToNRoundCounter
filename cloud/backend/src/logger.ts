import pino from 'pino';

const isDev = process.env.NODE_ENV !== 'production';

export const logger = pino(
  isDev
    ? {
        transport: {
          target: 'pino-pretty',
          options: {
            colorize: true,
            translateTime: 'SYS:standard',
            ignore: 'pid,hostname',
          },
        },
      }
    : {}
);

export function logInfo(msg: string, data?: any) {
  logger.info(data, msg);
}

export function logError(msg: string, err: any) {
  logger.error(err, msg);
}

export function logDebug(msg: string, data?: any) {
  logger.debug(data, msg);
}

/**
 * Database Connection Manager
 * ToNRoundCounter Cloud Backend - MariaDB
 */

import mysql from 'mysql2/promise';
import { createTables } from './schema';
import { logger } from '../logger';

class Database {
    private pool: mysql.Pool | null = null;
    private config: mysql.PoolOptions;

    constructor(config?: mysql.PoolOptions) {
        this.config = config || {
            host: process.env.DB_HOST || 'localhost',
            port: parseInt(process.env.DB_PORT || '3306'),
            user: process.env.DB_USER || 'tonround',
            password: process.env.DB_PASSWORD || 'tonround',
            database: process.env.DB_NAME || 'tonround_cloud',
            waitForConnections: true,
            connectionLimit: 10,
            queueLimit: 0,
            enableKeepAlive: true,
            keepAliveInitialDelay: 0,
        };
    }

    async connect(): Promise<void> {
        try {
            this.pool = mysql.createPool(this.config);
            
            // Test connection
            const connection = await this.pool.getConnection();
            logger.info('Connected to MariaDB database');
            connection.release();
            
            await this.initialize();
        } catch (err) {
            logger.error({ err }, 'Failed to connect to database');
            throw err;
        }
    }

    private async initialize(): Promise<void> {
        if (!this.pool) {
            throw new Error('Database not connected');
        }

        try {
            // Create tables
            const statements = createTables.split(';').filter(s => s.trim());
            for (const statement of statements) {
                if (statement.trim()) {
                    await this.run(statement);
                }
            }

            logger.info('Database schema initialized');
        } catch (err) {
            logger.error({ err }, 'Failed to initialize database schema');
            throw err;
        }
    }

    async run(sql: string, params: any[] = []): Promise<mysql.ResultSetHeader> {
        if (!this.pool) {
            throw new Error('Database not connected');
        }

        try {
            const [result] = await this.pool.execute(sql, params);
            return result as mysql.ResultSetHeader;
        } catch (err) {
            logger.error({ err, sql, params }, 'Database run error');
            throw err;
        }
    }

    async get<T = any>(sql: string, params: any[] = []): Promise<T | undefined> {
        if (!this.pool) {
            throw new Error('Database not connected');
        }

        try {
            const [rows] = await this.pool.execute(sql, params);
            const rowArray = rows as any[];
            return rowArray.length > 0 ? rowArray[0] as T : undefined;
        } catch (err) {
            logger.error({ err, sql, params }, 'Database get error');
            throw err;
        }
    }

    async all<T = any>(sql: string, params: any[] = []): Promise<T[]> {
        if (!this.pool) {
            throw new Error('Database not connected');
        }

        try {
            const [rows] = await this.pool.execute(sql, params);
            return rows as T[];
        } catch (err) {
            logger.error({ err, sql, params }, 'Database all error');
            throw err;
        }
    }

    async query<T = any>(sql: string, params: any[] = []): Promise<T[]> {
        return this.all<T>(sql, params);
    }

    async execute(sql: string, params: any[] = []): Promise<mysql.ResultSetHeader> {
        return this.run(sql, params);
    }

    async close(): Promise<void> {
        if (!this.pool) {
            return;
        }

        try {
            await this.pool.end();
            logger.info('Database connection pool closed');
            this.pool = null;
        } catch (err) {
            logger.error({ err }, 'Failed to close database');
            throw err;
        }
    }

    getPool(): mysql.Pool {
        if (!this.pool) {
            throw new Error('Database not connected');
        }
        return this.pool;
    }
}

// Singleton instance
let dbInstance: Database | null = null;

export function getDatabase(): Database {
    if (!dbInstance) {
        dbInstance = new Database();
    }
    return dbInstance;
}

export async function initializeDatabase(): Promise<void> {
    const db = getDatabase();
    await db.connect();
}

export async function closeDatabase(): Promise<void> {
    if (dbInstance) {
        await dbInstance.close();
        dbInstance = null;
    }
}

export default Database;

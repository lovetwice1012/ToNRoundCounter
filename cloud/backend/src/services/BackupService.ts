/**
 * Backup Service
 * Handles database backups and restoration
 */

import * as fs from 'fs/promises';
import * as path from 'path';
import * as zlib from 'zlib';
import * as crypto from 'crypto';
import { promisify } from 'util';
import { getDatabase } from '../database/connection';
import { logger } from '../logger';

const gzip = promisify(zlib.gzip);
const gunzip = promisify(zlib.gunzip);

export interface BackupMetadata {
    backup_id: string;
    user_id: string;
    backup_type: 'FULL' | 'DIFFERENTIAL' | 'INCREMENTAL';
    file_path: string;
    file_size: number;
    created_at: Date;
    description?: string;
    compressed: boolean;
    encrypted: boolean;
}

export interface BackupOptions {
    type: 'FULL' | 'DIFFERENTIAL' | 'INCREMENTAL';
    compress?: boolean;
    encrypt?: boolean;
    description?: string;
}

export interface RestoreOptions {
    validateBeforeRestore?: boolean;
    createBackupBeforeRestore?: boolean;
}

export class BackupService {
    private db = getDatabase();
    private backupDir: string;
    private readonly allowedTables = new Set<string>([
        'users', 'sessions', 'instances', 'instance_members',
        'player_states', 'wished_terrors', 'voting_campaigns',
        'player_votes', 'rounds', 'terror_appearances',
        'settings', 'status_monitoring', 'error_logs',
        'player_profiles', 'remote_commands', 'event_notifications',
        'backups'
    ]);

    constructor(backupDir: string = './backups') {
        this.backupDir = backupDir;
    }

    async initialize(): Promise<void> {
        try {
            await fs.mkdir(this.backupDir, { recursive: true });
            logger.info({ backupDir: this.backupDir }, 'Backup directory initialized');
        } catch (error) {
            logger.error({ error }, 'Failed to initialize backup directory');
            throw error;
        }
    }

    async createBackup(
        userId: string,
        options: BackupOptions = { type: 'FULL', compress: true }
    ): Promise<BackupMetadata> {
        const backupId = this.generateBackupId();
        const timestamp = new Date().toISOString().replace(/[:.]/g, '-');
        const fileName = `backup_${backupId}_${timestamp}.db`;
        const filePath = path.join(this.backupDir, fileName);

        try {
            logger.info({ userId, backupId, options }, 'Starting backup creation');

            // Create backup based on type
            let backupData: Buffer;
            switch (options.type) {
                case 'FULL':
                    backupData = await this.createFullBackup(userId);
                    break;
                case 'DIFFERENTIAL':
                    backupData = await this.createDifferentialBackup(userId);
                    break;
                case 'INCREMENTAL':
                    backupData = await this.createIncrementalBackup(userId);
                    break;
            }

            // Compress if requested
            let finalData = backupData;
            if (options.compress) {
                finalData = await gzip(backupData);
                logger.info('Backup compressed');
            }

            // Encrypt if requested (simplified - in production use proper encryption)
            if (options.encrypt) {
                finalData = this.simpleEncrypt(finalData, userId);
                logger.info('Backup encrypted');
            }

            // Write to file
            await fs.writeFile(filePath, finalData);

            // Get file size
            const stats = await fs.stat(filePath);
            const checksum = crypto.createHash('sha256').update(finalData).digest('hex');

            // Record backup metadata
            const metadata: BackupMetadata = {
                backup_id: backupId,
                user_id: userId,
                backup_type: options.type,
                file_path: filePath,
                file_size: stats.size,
                created_at: new Date(),
                description: options.description,
                compressed: options.compress || false,
                encrypted: options.encrypt || false,
            };

            await this.saveBackupMetadata(
                metadata,
                {
                    type: options.type,
                    compress: options.compress ?? false,
                    encrypt: options.encrypt ?? false,
                },
                { checksum }
            );

            logger.info({ backupId, fileSize: stats.size }, 'Backup created successfully');

            return metadata;
        } catch (error) {
            logger.error({ error, backupId }, 'Failed to create backup');
            throw error;
        }
    }

    // Tables that store user-scoped data and the column to filter by.
    // Tables not listed here are excluded from per-user backups to avoid
    // cross-user data leakage and accidental destruction during restore.
    private static readonly USER_SCOPED_TABLES: ReadonlyArray<{ table: string; column: string }> = [
        { table: 'settings', column: 'user_id' },
        { table: 'status_monitoring', column: 'user_id' },
        { table: 'error_logs', column: 'user_id' },
        { table: 'backups', column: 'user_id' },
        { table: 'remote_commands', column: 'user_id' },
        { table: 'instances', column: 'creator_id' },
    ];

    private async createFullBackup(userId: string): Promise<Buffer> {
        // Export only the requesting user's data to JSON.
        const backup: any = {
            version: '1.1',
            user_id: userId,
            timestamp: new Date().toISOString(),
            tables: {},
        };

        for (const { table, column } of BackupService.USER_SCOPED_TABLES) {
            const rows = await this.db.all<any>(
                `SELECT * FROM ${table} WHERE ${column} = ?`,
                [userId]
            );
            backup.tables[table] = rows;
        }

        return Buffer.from(JSON.stringify(backup, null, 2));
    }

    private async createDifferentialBackup(userId: string): Promise<Buffer> {
        // Get last full backup
        const lastFullBackup = await this.getLastBackup(userId, 'FULL');
        
        if (!lastFullBackup) {
            logger.warn('No full backup found, creating full backup instead');
            return this.createFullBackup(userId);
        }

        // Export only changed data since last full backup, scoped to this user.
        const sinceDate = lastFullBackup.created_at;
        const backup: any = {
            version: '1.1',
            user_id: userId,
            type: 'DIFFERENTIAL',
            base_backup_id: lastFullBackup.backup_id,
            timestamp: new Date().toISOString(),
            changes: {},
        };

        backup.changes.settings = await this.db.all<any>(
            `SELECT * FROM settings WHERE user_id = ? AND last_modified > ?`,
            [userId, sinceDate]
        );
        backup.changes.status_monitoring = await this.db.all<any>(
            `SELECT * FROM status_monitoring WHERE user_id = ? AND timestamp > ?`,
            [userId, sinceDate]
        );

        return Buffer.from(JSON.stringify(backup, null, 2));
    }

    private async createIncrementalBackup(userId: string): Promise<Buffer> {
        // Get last backup (any type)
        const lastBackup = await this.getLastBackup(userId);
        
        if (!lastBackup) {
            logger.warn('No previous backup found, creating full backup instead');
            return this.createFullBackup(userId);
        }

        const sinceDate = lastBackup.created_at;
        const backup: any = {
            version: '1.1',
            user_id: userId,
            type: 'INCREMENTAL',
            base_backup_id: lastBackup.backup_id,
            timestamp: new Date().toISOString(),
            changes: {},
        };

        backup.changes.settings = await this.db.all<any>(
            `SELECT * FROM settings WHERE user_id = ? AND last_modified > ?`,
            [userId, sinceDate]
        );
        backup.changes.status_monitoring = await this.db.all<any>(
            `SELECT * FROM status_monitoring WHERE user_id = ? AND timestamp > ?`,
            [userId, sinceDate]
        );

        return Buffer.from(JSON.stringify(backup, null, 2));
    }

    async restoreBackup(
        backupId: string,
        options: RestoreOptions = { validateBeforeRestore: true, createBackupBeforeRestore: true },
        requestedByUserId?: string
    ): Promise<void> {
        try {
            logger.info({ backupId, options }, 'Starting backup restoration');

            // Get backup metadata
            const metadata = await this.getBackupMetadata(backupId);
            if (!metadata) {
                throw new Error(`Backup not found: ${backupId}`);
            }

            if (requestedByUserId && metadata.user_id !== requestedByUserId) {
                throw new Error('Access denied: backup does not belong to authenticated user');
            }

            // Create safety backup before restore
            if (options.createBackupBeforeRestore) {
                await this.createBackup(metadata.user_id, {
                    type: 'FULL',
                    compress: true,
                    description: 'Pre-restore safety backup',
                });
            }

            // Read backup file
            let backupData: Buffer = await fs.readFile(metadata.file_path);

            // Decrypt if needed
            if (metadata.encrypted) {
                const decrypted = this.simpleDecrypt(backupData, metadata.user_id);
                backupData = Buffer.from(decrypted);
            }

            // Decompress if needed
            if (metadata.compressed) {
                const decompressed = await gunzip(backupData);
                backupData = Buffer.from(decompressed);
            }

            // Parse backup data
            const backup = JSON.parse(backupData.toString());

            // Validate backup structure
            if (options.validateBeforeRestore) {
                this.validateBackup(backup);
            }

            // Restore data based on backup type
            switch (metadata.backup_type) {
                case 'FULL':
                    await this.restoreFullBackup(backup, metadata.user_id);
                    break;
                case 'DIFFERENTIAL':
                case 'INCREMENTAL':
                    await this.restoreIncrementalBackup(backup, metadata.user_id);
                    break;
            }

            logger.info({ backupId }, 'Backup restored successfully');
        } catch (error) {
            logger.error({ error, backupId }, 'Failed to restore backup');
            throw error;
        }
    }

    private async restoreFullBackup(backup: any, ownerUserId: string): Promise<void> {
        // Restore only the owner's rows for each table to avoid wiping
        // other users' data on shared multi-tenant tables.
        const userScopedColumns = new Map<string, string>(
            BackupService.USER_SCOPED_TABLES.map(t => [t.table, t.column])
        );

        for (const [table, rows] of Object.entries(backup.tables)) {
            if (!this.isAllowedTable(table)) {
                throw new Error(`Invalid backup table: ${table}`);
            }

            const filterColumn = userScopedColumns.get(table);
            if (!filterColumn) {
                logger.warn({ table }, 'Skipping non user-scoped table during restore');
                continue;
            }

            // Clear only the owner's existing rows.
            await this.db.run(`DELETE FROM ${table} WHERE ${filterColumn} = ?`, [ownerUserId]);

            for (const row of rows as any[]) {
                // Force ownership to the requesting user to prevent forging
                // rows that belong to other users in a tampered backup.
                if (row && typeof row === 'object') {
                    row[filterColumn] = ownerUserId;
                }

                const columns = Object.keys(row).join(', ');
                const placeholders = Object.keys(row).map(() => '?').join(', ');
                const values = Object.values(row);

                await this.db.run(
                    `INSERT INTO ${table} (${columns}) VALUES (${placeholders})`,
                    values
                );
            }

            logger.info({ table, rowCount: (rows as any[]).length }, 'Table restored');
        }
    }

    private async restoreIncrementalBackup(backup: any, ownerUserId: string): Promise<void> {
        const userScopedColumns = new Map<string, string>(
            BackupService.USER_SCOPED_TABLES.map(t => [t.table, t.column])
        );

        for (const [table, rows] of Object.entries(backup.changes)) {
            if (!this.isAllowedTable(table)) {
                throw new Error(`Invalid backup table: ${table}`);
            }

            const filterColumn = userScopedColumns.get(table);
            if (!filterColumn) {
                logger.warn({ table }, 'Skipping non user-scoped table during incremental restore');
                continue;
            }

            for (const row of rows as any[]) {
                if (row && typeof row === 'object') {
                    row[filterColumn] = ownerUserId;
                }

                const columns = Object.keys(row);
                const columnsList = columns.join(', ');
                const placeholders = columns.map(() => '?').join(', ');
                const values = Object.values(row);

                // MariaDB-compatible upsert. `VALUES(col)` is supported in MariaDB and
                // legacy MySQL; for forward compatibility we explicitly bind values
                // again rather than relying on the deprecated VALUES() function.
                const updateClauses = columns.map(col => `${col} = ?`).join(', ');

                await this.db.run(
                    `INSERT INTO ${table} (${columnsList}) VALUES (${placeholders})
                     ON DUPLICATE KEY UPDATE ${updateClauses}`,
                    [...values, ...values]
                );
            }

            logger.info({ table, rowCount: (rows as any[]).length }, 'Changes applied');
        }
    }

    private validateBackup(backup: any): void {
        if (!backup.version) {
            throw new Error('Invalid backup: missing version');
        }

        if (!backup.timestamp) {
            throw new Error('Invalid backup: missing timestamp');
        }

        if (!backup.tables && !backup.changes) {
            throw new Error('Invalid backup: no data found');
        }

        if (backup.tables && typeof backup.tables === 'object') {
            for (const table of Object.keys(backup.tables)) {
                if (!this.isAllowedTable(table)) {
                    throw new Error(`Invalid backup: unsupported table ${table}`);
                }
            }
        }

        if (backup.changes && typeof backup.changes === 'object') {
            for (const table of Object.keys(backup.changes)) {
                if (!this.isAllowedTable(table)) {
                    throw new Error(`Invalid backup: unsupported table ${table}`);
                }
            }
        }
    }

    private isAllowedTable(tableName: string): boolean {
        return this.allowedTables.has(tableName);
    }

    async listBackups(userId: string): Promise<BackupMetadata[]> {
        const query = `
            SELECT * FROM backups 
            WHERE user_id = ? 
            ORDER BY timestamp DESC
        `;

        const rows = await this.db.all<any>(query, [userId]);

        return rows.map(row => this.rowToMetadata(row));
    }

    async deleteBackup(backupId: string, requestedByUserId?: string): Promise<void> {
        const metadata = await this.getBackupMetadata(backupId);
        
        if (!metadata) {
            throw new Error(`Backup not found: ${backupId}`);
        }

        if (requestedByUserId && metadata.user_id !== requestedByUserId) {
            throw new Error('Access denied: backup does not belong to authenticated user');
        }

        // Delete file
        try {
            await fs.unlink(metadata.file_path);
        } catch (error) {
            logger.warn({ error, filePath: metadata.file_path }, 'Failed to delete backup file');
        }

        // Delete metadata
        await this.db.run('DELETE FROM backups WHERE backup_id = ?', [backupId]);

        logger.info({ backupId }, 'Backup deleted');
    }

    private async saveBackupMetadata(metadata: BackupMetadata, contents: any, extras: { checksum: string }): Promise<void> {
        const metadataJson = JSON.stringify({ description: metadata.description ?? null });
        const contentsJson = JSON.stringify(contents ?? {});
        await this.db.run(
            `INSERT INTO backups (
                backup_id, user_id, type, creator, contents, metadata,
                file_path, size, checksum, compression_type, encrypted, status, timestamp
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
            [
                metadata.backup_id,
                metadata.user_id,
                metadata.backup_type,
                metadata.user_id,
                contentsJson,
                metadataJson,
                metadata.file_path,
                metadata.file_size,
                extras.checksum,
                metadata.compressed ? 'gzip' : 'none',
                metadata.encrypted ? 1 : 0,
                'COMPLETED',
                metadata.created_at.toISOString().slice(0, 19).replace('T', ' '),
            ]
        );
    }

    private rowToMetadata(row: any): BackupMetadata {
        let description: string | undefined;
        try {
            const meta = typeof row.metadata === 'string' ? JSON.parse(row.metadata) : row.metadata;
            description = meta?.description ?? undefined;
        } catch {
            description = undefined;
        }

        return {
            backup_id: row.backup_id,
            user_id: row.user_id,
            backup_type: row.type,
            file_path: row.file_path,
            file_size: typeof row.size === 'string' ? parseInt(row.size, 10) : row.size,
            created_at: new Date(row.timestamp),
            description,
            compressed: row.compression_type !== 'none',
            encrypted: row.encrypted === 1 || row.encrypted === true,
        };
    }

    private async getBackupMetadata(backupId: string): Promise<BackupMetadata | null> {
        const row = await this.db.get<any>(
            'SELECT * FROM backups WHERE backup_id = ?',
            [backupId]
        );

        if (!row) {
            return null;
        }

        return this.rowToMetadata(row);
    }

    private async getLastBackup(
        userId: string,
        type?: 'FULL' | 'DIFFERENTIAL' | 'INCREMENTAL'
    ): Promise<BackupMetadata | null> {
        let query = `
            SELECT * FROM backups 
            WHERE user_id = ?
        `;

        const params: any[] = [userId];

        if (type) {
            query += ` AND type = ?`;
            params.push(type);
        }

        query += ` ORDER BY timestamp DESC LIMIT 1`;

        const row = await this.db.get<any>(query, params);

        if (!row) {
            return null;
        }

        return this.rowToMetadata(row);
    }

    private generateBackupId(): string {
        return `backup_${Date.now()}_${Math.random().toString(36).substring(7)}`;
    }

    private simpleEncrypt(data: Buffer, key: string): Buffer {
        // Simplified encryption - in production, use crypto module properly
        // This is just XOR for demonstration
        const keyBuffer = Buffer.from(key);
        const encrypted = Buffer.alloc(data.length);

        for (let i = 0; i < data.length; i++) {
            encrypted[i] = data[i] ^ keyBuffer[i % keyBuffer.length];
        }

        return encrypted;
    }

    private simpleDecrypt(data: Buffer, key: string): Buffer {
        // XOR is symmetric
        return this.simpleEncrypt(data, key);
    }
}

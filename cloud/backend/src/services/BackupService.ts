/**
 * Backup Service
 * Handles database backups and restoration
 */

import * as fs from 'fs/promises';
import * as path from 'path';
import * as zlib from 'zlib';
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
                    backupData = await this.createFullBackup();
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

            await this.saveBackupMetadata(metadata);

            logger.info({ backupId, fileSize: stats.size }, 'Backup created successfully');

            return metadata;
        } catch (error) {
            logger.error({ error, backupId }, 'Failed to create backup');
            throw error;
        }
    }

    private async createFullBackup(): Promise<Buffer> {
        // Export all tables to JSON
        const tables = [
            'users', 'sessions', 'instances', 'instance_members', 
            'player_states', 'wished_terrors', 'voting_campaigns', 
            'player_votes', 'rounds', 'terror_appearances', 
            'settings', 'status_monitoring', 'error_logs', 
            'player_profiles', 'remote_commands', 'event_notifications'
        ];

        const backup: any = {
            version: '1.0',
            timestamp: new Date().toISOString(),
            tables: {},
        };

        for (const table of tables) {
            const rows = await this.db.all<any>(`SELECT * FROM ${table}`);
            backup.tables[table] = rows;
        }

        return Buffer.from(JSON.stringify(backup, null, 2));
    }

    private async createDifferentialBackup(userId: string): Promise<Buffer> {
        // Get last full backup
        const lastFullBackup = await this.getLastBackup(userId, 'FULL');
        
        if (!lastFullBackup) {
            logger.warn('No full backup found, creating full backup instead');
            return this.createFullBackup();
        }

        // Export only changed data since last full backup
        const sinceDate = lastFullBackup.created_at;
        const backup: any = {
            version: '1.0',
            type: 'DIFFERENTIAL',
            base_backup_id: lastFullBackup.backup_id,
            timestamp: new Date().toISOString(),
            changes: {},
        };

        // Get changed rounds
        backup.changes.rounds = await this.db.all<any>(
            `SELECT * FROM rounds WHERE created_at > ?`,
            [sinceDate]
        );

        // Get changed settings
        backup.changes.settings = await this.db.all<any>(
            `SELECT * FROM settings WHERE updated_at > ?`,
            [sinceDate]
        );

        // Add other changed tables as needed

        return Buffer.from(JSON.stringify(backup, null, 2));
    }

    private async createIncrementalBackup(userId: string): Promise<Buffer> {
        // Get last backup (any type)
        const lastBackup = await this.getLastBackup(userId);
        
        if (!lastBackup) {
            logger.warn('No previous backup found, creating full backup instead');
            return this.createFullBackup();
        }

        // Export only changed data since last backup
        const sinceDate = lastBackup.created_at;
        const backup: any = {
            version: '1.0',
            type: 'INCREMENTAL',
            base_backup_id: lastBackup.backup_id,
            timestamp: new Date().toISOString(),
            changes: {},
        };

        // Similar to differential but from last backup of any type
        backup.changes.rounds = await this.db.all<any>(
            `SELECT * FROM rounds WHERE created_at > ?`,
            [sinceDate]
        );

        return Buffer.from(JSON.stringify(backup, null, 2));
    }

    async restoreBackup(
        backupId: string,
        options: RestoreOptions = { validateBeforeRestore: true, createBackupBeforeRestore: true }
    ): Promise<void> {
        try {
            logger.info({ backupId, options }, 'Starting backup restoration');

            // Get backup metadata
            const metadata = await this.getBackupMetadata(backupId);
            if (!metadata) {
                throw new Error(`Backup not found: ${backupId}`);
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
                    await this.restoreFullBackup(backup);
                    break;
                case 'DIFFERENTIAL':
                case 'INCREMENTAL':
                    await this.restoreIncrementalBackup(backup);
                    break;
            }

            logger.info({ backupId }, 'Backup restored successfully');
        } catch (error) {
            logger.error({ error, backupId }, 'Failed to restore backup');
            throw error;
        }
    }

    private async restoreFullBackup(backup: any): Promise<void> {
        // Restore all tables
        for (const [table, rows] of Object.entries(backup.tables)) {
            // Clear existing data
            await this.db.run(`DELETE FROM ${table}`);

            // Insert backup data
            for (const row of rows as any[]) {
                const columns = Object.keys(row).join(', ');
                const placeholders = Object.keys(row).map(() => '?').join(', ');
                const values = Object.values(row);

                await this.db.execute(
                    `INSERT INTO ${table} (${columns}) VALUES (${placeholders})`,
                    values
                );
            }

            logger.info({ table, rowCount: (rows as any[]).length }, 'Table restored');
        }
    }

    private async restoreIncrementalBackup(backup: any): Promise<void> {
        // Apply changes from incremental/differential backup
        for (const [table, rows] of Object.entries(backup.changes)) {
            for (const row of rows as any[]) {
                const columns = Object.keys(row).join(', ');
                const placeholders = Object.keys(row).map(() => '?').join(', ');
                const values = Object.values(row);
                
                // Get primary key column(s) for UPDATE clause
                const updateClauses = columns.split(', ').map(col => `${col} = VALUES(${col})`).join(', ');

                // Use INSERT ... ON DUPLICATE KEY UPDATE to handle updates (MariaDB syntax)
                await this.db.execute(
                    `INSERT INTO ${table} (${columns}) VALUES (${placeholders})
                     ON DUPLICATE KEY UPDATE ${updateClauses}`,
                    values
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
    }

    async listBackups(userId: string): Promise<BackupMetadata[]> {
        const query = `
            SELECT * FROM backups 
            WHERE user_id = ? 
            ORDER BY created_at DESC
        `;

        const rows = await this.db.all<any>(query, [userId]);

        return rows.map(row => ({
            backup_id: row.backup_id,
            user_id: row.user_id,
            backup_type: row.backup_type,
            file_path: row.file_path,
            file_size: row.file_size,
            created_at: new Date(row.created_at),
            description: row.description,
            compressed: row.compressed === 1,
            encrypted: row.encrypted === 1,
        }));
    }

    async deleteBackup(backupId: string): Promise<void> {
        const metadata = await this.getBackupMetadata(backupId);
        
        if (!metadata) {
            throw new Error(`Backup not found: ${backupId}`);
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

    private async saveBackupMetadata(metadata: BackupMetadata): Promise<void> {
        await this.db.run(
            `INSERT INTO backups (
                backup_id, user_id, backup_type, file_path, 
                file_size, created_at, description, compressed, encrypted
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)`,
            [
                metadata.backup_id,
                metadata.user_id,
                metadata.backup_type,
                metadata.file_path,
                metadata.file_size,
                metadata.created_at.toISOString(),
                metadata.description,
                metadata.compressed ? 1 : 0,
                metadata.encrypted ? 1 : 0,
            ]
        );
    }

    private async getBackupMetadata(backupId: string): Promise<BackupMetadata | null> {
        const row = await this.db.get<any>(
            'SELECT * FROM backups WHERE backup_id = ?',
            [backupId]
        );

        if (!row) {
            return null;
        }

        return {
            backup_id: row.backup_id,
            user_id: row.user_id,
            backup_type: row.backup_type,
            file_path: row.file_path,
            file_size: row.file_size,
            created_at: new Date(row.created_at),
            description: row.description,
            compressed: row.compressed === 1,
            encrypted: row.encrypted === 1,
        };
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
            query += ` AND backup_type = ?`;
            params.push(type);
        }

        query += ` ORDER BY created_at DESC LIMIT 1`;

        const row = await this.db.get<any>(query, params);

        if (!row) {
            return null;
        }

        return {
            backup_id: row.backup_id,
            user_id: row.user_id,
            backup_type: row.backup_type,
            file_path: row.file_path,
            file_size: row.file_size,
            created_at: new Date(row.created_at),
            description: row.description,
            compressed: row.compressed === 1,
            encrypted: row.encrypted === 1,
        };
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

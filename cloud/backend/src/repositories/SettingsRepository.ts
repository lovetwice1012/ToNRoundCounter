/**
 * Settings Repository
 * Handles all settings-related database operations
 */

import { v4 as uuidv4 } from 'uuid';
import { getDatabase } from '../database/connection';
import { Settings, SettingsCategories } from '../models/types';
import { logger } from '../logger';

export class SettingsRepository {
    private db = getDatabase();

    async createSettings(userId: string, categories: SettingsCategories): Promise<Settings> {
        const settingsId = `settings_${uuidv4()}`;

        await this.db.run(
            `INSERT INTO settings (settings_id, user_id, version, categories)
             VALUES (?, ?, ?, ?)`,
            [settingsId, userId, 1, JSON.stringify(categories)]
        );

        logger.info({ settingsId, userId }, 'Settings created');

        return {
            settings_id: settingsId,
            user_id: userId,
            version: 1,
            categories,
            last_modified: new Date(),
        };
    }

    async getSettings(userId: string): Promise<Settings | undefined> {
        const row = await this.db.get<any>(
            `SELECT * FROM settings WHERE user_id = ? ORDER BY version DESC LIMIT 1`,
            [userId]
        );

        if (!row) {
            return undefined;
        }

        return {
            settings_id: row.settings_id,
            user_id: row.user_id,
            version: row.version,
            categories: JSON.parse(row.categories),
            last_modified: new Date(row.last_modified),
        };
    }

    async updateSettings(userId: string, categories: SettingsCategories): Promise<Settings> {
        const existing = await this.getSettings(userId);

        if (!existing) {
            return await this.createSettings(userId, categories);
        }

        const newVersion = existing.version + 1;

        await this.db.run(
            `UPDATE settings SET version = ?, categories = ?, last_modified = NOW()
             WHERE user_id = ?`,
            [newVersion, JSON.stringify(categories), userId]
        );

        logger.info({ userId, version: newVersion }, 'Settings updated');

        return {
            settings_id: existing.settings_id,
            user_id: userId,
            version: newVersion,
            categories,
            last_modified: new Date(),
        };
    }

    async getSettingsHistory(userId: string, limit: number = 10): Promise<Settings[]> {
        const rows = await this.db.all<any>(
            `SELECT * FROM settings WHERE user_id = ? ORDER BY version DESC LIMIT ?`,
            [userId, limit]
        );

        return rows.map(row => ({
            settings_id: row.settings_id,
            user_id: row.user_id,
            version: row.version,
            categories: JSON.parse(row.categories),
            last_modified: new Date(row.last_modified),
        }));
    }

    async deleteSettings(userId: string): Promise<void> {
        await this.db.run(`DELETE FROM settings WHERE user_id = ?`, [userId]);
        logger.info({ userId }, 'Settings deleted');
    }
}

export default new SettingsRepository();

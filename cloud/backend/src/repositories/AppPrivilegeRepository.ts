/**
 * Repository for administrator-managed privileged external app scopes.
 */

import { getDatabase } from '../database/connection';
import { logger } from '../logger';

export interface AppPrivilegeRecord {
    app_id: string;
    privileged_scopes: string[];
    description?: string;
    created_by?: string;
    created_at?: Date;
    updated_at?: Date;
}

export class AppPrivilegeRepository {
    private db = getDatabase();

    async listAppPrivileges(): Promise<AppPrivilegeRecord[]> {
        const rows = await this.db.all<any>(
            `SELECT app_id, privileged_scopes, description, created_by, created_at, updated_at
             FROM app_privileged_scopes
             ORDER BY app_id ASC`
        );

        return rows.map(row => this.mapRow(row));
    }

    async getAppPrivilege(appId: string): Promise<AppPrivilegeRecord | undefined> {
        const row = await this.db.get<any>(
            `SELECT app_id, privileged_scopes, description, created_by, created_at, updated_at
             FROM app_privileged_scopes
             WHERE app_id = ?`,
            [appId]
        );

        return row ? this.mapRow(row) : undefined;
    }

    async upsertAppPrivilege(
        appId: string,
        privilegedScopes: string[],
        description: string | undefined,
        createdBy: string
    ): Promise<AppPrivilegeRecord> {
        await this.db.run(
            `INSERT INTO app_privileged_scopes (app_id, privileged_scopes, description, created_by)
             VALUES (?, ?, ?, ?)
             ON DUPLICATE KEY UPDATE
                privileged_scopes = VALUES(privileged_scopes),
                description = VALUES(description),
                updated_at = CURRENT_TIMESTAMP`,
            [
                appId,
                JSON.stringify(privilegedScopes),
                description ?? null,
                createdBy,
            ]
        );

        logger.info({ appId, privilegedScopes, createdBy }, 'App privileged scopes updated');
        const updated = await this.getAppPrivilege(appId);
        if (!updated) {
            throw new Error('Failed to load updated app privilege');
        }

        return updated;
    }

    async deleteAppPrivilege(appId: string): Promise<void> {
        await this.db.run(
            `DELETE FROM app_privileged_scopes WHERE app_id = ?`,
            [appId]
        );

        logger.info({ appId }, 'App privileged scopes deleted');
    }

    private mapRow(row: any): AppPrivilegeRecord {
        return {
            app_id: row.app_id,
            privileged_scopes: this.parseScopes(row.privileged_scopes),
            description: row.description ?? undefined,
            created_by: row.created_by ?? undefined,
            created_at: row.created_at ? new Date(row.created_at) : undefined,
            updated_at: row.updated_at ? new Date(row.updated_at) : undefined,
        };
    }

    private parseScopes(value: any): string[] {
        if (!value) {
            return [];
        }

        try {
            const parsed = typeof value === 'string' ? JSON.parse(value) : value;
            if (!Array.isArray(parsed)) {
                return [];
            }

            return parsed.filter(item => typeof item === 'string' && item.trim()).map(item => item.trim());
        } catch {
            return [];
        }
    }
}

export default new AppPrivilegeRepository();

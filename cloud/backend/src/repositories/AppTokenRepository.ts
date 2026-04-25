/**
 * User-approved external application token repository.
 */

import { getDatabase } from '../database/connection';
import { logger } from '../logger';

export class AppTokenRepository {
    private db = getDatabase();

    async registerUserAppToken(userId: string, appId: string, appTokenHash: string, scopes: string[]): Promise<void> {
        await this.db.run(
            `INSERT INTO user_app_tokens (user_id, app_id, app_token_hash, scopes)
             VALUES (?, ?, ?, ?)
             ON DUPLICATE KEY UPDATE
                app_token_hash = VALUES(app_token_hash),
                scopes = VALUES(scopes),
                revoked_at = NULL,
                updated_at = CURRENT_TIMESTAMP`,
            [userId, appId, appTokenHash, JSON.stringify(scopes)]
        );

        logger.info({ userId, appId, scopes }, 'User app token registered');
    }

    async validateUserAppToken(userId: string, appId: string, appTokenHash: string): Promise<{ scopes: string[] } | undefined> {
        const row = await this.db.get<any>(
            `SELECT scopes
             FROM user_app_tokens
             WHERE user_id = ?
               AND app_id = ?
               AND app_token_hash = ?
               AND revoked_at IS NULL`,
            [userId, appId, appTokenHash]
        );

        if (!row) {
            return undefined;
        }

        return {
            scopes: this.parseScopes(row.scopes),
        };
    }

    async revokeUserAppToken(userId: string, appId: string): Promise<void> {
        await this.db.run(
            `UPDATE user_app_tokens
             SET revoked_at = CURRENT_TIMESTAMP,
                 updated_at = CURRENT_TIMESTAMP
             WHERE user_id = ? AND app_id = ?`,
            [userId, appId]
        );

        logger.info({ userId, appId }, 'User app token revoked');
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

export default new AppTokenRepository();

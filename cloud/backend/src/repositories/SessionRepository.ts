/**
 * Session Repository
 * Handles all session-related database operations
 */

import { v4 as uuidv4 } from 'uuid';
import { getDatabase } from '../database/connection';
import { Session } from '../models/types';
import { logger } from '../logger';

/**
 * Convert Date to MySQL/MariaDB datetime format (YYYY-MM-DD HH:MM:SS)
 */
function toMySQLDatetime(date: Date): string {
    return date.toISOString().slice(0, 19).replace('T', ' ');
}

export class SessionRepository {
    private db = getDatabase();

    async createSession(
        userId: string,
        playerId: string,
        clientVersion: string,
        ipAddress?: string,
        userAgent?: string
    ): Promise<Session> {
        const sessionId = `sess_${uuidv4()}`;
        const sessionToken = `token_${uuidv4()}`;
        const expiresAt = new Date(Date.now() + 24 * 60 * 60 * 1000); // 24 hours

        // Explicitly convert undefined to null for database insertion
        const params = [
            sessionId, 
            userId, 
            sessionToken, 
            playerId, 
            clientVersion, 
            toMySQLDatetime(expiresAt), 
            ipAddress !== undefined ? ipAddress : null, 
            userAgent !== undefined ? userAgent : null
        ];

        await this.db.run(
            `INSERT INTO sessions (session_id, user_id, session_token, player_id, client_version, expires_at, ip_address, user_agent)
             VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
            params
        );

        logger.info({ sessionId, userId, playerId }, 'Session created');

        return {
            session_id: sessionId,
            user_id: userId,
            session_token: sessionToken,
            player_id: playerId,
            client_version: clientVersion,
            expires_at: expiresAt,
            created_at: new Date(),
            last_activity: new Date(),
            ip_address: ipAddress,
            user_agent: userAgent,
        };
    }

    async getSessionByToken(token: string): Promise<Session | undefined> {
        const now = toMySQLDatetime(new Date());
        const row = await this.db.get<any>(
            `SELECT * FROM sessions WHERE session_token = ? AND expires_at > ?`,
            [token, now]
        );

        if (!row) {
            return undefined;
        }

        return this.mapRowToSession(row);
    }

    async getSessionById(sessionId: string): Promise<Session | undefined> {
        const row = await this.db.get<any>(
            `SELECT * FROM sessions WHERE session_id = ?`,
            [sessionId]
        );

        if (!row) {
            return undefined;
        }

        return this.mapRowToSession(row);
    }

    async updateLastActivity(sessionId: string): Promise<void> {
        const now = toMySQLDatetime(new Date());
        await this.db.run(
            `UPDATE sessions SET last_activity = ? WHERE session_id = ?`,
            [now, sessionId]
        );
    }

    async extendSession(sessionId: string): Promise<void> {
        const expiresAt = new Date(Date.now() + 24 * 60 * 60 * 1000);
        await this.db.run(
            `UPDATE sessions SET expires_at = ? WHERE session_id = ?`,
            [toMySQLDatetime(expiresAt), sessionId]
        );
    }

    async deleteSession(sessionId: string): Promise<void> {
        await this.db.run(`DELETE FROM sessions WHERE session_id = ?`, [sessionId]);
        logger.info({ sessionId }, 'Session deleted');
    }

    async deleteExpiredSessions(): Promise<void> {
        const now = toMySQLDatetime(new Date());
        await this.db.run(`DELETE FROM sessions WHERE expires_at < ?`, [now]);
    }

    async getUserSessions(userId: string): Promise<Session[]> {
        const now = toMySQLDatetime(new Date());
        const rows = await this.db.all<any>(
            `SELECT * FROM sessions WHERE user_id = ? AND expires_at > ?`,
            [userId, now]
        );

        return rows.map(row => this.mapRowToSession(row));
    }

    async createOneTimeToken(token: string, playerId: string, expiresAt: Date): Promise<void> {
        await this.db.run(
            `INSERT INTO one_time_tokens (token, player_id, expires_at)
             VALUES (?, ?, ?)
             ON DUPLICATE KEY UPDATE expires_at = ?`,
            [token, playerId, toMySQLDatetime(expiresAt), toMySQLDatetime(expiresAt)]
        );

        logger.info({ playerId, tokenPrefix: token.substring(0, 8) }, 'One-time token created');
    }

    async getOneTimeToken(token: string): Promise<{ player_id: string; expires_at: Date } | undefined> {
        const now = toMySQLDatetime(new Date());
        const row = await this.db.get<any>(
            `SELECT player_id, expires_at FROM one_time_tokens WHERE token = ? AND expires_at > ?`,
            [token, now]
        );

        if (!row) {
            return undefined;
        }

        return {
            player_id: row.player_id,
            expires_at: new Date(row.expires_at),
        };
    }

    async deleteOneTimeToken(token: string): Promise<void> {
        await this.db.run(`DELETE FROM one_time_tokens WHERE token = ?`, [token]);
        logger.info({ tokenPrefix: token.substring(0, 8) }, 'One-time token deleted');
    }

    async deleteExpiredOneTimeTokens(): Promise<void> {
        const now = toMySQLDatetime(new Date());
        await this.db.run(`DELETE FROM one_time_tokens WHERE expires_at < ?`, [now]);
    }

    private mapRowToSession(row: any): Session {
        return {
            session_id: row.session_id,
            user_id: row.user_id,
            session_token: row.session_token,
            player_id: row.player_id,
            client_version: row.client_version,
            expires_at: new Date(row.expires_at),
            created_at: new Date(row.created_at),
            last_activity: new Date(row.last_activity),
            ip_address: row.ip_address,
            user_agent: row.user_agent,
        };
    }
}

export default new SessionRepository();

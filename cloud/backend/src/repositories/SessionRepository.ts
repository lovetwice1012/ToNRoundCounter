/**
 * Session Repository
 * Handles all session-related database operations
 */

import { v4 as uuidv4 } from 'uuid';
import { getDatabase } from '../database/connection';
import { Session } from '../models/types';
import { logger } from '../logger';

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

        await this.db.run(
            `INSERT INTO sessions (session_id, user_id, session_token, player_id, client_version, expires_at, ip_address, user_agent)
             VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
            [sessionId, userId, sessionToken, playerId, clientVersion, expiresAt.toISOString(), ipAddress, userAgent]
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
        const row = await this.db.get<any>(
            `SELECT * FROM sessions WHERE session_token = ? AND expires_at > NOW()`,
            [token]
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
        await this.db.run(
            `UPDATE sessions SET last_activity = NOW() WHERE session_id = ?`,
            [sessionId]
        );
    }

    async extendSession(sessionId: string): Promise<void> {
        const expiresAt = new Date(Date.now() + 24 * 60 * 60 * 1000);
        await this.db.run(
            `UPDATE sessions SET expires_at = ? WHERE session_id = ?`,
            [expiresAt.toISOString(), sessionId]
        );
    }

    async deleteSession(sessionId: string): Promise<void> {
        await this.db.run(`DELETE FROM sessions WHERE session_id = ?`, [sessionId]);
        logger.info({ sessionId }, 'Session deleted');
    }

    async deleteExpiredSessions(): Promise<void> {
        await this.db.run(`DELETE FROM sessions WHERE expires_at < NOW()`);
    }

    async getUserSessions(userId: string): Promise<Session[]> {
        const rows = await this.db.all<any>(
            `SELECT * FROM sessions WHERE user_id = ? AND expires_at > NOW()`,
            [userId]
        );

        return rows.map(row => this.mapRowToSession(row));
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

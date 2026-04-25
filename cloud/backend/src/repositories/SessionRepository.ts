/**
 * Session Repository
 * Handles all session-related database operations
 */

import { v4 as uuidv4 } from 'uuid';
import { getDatabase } from '../database/connection';
import { ClientType, DeviceInfo, LoginDevice, Session } from '../models/types';
import { logger } from '../logger';

/**
 * Convert Date to MySQL/MariaDB datetime format (YYYY-MM-DD HH:MM:SS)
 */
function toMySQLDatetime(date: Date): string {
    return date.toISOString().slice(0, 19).replace('T', ' ');
}

function cleanString(value: any, maxLength: number): string | undefined {
    if (typeof value !== 'string') {
        return undefined;
    }

    const trimmed = value.trim();
    if (!trimmed) {
        return undefined;
    }

    return trimmed.length > maxLength ? trimmed.substring(0, maxLength) : trimmed;
}

function cleanNumber(value: any): number | undefined {
    const numeric = Number(value);
    if (!Number.isFinite(numeric) || numeric < 0) {
        return undefined;
    }

    return Math.round(numeric);
}

function normalizeDeviceInfo(deviceInfo?: DeviceInfo): DeviceInfo {
    if (!deviceInfo || typeof deviceInfo !== 'object' || Array.isArray(deviceInfo)) {
        return {};
    }

    return {
        device_id: cleanString(deviceInfo.device_id, 128),
        device_name: cleanString(deviceInfo.device_name ?? deviceInfo.machine_name, 255),
        machine_name: cleanString(deviceInfo.machine_name ?? deviceInfo.device_name, 255),
        os_description: cleanString(deviceInfo.os_description, 255),
        os_architecture: cleanString(deviceInfo.os_architecture, 64),
        processor_name: cleanString(deviceInfo.processor_name ?? deviceInfo.cpu_name, 255),
        cpu_name: cleanString(deviceInfo.cpu_name ?? deviceInfo.processor_name, 255),
        gpu_name: cleanString(deviceInfo.gpu_name, 2000),
        memory_mb: cleanNumber(deviceInfo.memory_mb),
    };
}

function parseStringArray(value: any): string[] | undefined {
    if (!value) {
        return undefined;
    }

    try {
        const parsed = typeof value === 'string' ? JSON.parse(value) : value;
        if (!Array.isArray(parsed)) {
            return undefined;
        }

        return parsed.filter(item => typeof item === 'string' && item.trim()).map(item => item.trim());
    } catch {
        return undefined;
    }
}

export class SessionRepository {
    private db = getDatabase();

    async createSession(
        userId: string,
        playerId: string,
        clientVersion: string,
        ipAddress?: string,
        userAgent?: string,
        clientType: string = 'unknown',
        deviceInfo?: DeviceInfo,
        appId?: string,
        appScopes?: string[]
    ): Promise<Session> {
        const sessionId = `sess_${uuidv4()}`;
        const sessionToken = `token_${uuidv4()}`;
        const expiresAt = new Date(Date.now() + 24 * 60 * 60 * 1000); // 24 hours
        const normalizedClientType = cleanString(clientType, 20) ?? 'unknown';
        const normalizedAppId = cleanString(appId, 128);
        const normalizedAppScopes = appScopes?.filter(scope => typeof scope === 'string' && scope.trim()).map(scope => scope.trim()) ?? [];

        // Explicitly convert undefined to null for database insertion
        const params = [
            sessionId, 
            userId, 
            sessionToken, 
            playerId, 
            clientVersion, 
            normalizedClientType,
            normalizedAppId ?? null,
            normalizedAppScopes.length > 0 ? JSON.stringify(normalizedAppScopes) : null,
            toMySQLDatetime(expiresAt), 
            ipAddress !== undefined ? ipAddress : null, 
            userAgent !== undefined ? userAgent : null
        ];

        await this.db.run(
            `INSERT INTO sessions (session_id, user_id, session_token, player_id, client_version, client_type, app_id, app_scopes, expires_at, ip_address, user_agent)
             VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
            params
        );

        logger.info({ sessionId, userId, playerId }, 'Session created');

        await this.recordLoginDevice(
            sessionId,
            userId,
            playerId,
            clientVersion,
            normalizedClientType,
            ipAddress,
            userAgent,
            deviceInfo
        );

        return {
            session_id: sessionId,
            user_id: userId,
            session_token: sessionToken,
            player_id: playerId,
            client_version: clientVersion,
            client_type: normalizedClientType as ClientType,
            app_id: normalizedAppId,
            app_scopes: normalizedAppScopes,
            expires_at: expiresAt,
            created_at: new Date(),
            last_activity: new Date(),
            ip_address: ipAddress,
            user_agent: userAgent,
        };
    }

    private async recordLoginDevice(
        sessionId: string,
        userId: string,
        playerId: string,
        clientVersion: string,
        clientType: string,
        ipAddress?: string,
        userAgent?: string,
        deviceInfo?: DeviceInfo
    ): Promise<void> {
        const normalized = normalizeDeviceInfo(deviceInfo);
        const deviceInfoJson = Object.values(normalized).some(value => value !== undefined)
            ? JSON.stringify(normalized)
            : null;

        await this.db.run(
            `INSERT INTO login_devices (
                session_id, user_id, player_id, client_type, client_version,
                device_id, device_name, os_description, os_architecture,
                processor_name, gpu_name, memory_mb, ip_address, user_agent, device_info
             ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
            [
                sessionId,
                userId,
                playerId,
                cleanString(clientType, 20) ?? 'unknown',
                cleanString(clientVersion, 50) ?? 'unknown',
                normalized.device_id ?? null,
                normalized.device_name ?? normalized.machine_name ?? null,
                normalized.os_description ?? null,
                normalized.os_architecture ?? null,
                normalized.processor_name ?? normalized.cpu_name ?? null,
                normalized.gpu_name ?? null,
                normalized.memory_mb ?? null,
                ipAddress !== undefined ? ipAddress : null,
                userAgent !== undefined ? userAgent : null,
                deviceInfoJson,
            ]
        );
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

    async rotateSession(sessionId: string): Promise<Session | undefined> {
        const existing = await this.getSessionById(sessionId);
        if (!existing) {
            return undefined;
        }

        const nextSessionId = `sess_${uuidv4()}`;
        const nextSessionToken = `token_${uuidv4()}`;
        const expiresAt = new Date(Date.now() + 24 * 60 * 60 * 1000);
        const now = new Date();

        await this.db.run(
            `UPDATE sessions
             SET session_id = ?, session_token = ?, expires_at = ?, last_activity = ?
             WHERE session_id = ?`,
            [
                nextSessionId,
                nextSessionToken,
                toMySQLDatetime(expiresAt),
                toMySQLDatetime(now),
                sessionId,
            ]
        );

        logger.info({ oldSessionId: sessionId, sessionId: nextSessionId }, 'Session rotated');

        return {
            ...existing,
            session_id: nextSessionId,
            session_token: nextSessionToken,
            expires_at: expiresAt,
            last_activity: now,
        };
    }

    async deleteSession(sessionId: string): Promise<void> {
        await this.db.run(`DELETE FROM sessions WHERE session_id = ?`, [sessionId]);
        logger.info({ sessionId }, 'Session deleted');
    }

    async deleteLoginDeviceForSession(sessionId: string): Promise<void> {
        await this.db.run(`DELETE FROM login_devices WHERE session_id = ?`, [sessionId]);
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

    async getRecentLoginDevices(userId: string, limit: number = 10): Promise<LoginDevice[]> {
        const safeLimit = Math.max(1, Math.min(50, Math.floor(Number(limit) || 10)));
        const rows = await this.db.all<any>(
            `SELECT * FROM login_devices WHERE user_id = ? ORDER BY logged_in_at DESC LIMIT ${safeLimit}`,
            [userId]
        );

        return rows.map(row => this.mapRowToLoginDevice(row));
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
            client_type: (row.client_type ?? 'unknown') as ClientType,
            app_id: row.app_id ?? undefined,
            app_scopes: parseStringArray(row.app_scopes),
            expires_at: new Date(row.expires_at),
            created_at: new Date(row.created_at),
            last_activity: new Date(row.last_activity),
            ip_address: row.ip_address,
            user_agent: row.user_agent,
        };
    }

    private mapRowToLoginDevice(row: any): LoginDevice {
        let parsedDeviceInfo: DeviceInfo | undefined;
        if (row.device_info) {
            try {
                parsedDeviceInfo = typeof row.device_info === 'string'
                    ? JSON.parse(row.device_info)
                    : row.device_info;
            } catch {
                parsedDeviceInfo = undefined;
            }
        }

        return {
            id: Number(row.id),
            session_id: row.session_id,
            user_id: row.user_id,
            player_id: row.player_id,
            client_type: row.client_type,
            client_version: row.client_version,
            device_id: row.device_id,
            device_name: row.device_name,
            os_description: row.os_description,
            os_architecture: row.os_architecture,
            processor_name: row.processor_name,
            gpu_name: row.gpu_name,
            memory_mb: row.memory_mb !== null && row.memory_mb !== undefined ? Number(row.memory_mb) : undefined,
            ip_address: row.ip_address,
            user_agent: row.user_agent,
            device_info: parsedDeviceInfo,
            logged_in_at: new Date(row.logged_in_at),
            last_seen_at: new Date(row.last_seen_at),
        };
    }
}

export default new SessionRepository();

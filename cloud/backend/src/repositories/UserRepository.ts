/**
 * User Repository
 * Handles all user-related database operations
 */

import { getDatabase } from '../database/connection';
import { logger } from '../logger';

export interface User {
    user_id: string;
    username: string;
    email: string;
    password_hash: string;
    display_name?: string;
    avatar?: string;
    roles: string[];
    permissions: string[];
    status: string;
    mfa_enabled: boolean;
    last_password_change: Date;
    created_at: Date;
    last_login?: Date;
    metadata?: any;
}

export class UserRepository {
    private db = getDatabase();

    async createUser(
        userId: string,
        username: string,
        email: string,
        displayName?: string
    ): Promise<User> {
        // For ToNRoundCounter, we use player_id as both user_id and username
        // No password required for now (auth is based on session tokens)
        const passwordHash = ''; // Empty password hash
        const roles = JSON.stringify(['player']);
        const permissions = JSON.stringify(['basic']);

        await this.db.run(
            `INSERT INTO users (user_id, username, email, password_hash, display_name, roles, permissions, status, mfa_enabled)
             VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)`,
            [
                userId,
                username,
                email || `${userId}@tonround.local`,
                passwordHash,
                displayName ?? username,
                roles,
                permissions,
                'ACTIVE',
                false
            ]
        );

        logger.info({ userId, username }, 'User created');

        return {
            user_id: userId,
            username,
            email: email || `${userId}@tonround.local`,
            password_hash: passwordHash,
            display_name: displayName ?? username,
            roles: ['player'],
            permissions: ['basic'],
            status: 'ACTIVE',
            mfa_enabled: false,
            last_password_change: new Date(),
            created_at: new Date(),
        };
    }

    async getUserById(userId: string): Promise<User | undefined> {
        const row = await this.db.get<any>(
            `SELECT * FROM users WHERE user_id = ?`,
            [userId]
        );

        if (!row) {
            return undefined;
        }

        return this.mapRowToUser(row);
    }

    async getUserByUsername(username: string): Promise<User | undefined> {
        const row = await this.db.get<any>(
            `SELECT * FROM users WHERE username = ?`,
            [username]
        );

        if (!row) {
            return undefined;
        }

        return this.mapRowToUser(row);
    }

    async updateLastLogin(userId: string): Promise<void> {
        await this.db.run(
            `UPDATE users SET last_login = NOW() WHERE user_id = ?`,
            [userId]
        );
    }

    async getUserOrCreate(userId: string, username: string, displayName?: string): Promise<User> {
        let user = await this.getUserById(userId);
        
        if (!user) {
            user = await this.createUser(userId, username, '', displayName);
        }

        return user;
    }

    async createUserWithApiKey(userId: string, username: string, apiKeyHash: string, displayName?: string): Promise<User> {
        const roles = JSON.stringify(['player']);
        const permissions = JSON.stringify(['basic']);

        await this.db.run(
            `INSERT INTO users (user_id, username, email, password_hash, display_name, roles, permissions, status, mfa_enabled)
             VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)`,
            [
                userId,
                username,
                `${userId}@tonround.local`,
                apiKeyHash,
                displayName ?? username,
                roles,
                permissions,
                'ACTIVE',
                false
            ]
        );

        logger.info({ userId, username }, 'User created with API key');

        return {
            user_id: userId,
            username,
            email: `${userId}@tonround.local`,
            password_hash: apiKeyHash,
            display_name: displayName ?? username,
            roles: ['player'],
            permissions: ['basic'],
            status: 'ACTIVE',
            mfa_enabled: false,
            last_password_change: new Date(),
            created_at: new Date(),
        };
    }

    async updateUserApiKey(userId: string, apiKeyHash: string): Promise<void> {
        await this.db.run(
            `UPDATE users SET password_hash = ?, last_password_change = CURRENT_TIMESTAMP WHERE user_id = ?`,
            [apiKeyHash, userId]
        );

        logger.info({ userId }, 'User API key updated');
    }

    private mapRowToUser(row: any): User {
        return {
            user_id: row.user_id,
            username: row.username,
            email: row.email,
            password_hash: row.password_hash,
            display_name: row.display_name,
            avatar: row.avatar,
            roles: JSON.parse(row.roles || '[]'),
            permissions: JSON.parse(row.permissions || '[]'),
            status: row.status,
            mfa_enabled: row.mfa_enabled,
            last_password_change: new Date(row.last_password_change),
            created_at: new Date(row.created_at),
            last_login: row.last_login ? new Date(row.last_login) : undefined,
            metadata: row.metadata ? JSON.parse(row.metadata) : undefined,
        };
    }
}

export default new UserRepository();

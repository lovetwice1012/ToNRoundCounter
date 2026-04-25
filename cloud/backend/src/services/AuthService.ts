/**
 * Authentication Service
 * Handles user authentication and session management
 */

import { v4 as uuidv4 } from 'uuid';
import * as crypto from 'crypto';
import SessionRepository from '../repositories/SessionRepository';
import UserRepository from '../repositories/UserRepository';
import AppTokenRepository from '../repositories/AppTokenRepository';
import AppPrivilegeRepository, { AppPrivilegeRecord } from '../repositories/AppPrivilegeRepository';
import { DeviceInfo, Session } from '../models/types';
import { logger } from '../logger';

export interface AppAuthorizationValidation {
    appId: string;
    scopes: string[];
}

const DEFAULT_APP_SCOPES = ['app:custom_rpc'];

const PUBLIC_APP_SCOPES = new Set([
    'app:custom_rpc',
    'read:instances',
    'read:player_state',
    'read:rounds',
    'read:voting',
    'read:auto_suicide',
    'read:wished_terrors',
    'read:profiles',
    'read:settings',
    'read:monitoring',
    'read:analytics',
    'read:backups',
]);

export const PRIVILEGED_APP_SCOPES = new Set([
    'cloud:instances:write',
    'cloud:player_state:write',
    'cloud:rounds:write',
    'cloud:threats:write',
    'cloud:voting:write',
    'cloud:auto_suicide:write',
    'cloud:wished_terrors:write',
    'cloud:profiles:write',
    'cloud:settings:write',
    'cloud:monitoring:write',
    'cloud:backups:write',
]);

export const ALL_APP_SCOPES = [
    ...Array.from(PUBLIC_APP_SCOPES),
    ...Array.from(PRIVILEGED_APP_SCOPES),
].sort();

export class AuthService {
    private readonly ACCESS_KEY = process.env.ACCESS_KEY || '';
    
    /**
     * Register a new user with API key
     * If user already exists, regenerate and return new API key
     */
    async registerUser(
        playerId: string,
        clientVersion: string,
        clientType: string,
        ipAddress?: string
    ): Promise<{ user_id: string; api_key: string; is_new: boolean }> {
        if (!playerId || !clientVersion) {
            throw new Error('playerId and clientVersion are required');
        }

        if (clientType !== 'csharp') {
            logger.warn({ playerId, clientType, ipAddress }, 'Rejected API key registration from non-C# client');
            throw new Error('API key registration is only allowed from the official C# client');
        }

        // Check if user already exists
        const existingUser = await UserRepository.getUserById(playerId);
        if (existingUser) {
            logger.warn({ playerId, ipAddress }, 'Rejected API key registration for existing user');
            throw new Error('User already exists');
        }

        // Generate new API key
        const apiKey = this.generateApiKey();
        const apiKeyHash = this.hashApiKey(apiKey);

        // Create new user with API key
        await UserRepository.createUserWithApiKey(playerId, playerId, apiKeyHash);
        logger.info({ playerId }, 'User registered successfully');

        return {
            user_id: playerId,
            api_key: apiKey,
            is_new: true,
        };
    }

    /**
     * Create session with API key authentication
     */
    async createSessionWithApiKey(
        playerId: string,
        apiKey: string,
        clientVersion: string,
        ipAddress?: string,
        userAgent?: string,
        clientType: string = 'unknown',
        deviceInfo?: DeviceInfo,
        appId?: string,
        appToken?: string
    ): Promise<Session> {
        if (!playerId || !apiKey || !clientVersion) {
            throw new Error('playerId, apiKey, and clientVersion are required');
        }

        // Get user and verify API key
        const user = await UserRepository.getUserById(playerId);
        if (!user) {
            logger.warn({ playerId, ipAddress }, 'User not found');
            throw new Error('Invalid credentials');
        }

        // Verify API key
        const apiKeyHash = this.hashApiKey(apiKey);
        if (user.password_hash !== apiKeyHash) {
            logger.warn({ playerId, ipAddress }, 'Invalid API key');
            throw new Error('Invalid credentials');
        }

        const appAuthorization = clientType === 'sdk'
            ? await this.validateExternalAppAuthorization(user.user_id, appId, appToken, ipAddress)
            : undefined;

        // Create session
        const session = await SessionRepository.createSession(
            user.user_id,
            playerId,
            clientVersion,
            ipAddress,
            userAgent,
            clientType,
            deviceInfo,
            appAuthorization?.appId,
            appAuthorization?.scopes
        );

        logger.info({ playerId, sessionId: session.session_id }, 'Session created with API key');

        return session;
    }

    async revokeUserAppToken(playerId: string, apiKey: string, appId: string): Promise<void> {
        if (!playerId || !apiKey || !appId) {
            throw new Error('playerId, apiKey, and appId are required');
        }

        const user = await UserRepository.getUserById(playerId);
        if (!user) {
            throw new Error('Invalid credentials');
        }

        const apiKeyHash = this.hashApiKey(apiKey);
        if (user.password_hash !== apiKeyHash) {
            throw new Error('Invalid credentials');
        }

        await AppTokenRepository.revokeUserAppToken(user.user_id, this.normalizeAppId(appId));
    }

    async createUserAppAuthorization(userId: string, appId: string, requestedScopes?: unknown): Promise<{ app_id: string; app_token: string; scopes: string[] }> {
        if (!userId || !appId) {
            throw new Error('userId and appId are required');
        }

        const user = await UserRepository.getUserById(userId);
        if (!user) {
            throw new Error('User not found');
        }

        const normalizedAppId = this.normalizeAppId(appId);
        const scopes = await this.normalizeRequestedAppScopes(normalizedAppId, requestedScopes);
        const appToken = this.generateAppToken();

        await AppTokenRepository.registerUserAppToken(
            user.user_id,
            normalizedAppId,
            this.hashAppToken(appToken),
            scopes
        );

        return {
            app_id: normalizedAppId,
            app_token: appToken,
            scopes,
        };
    }

    /**
     * Generate one-time login token
     */
    async generateOneTimeToken(playerId: string, apiKey: string): Promise<string> {
        // Verify API key first
        const user = await UserRepository.getUserById(playerId);
        if (!user) {
            throw new Error('Invalid credentials');
        }

        const apiKeyHash = this.hashApiKey(apiKey);
        if (user.password_hash !== apiKeyHash) {
            throw new Error('Invalid credentials');
        }

        // Generate one-time token (valid for 5 minutes)
        const token = uuidv4();
        const expiresAt = new Date(Date.now() + 5 * 60 * 1000); // 5 minutes

        await SessionRepository.createOneTimeToken(token, playerId, expiresAt);

        logger.info({ playerId }, 'One-time token generated');

        return token;
    }

    /**
     * Login with one-time token
     */
    async loginWithOneTimeToken(token: string, clientVersion: string, ipAddress?: string, userAgent?: string, deviceInfo?: DeviceInfo): Promise<Session> {
        if (!token || !clientVersion) {
            throw new Error('token and clientVersion are required');
        }

        // Get and validate token
        const tokenData = await SessionRepository.getOneTimeToken(token);
        if (!tokenData) {
            logger.warn({ token: token.substring(0, 8), ipAddress }, 'Invalid one-time token');
            throw new Error('Invalid or expired token');
        }

        // Delete token (one-time use)
        await SessionRepository.deleteOneTimeToken(token);

        // Create session
        const user = await UserRepository.getUserById(tokenData.player_id);
        if (!user) {
            throw new Error('User not found');
        }

        const session = await SessionRepository.createSession(
            user.user_id,
            tokenData.player_id,
            clientVersion,
            ipAddress,
            userAgent,
            'web',
            deviceInfo
        );

        logger.info({ playerId: tokenData.player_id, sessionId: session.session_id }, 'Session created with one-time token');

        return session;
    }

    /**
     * Generate API key
     */
    private generateApiKey(): string {
        return crypto.randomBytes(32).toString('hex');
    }

    private generateAppToken(): string {
        return `apptok_${crypto.randomBytes(32).toString('hex')}`;
    }

    /**
     * Hash API key
     */
    private hashApiKey(apiKey: string): string {
        return crypto.createHash('sha256').update(apiKey).digest('hex');
    }

    private hashAppToken(appToken: string): string {
        return crypto.createHash('sha256').update(appToken).digest('hex');
    }

    private normalizeAppId(appId: string): string {
        const normalized = appId.trim();
        if (!normalized || normalized.length > 128) {
            throw new Error('Invalid app_id');
        }

        return normalized;
    }

    private async normalizeRequestedAppScopes(appId: string, requestedScopes?: unknown): Promise<string[]> {
        const rawScopes = this.parseScopeInput(requestedScopes);
        const scopes = rawScopes.length > 0 ? rawScopes : DEFAULT_APP_SCOPES;
        const uniqueScopes = Array.from(new Set(scopes));
        const privilegedScopes = await this.getPrivilegedAppScopes(appId);
        const grantedScopes: string[] = [];

        for (const scope of uniqueScopes) {
            if (!/^[a-z][a-z0-9:_-]{1,80}$/.test(scope)) {
                throw new Error(`Invalid app scope: ${scope}`);
            }

            if (PUBLIC_APP_SCOPES.has(scope)) {
                grantedScopes.push(scope);
                continue;
            }

            if (PRIVILEGED_APP_SCOPES.has(scope)) {
                if (privilegedScopes.has('*') || privilegedScopes.has(scope)) {
                    grantedScopes.push(scope);
                    continue;
                }

                logger.warn({ appId, scope }, 'Ignored unregistered privileged app scope request');
                continue;
            }

            throw new Error(`Unknown app scope: ${scope}`);
        }

        return grantedScopes;
    }

    private parseScopeInput(value?: unknown): string[] {
        if (value === undefined || value === null) {
            return [];
        }

        if (typeof value === 'string') {
            return value
                .split(/[\s,]+/)
                .map(scope => scope.trim())
                .filter(Boolean);
        }

        if (Array.isArray(value)) {
            return value
                .filter(scope => typeof scope === 'string')
                .map(scope => scope.trim())
                .filter(Boolean);
        }

        throw new Error('scopes must be a string or string array');
    }

    private async getPrivilegedAppScopes(appId: string): Promise<Set<string>> {
        const record = await AppPrivilegeRepository.getAppPrivilege(appId);
        return new Set(record?.privileged_scopes ?? []);
    }

    private async getEffectiveAppScopes(appId: string, storedScopes: string[]): Promise<string[]> {
        const privilegedScopes = await this.getPrivilegedAppScopes(appId);
        const effectiveScopes: string[] = [];

        for (const scope of Array.from(new Set(storedScopes))) {
            if (PUBLIC_APP_SCOPES.has(scope)) {
                effectiveScopes.push(scope);
                continue;
            }

            if (PRIVILEGED_APP_SCOPES.has(scope)) {
                if (privilegedScopes.has(scope) || privilegedScopes.has('*')) {
                    effectiveScopes.push(scope);
                }
                continue;
            }
        }

        return effectiveScopes;
    }

    hasAppScope(scopes: string[] | undefined, requiredScope: string): boolean {
        if (!requiredScope) {
            return true;
        }

        const scopeSet = new Set(scopes ?? []);
        return scopeSet.has(requiredScope) || scopeSet.has('*');
    }

    getAllAppScopes(): string[] {
        return ALL_APP_SCOPES;
    }

    getPrivilegedScopeCatalog(): string[] {
        return Array.from(PRIVILEGED_APP_SCOPES).sort();
    }

    async listAppPrivileges(): Promise<AppPrivilegeRecord[]> {
        return AppPrivilegeRepository.listAppPrivileges();
    }

    async upsertAppPrivilege(appId: string, privilegedScopes: unknown, description: unknown, adminUserId: string): Promise<AppPrivilegeRecord> {
        const normalizedAppId = this.normalizeAppId(appId);
        const scopes = this.parseScopeInput(privilegedScopes);
        const uniqueScopes = Array.from(new Set(scopes));

        for (const scope of uniqueScopes) {
            if (!PRIVILEGED_APP_SCOPES.has(scope)) {
                throw new Error(`Scope '${scope}' is not a privileged app scope`);
            }
        }

        const cleanDescription = typeof description === 'string' && description.trim()
            ? description.trim().slice(0, 2000)
            : undefined;

        return AppPrivilegeRepository.upsertAppPrivilege(
            normalizedAppId,
            uniqueScopes,
            cleanDescription,
            adminUserId
        );
    }

    async deleteAppPrivilege(appId: string): Promise<void> {
        await AppPrivilegeRepository.deleteAppPrivilege(this.normalizeAppId(appId));
    }

    async validateExternalAppAuthorization(
        userId: string,
        appId?: string,
        appToken?: string,
        ipAddress?: string
    ): Promise<AppAuthorizationValidation> {
        if (!appId || !appToken) {
            logger.warn({ userId, ipAddress, hasAppId: !!appId, hasAppToken: !!appToken }, 'SDK request missing app credentials');
            throw new Error('app_id and app_token are required for SDK clients');
        }

        const normalizedAppId = this.normalizeAppId(appId);
        const appTokenHash = this.hashAppToken(appToken);
        const authorization = await AppTokenRepository.validateUserAppToken(userId, normalizedAppId, appTokenHash);

        if (!authorization) {
            logger.warn({ userId, appId: normalizedAppId, ipAddress }, 'SDK app token authorization failed');
            throw new Error('Invalid app authorization');
        }

        const effectiveScopes = await this.getEffectiveAppScopes(normalizedAppId, authorization.scopes);

        return {
            appId: normalizedAppId,
            scopes: effectiveScopes,
        };
    }
    
    async createSession(playerId: string, clientVersion: string, ipAddress?: string, userAgent?: string, accessKey?: string, clientType: string = 'unknown', deviceInfo?: DeviceInfo): Promise<Session> {
        // Validate input parameters
        if (!playerId || !clientVersion) {
            throw new Error('playerId and clientVersion are required');
        }

        // Validate access key if configured
        if (this.ACCESS_KEY && this.ACCESS_KEY.length > 0) {
            if (!accessKey || accessKey !== this.ACCESS_KEY) {
                logger.warn({ playerId, ipAddress }, 'Invalid access key attempt');
                throw new Error('Invalid access key');
            }
        }

        // For simplicity, we're using playerId as userId
        // In production, you'd have proper user management
        const userId = playerId;

        // Ensure user exists before creating session
        const user = await UserRepository.getUserOrCreate(userId, playerId, playerId);
        
        if (!user || !user.user_id) {
            throw new Error('Failed to create or retrieve user');
        }

        const session = await SessionRepository.createSession(
            user.user_id,
            playerId,
            clientVersion,
            ipAddress,
            userAgent,
            clientType,
            deviceInfo
        );

        logger.info({ playerId, sessionId: session.session_id }, 'Session created');

        return session;
    }

    async validateSession(sessionToken: string): Promise<Session | null> {
        const session = await SessionRepository.getSessionByToken(sessionToken);

        if (!session) {
            return null;
        }

        // Check if session is expired
        if (session.expires_at < new Date()) {
            await SessionRepository.deleteSession(session.session_id);
            return null;
        }

        // Update last activity
        await SessionRepository.updateLastActivity(session.session_id);

        return session;
    }

    async extendSession(sessionId: string): Promise<void> {
        await SessionRepository.extendSession(sessionId);
    }

    async logout(sessionId: string): Promise<void> {
        await SessionRepository.deleteSession(sessionId);
    }

    async cleanupExpiredSessions(): Promise<void> {
        await SessionRepository.deleteExpiredSessions();
        logger.info('Expired sessions cleaned up');
    }

    async refreshSession(sessionId: string): Promise<Session> {
        const session = await SessionRepository.getSessionById(sessionId);

        if (!session) {
            throw new Error('Session not found');
        }

        // Check if session is expired
        if (session.expires_at < new Date()) {
            await SessionRepository.deleteSession(sessionId);
            throw new Error('Session expired');
        }

        const refreshedSession = await SessionRepository.rotateSession(sessionId);

        if (!refreshedSession) {
            throw new Error('Failed to refresh session');
        }

        logger.info({ oldSessionId: sessionId, sessionId: refreshedSession.session_id }, 'Session refreshed and rotated');

        return refreshedSession;
    }
}

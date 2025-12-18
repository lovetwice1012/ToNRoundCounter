/**
 * Authentication Service
 * Handles user authentication and session management
 */

import { v4 as uuidv4 } from 'uuid';
import * as crypto from 'crypto';
import SessionRepository from '../repositories/SessionRepository';
import UserRepository from '../repositories/UserRepository';
import { Session } from '../models/types';
import { logger } from '../logger';

export class AuthService {
    private readonly ACCESS_KEY = process.env.ACCESS_KEY || '';
    
    /**
     * Register a new user with API key
     * If user already exists, regenerate and return new API key
     */
    async registerUser(playerId: string, clientVersion: string): Promise<{ user_id: string; api_key: string; is_new: boolean }> {
        if (!playerId || !clientVersion) {
            throw new Error('playerId and clientVersion are required');
        }

        // Check if user already exists
        const existingUser = await UserRepository.getUserById(playerId);
        
        // Generate new API key
        const apiKey = this.generateApiKey();
        const apiKeyHash = this.hashApiKey(apiKey);

        if (existingUser) {
            // Update existing user's API key
            await UserRepository.updateUserApiKey(playerId, apiKeyHash);
            logger.info({ playerId }, 'User API key regenerated');

            return {
                user_id: playerId,
                api_key: apiKey,
                is_new: false,
            };
        } else {
            // Create new user with API key
            await UserRepository.createUserWithApiKey(playerId, playerId, apiKeyHash);
            logger.info({ playerId }, 'User registered successfully');

            return {
                user_id: playerId,
                api_key: apiKey,
                is_new: true,
            };
        }
    }

    /**
     * Create session with API key authentication
     */
    async createSessionWithApiKey(playerId: string, apiKey: string, clientVersion: string, ipAddress?: string, userAgent?: string): Promise<Session> {
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

        // Create session
        const session = await SessionRepository.createSession(
            user.user_id,
            playerId,
            clientVersion,
            ipAddress,
            userAgent
        );

        logger.info({ playerId, sessionId: session.session_id }, 'Session created with API key');

        return session;
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
    async loginWithOneTimeToken(token: string, clientVersion: string, ipAddress?: string, userAgent?: string): Promise<Session> {
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
            userAgent
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

    /**
     * Hash API key
     */
    private hashApiKey(apiKey: string): string {
        return crypto.createHash('sha256').update(apiKey).digest('hex');
    }
    
    async createSession(playerId: string, clientVersion: string, ipAddress?: string, userAgent?: string, accessKey?: string): Promise<Session> {
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
            userAgent
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

        // Extend session
        await SessionRepository.extendSession(sessionId);

        const refreshedSession = await SessionRepository.getSessionById(sessionId);

        if (!refreshedSession) {
            throw new Error('Failed to refresh session');
        }

        logger.info({ sessionId }, 'Session refreshed');

        return refreshedSession;
    }
}

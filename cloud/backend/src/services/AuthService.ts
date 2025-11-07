/**
 * Authentication Service
 * Handles user authentication and session management
 */

import { v4 as uuidv4 } from 'uuid';
import SessionRepository from '../repositories/SessionRepository';
import { Session } from '../models/types';
import { logger } from '../logger';

export class AuthService {
    async createSession(playerId: string, clientVersion: string): Promise<Session> {
        // For simplicity, we're using playerId as userId
        // In production, you'd have proper user management
        const userId = playerId;

        const session = await SessionRepository.createSession(
            userId,
            playerId,
            clientVersion
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

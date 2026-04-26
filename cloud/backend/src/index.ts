/**
 * Main Server Entry Point
 * ToNRoundCounter Cloud Backend
 */

import express from 'express';
import { createServer } from 'http';
import dotenv from 'dotenv';
import path from 'path';
import { logger } from './logger';
import { initializeDatabase, closeDatabase } from './database/connection';
import { WebSocketHandler } from './websocket/WebSocketHandler';
import { ApiController } from './controllers/ApiController';
import { BackupService } from './services/BackupService';
import { AuthService } from './services/AuthService';

// Load environment variables
dotenv.config();

const app = express();
const port = process.env.PORT || 3000;

app.set('trust proxy', true);

// Middleware
app.use(express.json());
app.use(express.urlencoded({ extended: true }));

// CORS middleware
app.use((req, res, next) => {
    res.header('Access-Control-Allow-Origin', '*');
    res.header('Access-Control-Allow-Methods', 'GET, POST, PUT, DELETE, OPTIONS');
    res.header('Access-Control-Allow-Headers', 'Content-Type, Authorization, X-ToNR-App-Id, X-ToNR-App-Token, X-ToNRound-App-Id, X-ToNRound-App-Token');
    
    if (req.method === 'OPTIONS') {
        res.sendStatus(200);
    } else {
        next();
    }
});

// Request logging
app.use((req, res, next) => {
    logger.info({ method: req.method, path: req.path }, 'Incoming request');
    next();
});

// Create HTTP server
const server = createServer(app);

// Initialize WebSocket handler
const wsHandler = new WebSocketHandler(server);
const authService = new AuthService();
const ADMIN_USER_ID = 'yussy5373';

// Initialize API controller
const apiController = new ApiController(wsHandler);

function firstString(value: unknown): string | undefined {
    if (typeof value === 'string') {
        return value;
    }

    if (Array.isArray(value) && typeof value[0] === 'string') {
        return value[0];
    }

    return undefined;
}

function getRestSdkAppCredentials(req: express.Request): { appId?: string; appToken?: string } {
    return {
        appId: req.header('x-tonr-app-id')
            || req.header('x-tonround-app-id')
            || firstString(req.body?.app_id)
            || firstString(req.query?.app_id),
        appToken: req.header('x-tonr-app-token')
            || req.header('x-tonround-app-token')
            || firstString(req.body?.app_token)
            || firstString(req.query?.app_token),
    };
}

function getRestRequiredScope(req: express.Request): string | undefined {
    const method = req.method.toUpperCase();
    const path = req.path;

    if (path === '/api/v1/instances') {
        return method === 'GET' ? 'read:instances' : 'cloud:instances:write';
    }
    if (path.startsWith('/api/v1/instances/')) {
        return method === 'GET' ? 'read:instances' : 'cloud:instances:write';
    }
    if (path.startsWith('/api/v1/profiles/')) {
        return method === 'GET' ? 'read:profiles' : 'cloud:profiles:write';
    }
    if (path === '/api/v1/stats/terrors') {
        return 'read:analytics';
    }

    return undefined;
}

function isAdminRequest(req: express.Request): boolean {
    return (req as any).userId === ADMIN_USER_ID || (req as any).playerId === ADMIN_USER_ID;
}

function requireAdmin(req: express.Request, res: express.Response): boolean {
    if (isAdminRequest(req)) {
        return true;
    }

    res.status(403).json({
        error: {
            code: 'ADMIN_REQUIRED',
            message: 'Administrator permission is required',
        },
    });
    return false;
}

function serializeAppPrivilege(record: any): any {
    return {
        app_id: record.app_id,
        privileged_scopes: record.privileged_scopes,
        description: record.description ?? null,
        created_by: record.created_by ?? null,
        created_at: record.created_at instanceof Date ? record.created_at.toISOString() : record.created_at,
        updated_at: record.updated_at instanceof Date ? record.updated_at.toISOString() : record.updated_at,
    };
}

const requireAuthMiddleware = async (req: express.Request, res: express.Response, next: express.NextFunction) => {
    const authHeader = req.header('authorization') || '';
    const match = authHeader.match(/^Bearer\s+(\S+)$/i);
    const sessionToken = match ? match[1] : null;

    if (!sessionToken) {
        res.status(401).json({
            error: {
                code: 'AUTH_REQUIRED',
                message: 'Missing authorization token',
            },
        });
        return;
    }

    try {
        const session = await authService.validateSession(sessionToken);
        if (!session) {
            res.status(401).json({
                error: {
                    code: 'INVALID_SESSION',
                    message: 'Session is invalid or expired',
                },
            });
            return;
        }

        if (session.client_type === 'sdk') {
            const { appId, appToken } = getRestSdkAppCredentials(req);
            try {
                const appAuthorization = await authService.validateExternalAppAuthorization(
                    session.user_id,
                    appId,
                    appToken,
                    req.ip
                );

                if (!session.app_id || appAuthorization.appId !== session.app_id) {
                    logger.warn({ userId: session.user_id, sessionAppId: session.app_id, requestAppId: appAuthorization.appId }, 'REST SDK app authorization does not match session');
                    res.status(403).json({
                        error: {
                            code: 'APP_AUTH_REQUIRED',
                            message: 'Valid APPID and APPToken are required for SDK sessions',
                        },
                    });
                    return;
                }

                const requiredScope = getRestRequiredScope(req);
                if (requiredScope && !authService.hasAppScope(appAuthorization.scopes, requiredScope)) {
                    res.status(403).json({
                        error: {
                            code: 'APP_SCOPE_REQUIRED',
                            message: `Scope '${requiredScope}' is required for this SDK REST request`,
                        },
                    });
                    return;
                }
            } catch (error: any) {
                logger.warn({ error, userId: session.user_id }, 'REST SDK app authorization failed');
                res.status(403).json({
                    error: {
                        code: 'APP_AUTH_REQUIRED',
                        message: error.message || 'Valid APPID and APPToken are required for SDK sessions',
                    },
                });
                return;
            }
        }

        (req as any).session = session;
        (req as any).userId = session.user_id;
        (req as any).playerId = session.player_id;
        next();
    } catch (error: any) {
        logger.error({ error }, 'Failed to validate REST session');
        res.status(500).json({
            error: {
                code: 'INTERNAL_ERROR',
                message: 'Failed to validate session',
            },
        });
    }
};

function escapeHtml(value: string): string {
    return value
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

function scriptStringLiteral(value: string): string {
    return JSON.stringify(value)
        .replace(/</g, '\\u003c')
        .replace(/>/g, '\\u003e')
        .replace(/&/g, '\\u0026')
        .replace(/\u2028/g, '\\u2028')
        .replace(/\u2029/g, '\\u2029');
}

function renderOneTimeLoginBridge(session: { session_token: string; player_id: string; user_id: string }): string {
    const persistedState = JSON.stringify({
        state: {
            sessionToken: session.session_token,
            playerId: session.player_id,
            userId: session.user_id,
        },
        version: 0,
    });

    return `<!doctype html>
<html lang="ja">
<head>
  <meta charset="utf-8">
  <meta name="referrer" content="no-referrer">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>ToNRoundCounter Cloud</title>
</head>
<body>
  <script>
    localStorage.setItem('tonround-cloud-storage', ${scriptStringLiteral(persistedState)});
    const returnTo = sessionStorage.getItem('tonround-cloud-return-to') || '/dashboard';
    sessionStorage.removeItem('tonround-cloud-return-to');
    location.replace(returnTo);
  </script>
  <noscript>Enable JavaScript and try logging in again.</noscript>
</body>
</html>`;
}

function validateAppRedirectUri(value: unknown): string {
    if (typeof value !== 'string' || !value.trim()) {
        throw new Error('redirect_uri is required');
    }

    if (value.length > 2048) {
        throw new Error('redirect_uri is too long');
    }

    let parsed: URL;
    try {
        parsed = new URL(value);
    } catch {
        throw new Error('redirect_uri is invalid');
    }

    const blockedProtocols = new Set(['javascript:', 'data:', 'vbscript:', 'file:']);
    if (blockedProtocols.has(parsed.protocol.toLowerCase())) {
        throw new Error('redirect_uri protocol is not allowed');
    }

    return parsed.toString();
}

function buildAppAuthorizationCallback(
    redirectUri: string,
    appId: string,
    appToken: string,
    state?: string,
    scopes: string[] = []
): string {
    const callback = new URL(redirectUri);
    callback.searchParams.set('app_id', appId);
    callback.searchParams.set('app_token', appToken);
    callback.searchParams.set('token_type', 'app_token');
    if (scopes.length > 0) {
        callback.searchParams.set('scope', scopes.join(' '));
    }
    if (state) {
        callback.searchParams.set('state', state);
    }

    return callback.toString();
}

function renderOneTimeLoginError(message: string): string {
    return `<!doctype html>
<html lang="ja">
<head>
  <meta charset="utf-8">
  <meta name="referrer" content="no-referrer">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>ToNRoundCounter Cloud Login Error</title>
</head>
<body>
  <h1>Login failed</h1>
  <p>${escapeHtml(message)}</p>
</body>
</html>`;
}

// REST API Routes
app.post('/api/auth/one-time-token', async (req, res) => {
    const token = typeof req.body?.token === 'string' ? req.body.token.trim() : '';
    const clientVersion = typeof req.body?.client_version === 'string' && req.body.client_version.trim()
        ? req.body.client_version.trim()
        : '1.0.0';
    const wantsRedirect = req.body?.redirect === true || req.body?.redirect === '1' || req.body?.redirect === 'true';

    res.setHeader('Referrer-Policy', 'no-referrer');
    res.setHeader('Cache-Control', 'no-store, max-age=0');
    res.setHeader('Pragma', 'no-cache');
    res.setHeader('X-Content-Type-Options', 'nosniff');

    if (!token) {
        if (wantsRedirect) {
            res.status(400).type('html').send(renderOneTimeLoginError('One-time token is required.'));
            return;
        }

        res.status(400).json({
            error: {
                code: 'INVALID_PARAMS',
                message: 'token is required',
            },
        });
        return;
    }

    try {
        const session = await authService.loginWithOneTimeToken(
            token,
            clientVersion,
            req.ip,
            req.header('user-agent') || undefined,
            typeof req.body?.device_info === 'object' ? req.body.device_info : undefined
        );

        const payload = {
            session_id: session.session_id,
            session_token: session.session_token,
            player_id: session.player_id,
            user_id: session.user_id,
            expires_at: session.expires_at.toISOString(),
        };

        if (wantsRedirect) {
            res.type('html').send(renderOneTimeLoginBridge(payload));
            return;
        }

        res.json(payload);
    } catch (error: any) {
        logger.warn({ error }, 'One-time token POST login failed');
        if (wantsRedirect) {
            res.status(401).type('html').send(renderOneTimeLoginError(error.message || 'Invalid or expired one-time token.'));
            return;
        }

        res.status(401).json({
            error: {
                code: 'INVALID_TOKEN',
                message: error.message || 'Invalid or expired token',
            },
        });
    }
});

app.post('/api/v1/app-authorizations', requireAuthMiddleware, async (req, res) => {
    res.setHeader('Cache-Control', 'no-store, max-age=0');
    res.setHeader('Pragma', 'no-cache');

    try {
        const appId = typeof req.body?.app_id === 'string' ? req.body.app_id.trim() : '';
        const requestedScopes = req.body?.scopes ?? req.body?.scope;
        const redirectUri = validateAppRedirectUri(req.body?.redirect_uri);
        const state = typeof req.body?.state === 'string' && req.body.state.length <= 1024
            ? req.body.state
            : undefined;

        if (!appId) {
            res.status(400).json({
                error: {
                    code: 'INVALID_PARAMS',
                    message: 'app_id is required',
                },
            });
            return;
        }

        const authorization = await authService.createUserAppAuthorization((req as any).userId, appId, requestedScopes);
        const callbackUri = buildAppAuthorizationCallback(
            redirectUri,
            authorization.app_id,
            authorization.app_token,
            state,
            authorization.scopes
        );

        res.json({
            app_id: authorization.app_id,
            app_token: authorization.app_token,
            scopes: authorization.scopes,
            redirect_uri: callbackUri,
        });
    } catch (error: any) {
        logger.warn({ error }, 'App authorization failed');
        res.status(400).json({
            error: {
                code: 'INVALID_APP_AUTHORIZATION_REQUEST',
                message: error.message || 'Invalid app authorization request',
            },
        });
    }
});

app.get('/api/v1/admin/app-privileges', requireAuthMiddleware, async (req, res) => {
    if (!requireAdmin(req, res)) {
        return;
    }

    try {
        const records = await authService.listAppPrivileges();
        res.json({
            app_privileges: records.map(serializeAppPrivilege),
            privileged_scopes: authService.getPrivilegedScopeCatalog(),
            available_scopes: authService.getAllAppScopes(),
        });
    } catch (error: any) {
        logger.error({ error }, 'Failed to list app privileges');
        res.status(500).json({
            error: {
                code: 'INTERNAL_ERROR',
                message: error.message || 'Failed to list app privileges',
            },
        });
    }
});

app.put('/api/v1/admin/app-privileges/:appId', requireAuthMiddleware, async (req, res) => {
    if (!requireAdmin(req, res)) {
        return;
    }

    try {
        const record = await authService.upsertAppPrivilege(
            req.params.appId,
            req.body?.privileged_scopes,
            req.body?.description,
            (req as any).userId
        );

        res.json({
            app_privilege: serializeAppPrivilege(record),
            privileged_scopes: authService.getPrivilegedScopeCatalog(),
        });
    } catch (error: any) {
        logger.warn({ error, appId: req.params.appId }, 'Failed to update app privilege');
        res.status(400).json({
            error: {
                code: 'INVALID_APP_PRIVILEGE',
                message: error.message || 'Failed to update app privilege',
            },
        });
    }
});

app.delete('/api/v1/admin/app-privileges/:appId', requireAuthMiddleware, async (req, res) => {
    if (!requireAdmin(req, res)) {
        return;
    }

    try {
        await authService.deleteAppPrivilege(req.params.appId);
        res.json({ success: true });
    } catch (error: any) {
        logger.warn({ error, appId: req.params.appId }, 'Failed to delete app privilege');
        res.status(400).json({
            error: {
                code: 'INVALID_APP_PRIVILEGE',
                message: error.message || 'Failed to delete app privilege',
            },
        });
    }
});

app.get('/api/v1/instances', requireAuthMiddleware, (req, res) => apiController.getInstances(req, res));
app.get('/api/v1/instances/:instanceId', requireAuthMiddleware, (req, res) => apiController.getInstance(req, res));
app.post('/api/v1/instances', requireAuthMiddleware, (req, res) => apiController.createInstance(req, res));
app.put('/api/v1/instances/:instanceId', requireAuthMiddleware, (req, res) => apiController.updateInstance(req, res));
app.delete('/api/v1/instances/:instanceId', requireAuthMiddleware, (req, res) => apiController.deleteInstance(req, res));
app.get('/api/v1/profiles/:playerId', requireAuthMiddleware, (req, res) => apiController.getProfile(req, res));
app.put('/api/v1/profiles/:playerId', requireAuthMiddleware, (req, res) => apiController.updateProfile(req, res));
app.get('/api/v1/stats/terrors', requireAuthMiddleware, (req, res) => apiController.getTerrorStats(req, res));

// Health check endpoint
app.get('/health', (req, res) => {
    res.json({
        status: 'ok',
        timestamp: new Date().toISOString(),
        version: '1.0.0',
    });
});

// 404 handler
app.use((req, res) => {
    res.status(404).json({
        error: {
            code: 'NOT_FOUND',
            message: 'Endpoint not found',
        },
    });
});

// Error handler
app.use((err: any, req: express.Request, res: express.Response, next: express.NextFunction) => {
    logger.error({ err }, 'Unhandled error');
    res.status(500).json({
        error: {
            code: 'INTERNAL_ERROR',
            message: err.message || 'Internal server error',
        },
    });
});

// Start server
async function startServer() {
    try {
        // Initialize database
        logger.info('Initializing database...');
        await initializeDatabase();
        
        // Initialize backup service
        const backupService = new BackupService();
        await backupService.initialize();
        logger.info('Backup service initialized');
        
        // Start HTTP server
        server.listen(port, () => {
            logger.info({ port }, 'Server started successfully');
            logger.info(`HTTP server: http://localhost:${port}`);
            logger.info(`WebSocket server: ws://localhost:${port}/ws`);
        });
    } catch (error) {
        logger.error({ error }, 'Failed to start server');
        process.exit(1);
    }
}

// Graceful shutdown
process.on('SIGINT', async () => {
    logger.info('Shutting down gracefully...');
    
    try {
        await closeDatabase();
        server.close(() => {
            logger.info('Server closed');
            process.exit(0);
        });
    } catch (error) {
        logger.error({ error }, 'Error during shutdown');
        process.exit(1);
    }
});

process.on('SIGTERM', async () => {
    logger.info('SIGTERM received, shutting down...');
    
    try {
        await closeDatabase();
        server.close(() => {
            logger.info('Server closed');
            process.exit(0);
        });
    } catch (error) {
        logger.error({ error }, 'Error during shutdown');
        process.exit(1);
    }
});

// Start the server
startServer();

export { app, server, wsHandler };

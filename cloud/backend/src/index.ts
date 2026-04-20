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

// Middleware
app.use(express.json());
app.use(express.urlencoded({ extended: true }));

// CORS middleware
app.use((req, res, next) => {
    res.header('Access-Control-Allow-Origin', '*');
    res.header('Access-Control-Allow-Methods', 'GET, POST, PUT, DELETE, OPTIONS');
    res.header('Access-Control-Allow-Headers', 'Content-Type, Authorization');
    
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

// Initialize API controller
const apiController = new ApiController(wsHandler);

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

// REST API Routes
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

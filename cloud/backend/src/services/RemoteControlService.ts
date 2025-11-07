/**
 * Remote Control Service
 * Handles remote command execution
 */

import { v4 as uuidv4 } from 'uuid';
import { getDatabase } from '../database/connection';
import { RemoteCommand } from '../models/types';
import { logger } from '../logger';

export class RemoteControlService {
    private db = getDatabase();
    private wsHandler: any;
    private commandHandlers: Map<string, (command: RemoteCommand) => Promise<any>> = new Map();

    constructor(wsHandler: any) {
        this.wsHandler = wsHandler;
        this.registerDefaultHandlers();
    }

    private registerDefaultHandlers(): void {
        // Round control handlers
        this.registerHandler('round.start', async (command) => {
            logger.info({ command }, 'Starting round remotely');
            return { success: true, message: 'Round started' };
        });

        this.registerHandler('round.stop', async (command) => {
            logger.info({ command }, 'Stopping round remotely');
            return { success: true, message: 'Round stopped' };
        });

        this.registerHandler('round.reset', async (command) => {
            logger.info({ command }, 'Resetting round remotely');
            return { success: true, message: 'Round reset' };
        });

        // Settings control handlers
        this.registerHandler('settings.update', async (command) => {
            logger.info({ command }, 'Updating settings remotely');
            const { settings } = command.parameters;
            return { success: true, message: 'Settings updated', settings };
        });

        // Emergency stop handler
        this.registerHandler('emergency.stop', async (command) => {
            logger.warn({ command }, 'Emergency stop triggered');
            return { success: true, message: 'Emergency stop executed' };
        });

        // Application control
        this.registerHandler('app.restart', async (command) => {
            logger.info({ command }, 'Restarting application remotely');
            return { success: true, message: 'Application restart initiated' };
        });
    }

    registerHandler(
        action: string,
        handler: (command: RemoteCommand) => Promise<any>
    ): void {
        this.commandHandlers.set(action, handler);
        logger.info({ action }, 'Command handler registered');
    }

    async createCommand(
        userId: string,
        instanceId: string | undefined,
        commandType: 'ROUND_CONTROL' | 'SETTINGS_CHANGE' | 'EMERGENCY_STOP',
        action: string,
        parameters: Record<string, any>,
        initiator: string,
        priority: number = 0
    ): Promise<RemoteCommand> {
        const commandId = `cmd_${uuidv4()}`;

        await this.db.run(
            `INSERT INTO remote_commands (command_id, user_id, instance_id, command_type, action, parameters, initiator, priority)
             VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
            [
                commandId,
                userId,
                instanceId,
                commandType,
                action,
                JSON.stringify(parameters),
                initiator,
                priority,
            ]
        );

        logger.info({ commandId, action, userId }, 'Remote command created');

        const command: RemoteCommand = {
            command_id: commandId,
            user_id: userId,
            instance_id: instanceId,
            command_type: commandType,
            action,
            parameters,
            status: 'PENDING',
            initiator,
            priority,
            created_at: new Date(),
        };

        // Execute command immediately if high priority
        if (priority > 0) {
            setImmediate(() => this.executeCommand(commandId));
        }

        return command;
    }

    async executeCommand(commandId: string): Promise<any> {
        const command = await this.getCommand(commandId);

        if (!command) {
            throw new Error('Command not found');
        }

        if (command.status !== 'PENDING') {
            throw new Error(`Command already ${command.status}`);
        }

        // Update status to executing
        await this.updateCommandStatus(commandId, 'EXECUTING');

        try {
            const handler = this.commandHandlers.get(command.action);

            if (!handler) {
                throw new Error(`No handler for action: ${command.action}`);
            }

            const result = await handler(command);

            // Update command with result
            await this.db.run(
                `UPDATE remote_commands 
                 SET status = 'COMPLETED', result = ?, completed_at = NOW()
                 WHERE command_id = ?`,
                [JSON.stringify(result), commandId]
            );

            logger.info({ commandId, action: command.action }, 'Command executed successfully');

            // Broadcast command result
            this.broadcastCommandResult(command.user_id, commandId, result);

            return result;
        } catch (error: any) {
            // Update command with error
            await this.db.run(
                `UPDATE remote_commands 
                 SET status = 'FAILED', error = ?, completed_at = NOW()
                 WHERE command_id = ?`,
                [error.message, commandId]
            );

            logger.error({ commandId, error }, 'Command execution failed');

            // Broadcast command error
            this.broadcastCommandError(command.user_id, commandId, error.message);

            throw error;
        }
    }

    async getCommand(commandId: string): Promise<RemoteCommand | undefined> {
        const row = await this.db.get<any>(
            `SELECT * FROM remote_commands WHERE command_id = ?`,
            [commandId]
        );

        if (!row) {
            return undefined;
        }

        return this.mapRowToCommand(row);
    }

    async getCommandsByUser(
        userId: string,
        status?: 'PENDING' | 'EXECUTING' | 'COMPLETED' | 'FAILED',
        limit: number = 50
    ): Promise<RemoteCommand[]> {
        let query = `SELECT * FROM remote_commands WHERE user_id = ?`;
        const params: any[] = [userId];

        if (status) {
            query += ` AND status = ?`;
            params.push(status);
        }

        query += ` ORDER BY priority DESC, created_at DESC LIMIT ?`;
        params.push(limit);

        const rows = await this.db.all<any>(query, params);

        return rows.map(row => this.mapRowToCommand(row));
    }

    async getCommandsByInstance(
        instanceId: string,
        status?: 'PENDING' | 'EXECUTING' | 'COMPLETED' | 'FAILED',
        limit: number = 50
    ): Promise<RemoteCommand[]> {
        let query = `SELECT * FROM remote_commands WHERE instance_id = ?`;
        const params: any[] = [instanceId];

        if (status) {
            query += ` AND status = ?`;
            params.push(status);
        }

        query += ` ORDER BY created_at DESC LIMIT ?`;
        params.push(limit);

        const rows = await this.db.all<any>(query, params);
        return rows.map(row => this.mapRowToCommand(row));
    }

    async updateCommandStatus(
        commandId: string,
        status: 'PENDING' | 'EXECUTING' | 'COMPLETED' | 'FAILED'
    ): Promise<void> {
        const updateField = status === 'EXECUTING' ? 'executed_at' : 'completed_at';

        await this.db.run(
            `UPDATE remote_commands 
             SET status = ?, ${updateField} = NOW()
             WHERE command_id = ?`,
            [status, commandId]
        );
    }

    async cancelCommand(commandId: string): Promise<void> {
        await this.db.run(
            `UPDATE remote_commands 
             SET status = 'FAILED', error = 'Cancelled by user', completed_at = NOW()
             WHERE command_id = ? AND status = 'PENDING'`,
            [commandId]
        );

        logger.info({ commandId }, 'Command cancelled');
    }

    private broadcastCommandResult(userId: string, commandId: string, result: any): void {
        logger.debug({ userId, commandId }, 'Broadcasting command result');
        this.wsHandler.broadcastToUser(userId, {
            stream: 'remote.command.completed',
            data: {
                command_id: commandId,
                result,
            },
            timestamp: new Date().toISOString(),
        });
    }

    private broadcastCommandError(userId: string, commandId: string, error: string): void {
        logger.debug({ userId, commandId, error }, 'Broadcasting command error');
        this.wsHandler.broadcastToUser(userId, {
            stream: 'remote.command.failed',
            data: {
                command_id: commandId,
                error,
            },
            timestamp: new Date().toISOString(),
        });
    }

    private mapRowToCommand(row: any): RemoteCommand {
        return {
            command_id: row.command_id,
            user_id: row.user_id,
            instance_id: row.instance_id,
            command_type: row.command_type,
            action: row.action,
            parameters: JSON.parse(row.parameters),
            status: row.status,
            result: row.result ? JSON.parse(row.result) : undefined,
            error: row.error,
            initiator: row.initiator,
            priority: row.priority,
            created_at: new Date(row.created_at),
            executed_at: row.executed_at ? new Date(row.executed_at) : undefined,
            completed_at: row.completed_at ? new Date(row.completed_at) : undefined,
        };
    }
}

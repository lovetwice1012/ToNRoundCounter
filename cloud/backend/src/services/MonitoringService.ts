/**
 * Monitoring Service
 * Handles status monitoring and error logging
 */

import { v4 as uuidv4 } from 'uuid';
import { getDatabase } from '../database/connection';
import { StatusMonitoring, ErrorLog } from '../models/types';
import { logger } from '../logger';

export class MonitoringService {
    private db = getDatabase();
    private wsHandler: any;

    constructor(wsHandler: any) {
        this.wsHandler = wsHandler;
    }

    async reportStatus(
        userId: string,
        instanceId: string | undefined,
        statusData: {
            application_status: 'RUNNING' | 'STOPPED' | 'ERROR';
            application_version?: string;
            uptime: number;
            memory_usage: number;
            cpu_usage: number;
            osc_status?: 'CONNECTED' | 'DISCONNECTED' | 'ERROR';
            osc_latency?: number;
            vrchat_status?: 'CONNECTED' | 'DISCONNECTED' | 'ERROR';
            vrchat_world_id?: string;
            vrchat_instance_id?: string;
        }
    ): Promise<StatusMonitoring> {
        const statusId = `status_${uuidv4()}`;

        await this.db.run(
            `INSERT INTO status_monitoring (
                status_id, user_id, instance_id, application_status, application_version,
                uptime, memory_usage, cpu_usage, osc_status, osc_latency,
                vrchat_status, vrchat_world_id, vrchat_instance_id
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
            [
                statusId,
                userId,
                instanceId,
                statusData.application_status,
                statusData.application_version,
                statusData.uptime,
                statusData.memory_usage,
                statusData.cpu_usage,
                statusData.osc_status,
                statusData.osc_latency,
                statusData.vrchat_status,
                statusData.vrchat_world_id,
                statusData.vrchat_instance_id,
            ]
        );

        const status: StatusMonitoring = {
            status_id: statusId,
            user_id: userId,
            instance_id: instanceId,
            ...statusData,
            timestamp: new Date(),
        };

        // Broadcast status update
        this.broadcastStatusUpdate(userId, status);

        // Check for critical conditions
        if (statusData.application_status === 'ERROR') {
            await this.logError(userId, instanceId, 'CRITICAL', 'Application error detected', {
                status: statusData,
            });
        }

        return status;
    }

    async getLatestStatus(userId: string): Promise<StatusMonitoring | undefined> {
        const row = await this.db.get<any>(
            `SELECT * FROM status_monitoring WHERE user_id = ? ORDER BY timestamp DESC LIMIT 1`,
            [userId]
        );

        if (!row) {
            return undefined;
        }

        return this.mapRowToStatus(row);
    }

    async getStatusHistory(
        userId: string,
        limit: number = 50
    ): Promise<StatusMonitoring[]> {
        const rows = await this.db.all<any>(
            `SELECT * FROM status_monitoring WHERE user_id = ? ORDER BY timestamp DESC LIMIT ?`,
            [userId, limit]
        );

        return rows.map(row => this.mapRowToStatus(row));
    }

    async logError(
        userId: string | undefined,
        instanceId: string | undefined,
        severity: 'INFO' | 'WARNING' | 'ERROR' | 'CRITICAL',
        message: string,
        context?: Record<string, any>,
        stack?: string
    ): Promise<ErrorLog> {
        const errorId = `error_${uuidv4()}`;

        await this.db.run(
            `INSERT INTO error_logs (error_id, user_id, instance_id, severity, message, stack, context)
             VALUES (?, ?, ?, ?, ?, ?, ?)`,
            [
                errorId,
                userId,
                instanceId,
                severity,
                message,
                stack,
                context ? JSON.stringify(context) : null,
            ]
        );

        const errorLog: ErrorLog = {
            error_id: errorId,
            user_id: userId,
            instance_id: instanceId,
            severity,
            message,
            stack,
            context,
            timestamp: new Date(),
            acknowledged: false,
        };

        logger.error({ errorLog }, 'Error logged');

        // Broadcast critical errors
        if (severity === 'CRITICAL' || severity === 'ERROR') {
            this.broadcastError(errorLog);
        }

        return errorLog;
    }

    async getErrors(
        userId?: string,
        severity?: 'INFO' | 'WARNING' | 'ERROR' | 'CRITICAL',
        limit: number = 100
    ): Promise<ErrorLog[]> {
        let query = `SELECT * FROM error_logs WHERE 1=1`;
        const params: any[] = [];

        if (userId) {
            query += ` AND user_id = ?`;
            params.push(userId);
        }

        if (severity) {
            query += ` AND severity = ?`;
            params.push(severity);
        }

        query += ` ORDER BY timestamp DESC LIMIT ?`;
        params.push(limit);

        const rows = await this.db.all<any>(query, params);

        return rows.map(row => ({
            error_id: row.error_id,
            user_id: row.user_id,
            instance_id: row.instance_id,
            severity: row.severity,
            message: row.message,
            stack: row.stack,
            context: row.context ? JSON.parse(row.context) : undefined,
            timestamp: new Date(row.timestamp),
            acknowledged: row.acknowledged === 1,
        }));
    }

    async acknowledgeError(errorId: string): Promise<void> {
        await this.db.run(
            `UPDATE error_logs SET acknowledged = 1 WHERE error_id = ?`,
            [errorId]
        );

        logger.info({ errorId }, 'Error acknowledged');
    }

    async clearOldLogs(daysToKeep: number = 30): Promise<void> {
        const cutoffDate = new Date();
        cutoffDate.setDate(cutoffDate.getDate() - daysToKeep);

        await this.db.run(
            `DELETE FROM status_monitoring WHERE timestamp < ?`,
            [cutoffDate.toISOString()]
        );

        await this.db.run(
            `DELETE FROM error_logs WHERE timestamp < ? AND acknowledged = 1`,
            [cutoffDate.toISOString()]
        );

        logger.info({ daysToKeep }, 'Old logs cleared');
    }

    private broadcastStatusUpdate(userId: string, status: StatusMonitoring): void {
        // Broadcast to all user's clients
        logger.debug({ userId, statusId: status.status_id }, 'Broadcasting status update');
    }

    private broadcastError(errorLog: ErrorLog): void {
        if (errorLog.user_id) {
            logger.debug({ userId: errorLog.user_id, errorId: errorLog.error_id }, 'Broadcasting error');
        }
    }

    private mapRowToStatus(row: any): StatusMonitoring {
        return {
            status_id: row.status_id,
            user_id: row.user_id,
            instance_id: row.instance_id,
            application_status: row.application_status,
            application_version: row.application_version,
            uptime: row.uptime,
            memory_usage: row.memory_usage,
            cpu_usage: row.cpu_usage,
            osc_status: row.osc_status,
            osc_latency: row.osc_latency,
            vrchat_status: row.vrchat_status,
            vrchat_world_id: row.vrchat_world_id,
            vrchat_instance_id: row.vrchat_instance_id,
            timestamp: new Date(row.timestamp),
        };
    }
}

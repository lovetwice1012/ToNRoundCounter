/**
 * Analytics Service
 * Handles advanced statistics and analytics
 */

import { getDatabase } from '../database/connection';
import { logger } from '../logger';

export class AnalyticsService {
    private db = getDatabase();

    async getPlayerStatistics(playerId: string, timeRange?: { start: Date; end: Date }): Promise<any> {
        let query = `
            SELECT 
                COUNT(*) as total_rounds,
                SUM(CASE WHEN r.status = 'COMPLETED' THEN 1 ELSE 0 END) as completed_rounds,
                AVG(TIMESTAMPDIFF(MINUTE, r.start_time, r.end_time)) as avg_duration_minutes,
                AVG(r.survivor_count * 1.0 / r.initial_player_count) as avg_survival_rate
            FROM rounds r
            JOIN instance_members im ON r.instance_id = im.instance_id
            WHERE im.player_id = ? AND im.status = 'ACTIVE'
        `;

        const params: any[] = [playerId];

        if (timeRange) {
            query += ` AND r.start_time BETWEEN ? AND ?`;
            params.push(timeRange.start.toISOString(), timeRange.end.toISOString());
        }

        const stats = await this.db.get<any>(query, params);

        return {
            player_id: playerId,
            total_rounds: stats?.total_rounds || 0,
            completed_rounds: stats?.completed_rounds || 0,
            avg_duration_minutes: stats?.avg_duration_minutes || 0,
            avg_survival_rate: stats?.avg_survival_rate || 0,
            time_range: timeRange,
        };
    }

    async getTerrorStatistics(terrorName?: string, timeRange?: { start: Date; end: Date }): Promise<any[]> {
        let query = `
            SELECT 
                ta.terror_name,
                COUNT(*) as appearance_count,
                COUNT(DISTINCT ta.round_id) as rounds_with_terror,
                AVG(JSON_LENGTH(ta.desire_players)) as avg_desire_count
            FROM terror_appearances ta
            JOIN rounds r ON ta.round_id = r.round_id
            WHERE 1=1
        `;

        const params: any[] = [];

        if (terrorName) {
            query += ` AND ta.terror_name = ?`;
            params.push(terrorName);
        }

        if (timeRange) {
            query += ` AND r.start_time BETWEEN ? AND ?`;
            params.push(timeRange.start.toISOString(), timeRange.end.toISOString());
        }

        query += ` GROUP BY ta.terror_name ORDER BY appearance_count DESC`;

        const rows = await this.db.all<any>(query, params);

        return rows.map(row => ({
            terror_name: row.terror_name,
            appearance_count: row.appearance_count,
            rounds_with_terror: row.rounds_with_terror,
            avg_desire_count: row.avg_desire_count,
            popularity_rank: rows.indexOf(row) + 1,
        }));
    }

    async getRoundTrends(
        groupBy: 'day' | 'week' | 'month',
        limit: number = 30
    ): Promise<any[]> {
        let dateFormat: string;
        switch (groupBy) {
            case 'day':
                dateFormat = '%Y-%m-%d';
                break;
            case 'week':
                dateFormat = '%Y-W%v'; // MariaDB uses %v for ISO week number
                break;
            case 'month':
                dateFormat = '%Y-%m';
                break;
        }

        const query = `
            SELECT 
                DATE_FORMAT(start_time, '${dateFormat}') as period,
                COUNT(*) as round_count,
                AVG(survivor_count * 1.0 / initial_player_count) as avg_survival_rate,
                AVG(TIMESTAMPDIFF(MINUTE, start_time, end_time)) as avg_duration_minutes
            FROM rounds
            WHERE status = 'COMPLETED' AND end_time IS NOT NULL
            GROUP BY period
            ORDER BY period DESC
            LIMIT ?
        `;

        const rows = await this.db.all<any>(query, [limit]);

        return rows.map(row => ({
            period: row.period,
            round_count: row.round_count,
            avg_survival_rate: row.avg_survival_rate,
            avg_duration_minutes: row.avg_duration_minutes,
        }));
    }

    async getInstanceStatistics(instanceId: string): Promise<any> {
        const query = `
            SELECT 
                COUNT(DISTINCT r.round_id) as total_rounds,
                COUNT(DISTINCT im.player_id) as unique_players,
                AVG(r.survivor_count * 1.0 / r.initial_player_count) as avg_survival_rate,
                MIN(r.start_time) as first_round,
                MAX(r.start_time) as last_round
            FROM rounds r
            LEFT JOIN instance_members im ON r.instance_id = im.instance_id
            WHERE r.instance_id = ?
        `;

        const stats = await this.db.get<any>(query, [instanceId]);

        return {
            instance_id: instanceId,
            total_rounds: stats?.total_rounds || 0,
            unique_players: stats?.unique_players || 0,
            avg_survival_rate: stats?.avg_survival_rate || 0,
            first_round: stats?.first_round ? new Date(stats.first_round) : null,
            last_round: stats?.last_round ? new Date(stats.last_round) : null,
        };
    }

    async getVotingStatistics(instanceId?: string): Promise<any> {
        let query = `
            SELECT 
                COUNT(*) as total_campaigns,
                COUNT(CASE WHEN status = 'RESOLVED' THEN 1 END) as resolved_campaigns,
                COUNT(CASE WHEN final_decision = 'Proceed' THEN 1 END) as proceed_count,
                COUNT(CASE WHEN final_decision = 'Cancel' THEN 1 END) as cancel_count,
                AVG(TIMESTAMPDIFF(SECOND, created_at, resolved_at)) as avg_voting_time_seconds
            FROM voting_campaigns
            WHERE 1=1
        `;

        const params: any[] = [];

        if (instanceId) {
            query += ` AND instance_id = ?`;
            params.push(instanceId);
        }

        const stats = await this.db.get<any>(query, params);

        return {
            instance_id: instanceId,
            total_campaigns: stats?.total_campaigns || 0,
            resolved_campaigns: stats?.resolved_campaigns || 0,
            proceed_count: stats?.proceed_count || 0,
            cancel_count: stats?.cancel_count || 0,
            avg_voting_time_seconds: stats?.avg_voting_time_seconds || 0,
            proceed_rate: stats?.resolved_campaigns > 0
                ? stats.proceed_count / stats.resolved_campaigns
                : 0,
        };
    }

    async generateCustomReport(
        reportConfig: {
            metrics: string[];
            groupBy?: string;
            filters?: Record<string, any>;
            timeRange?: { start: Date; end: Date };
        }
    ): Promise<any> {
        // This is a simplified custom report generator
        // In production, you'd have a more sophisticated query builder
        
        logger.info({ reportConfig }, 'Generating custom report');

        const results: any = {
            report_generated_at: new Date(),
            config: reportConfig,
            data: {},
        };

        // Add requested metrics
        if (reportConfig.metrics.includes('player_stats')) {
            results.data.player_stats = await this.getPlayerStatistics(
                reportConfig.filters?.player_id,
                reportConfig.timeRange
            );
        }

        if (reportConfig.metrics.includes('terror_stats')) {
            results.data.terror_stats = await this.getTerrorStatistics(
                reportConfig.filters?.terror_name,
                reportConfig.timeRange
            );
        }

        if (reportConfig.metrics.includes('trends')) {
            results.data.trends = await this.getRoundTrends(
                reportConfig.groupBy as any || 'day',
                30
            );
        }

        if (reportConfig.metrics.includes('voting_stats')) {
            results.data.voting_stats = await this.getVotingStatistics(
                reportConfig.filters?.instance_id
            );
        }

        return results;
    }

    async exportData(
        format: 'json' | 'csv',
        dataType: 'rounds' | 'players' | 'terrors',
        filters?: Record<string, any>
    ): Promise<string> {
        logger.info({ format, dataType, filters }, 'Exporting data');

        let data: any[] = [];

        switch (dataType) {
            case 'rounds':
                data = await this.exportRounds(filters);
                break;
            case 'players':
                data = await this.exportPlayers(filters);
                break;
            case 'terrors':
                data = await this.exportTerrors(filters);
                break;
        }

        if (format === 'json') {
            return JSON.stringify(data, null, 2);
        } else {
            return this.convertToCSV(data);
        }
    }

    private async exportRounds(filters?: Record<string, any>): Promise<any[]> {
        const query = `SELECT * FROM rounds ORDER BY start_time DESC LIMIT 1000`;
        return await this.db.all<any>(query);
    }

    private async exportPlayers(filters?: Record<string, any>): Promise<any[]> {
        const query = `SELECT * FROM player_profiles ORDER BY created_at DESC`;
        return await this.db.all<any>(query);
    }

    private async exportTerrors(filters?: Record<string, any>): Promise<any[]> {
        const query = `SELECT * FROM terror_appearances ORDER BY appearance_time DESC LIMIT 1000`;
        return await this.db.all<any>(query);
    }

    private convertToCSV(data: any[]): string {
        if (data.length === 0) {
            return '';
        }

        const headers = Object.keys(data[0]);
        const csv = [
            headers.join(','),
            ...data.map(row =>
                headers.map(header => {
                    const value = row[header];
                    if (typeof value === 'string' && value.includes(',')) {
                        return `"${value}"`;
                    }
                    return value;
                }).join(',')
            ),
        ].join('\n');

        return csv;
    }
}

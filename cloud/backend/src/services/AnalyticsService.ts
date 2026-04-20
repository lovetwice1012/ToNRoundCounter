/**
 * Analytics Service
 * Handles advanced statistics and analytics
 */

import { getDatabase } from '../database/connection';
import { logger } from '../logger';

export class AnalyticsService {
    private db = getDatabase();

    async getPlayerStatistics(playerId: string, timeRange?: { start: Date; end: Date }): Promise<any> {
        // Include rounds where the user is either an active instance member OR
        // the reporter (stored in metadata JSON by RoundService).  The LEFT JOIN
        // + OR approach avoids returning 0 when instance_members is empty but
        // the user has reported rounds directly.
        let query = `
            SELECT 
                COUNT(*) as total_rounds,
                SUM(CASE WHEN r.status = 'COMPLETED' THEN 1 ELSE 0 END) as completed_rounds,
                AVG(TIMESTAMPDIFF(MINUTE, r.start_time, r.end_time)) as avg_duration_minutes,
                AVG(CASE WHEN r.initial_player_count > 0
                         THEN r.survivor_count * 1.0 / r.initial_player_count
                         ELSE NULL END) as avg_survival_rate
            FROM rounds r
            LEFT JOIN instance_members im
                ON r.instance_id = im.instance_id AND im.player_id = ? AND im.status = 'ACTIVE'
            WHERE (
                im.player_id IS NOT NULL
                OR JSON_UNQUOTE(JSON_EXTRACT(r.metadata, '$.reporter_user_id')) = ?
            )
        `;

        const params: any[] = [playerId, playerId];

        if (timeRange) {
            query += ` AND r.start_time BETWEEN ? AND ?`;
            params.push(timeRange.start.toISOString(), timeRange.end.toISOString());
        }

        const stats = await this.db.get<any>(query, params);

        // Personal survival rate: how often this player personally survived
        // (round status = COMPLETED means reporter survived, FAILED means they died)
        let personalQuery = `
            SELECT
                COUNT(*) as total,
                SUM(CASE WHEN r.status = 'COMPLETED' THEN 1 ELSE 0 END) as survived
            FROM rounds r
            WHERE JSON_UNQUOTE(JSON_EXTRACT(r.metadata, '$.reporter_user_id')) = ?
        `;
        const personalParams: any[] = [playerId];
        if (timeRange) {
            personalQuery += ` AND r.start_time BETWEEN ? AND ?`;
            personalParams.push(timeRange.start.toISOString(), timeRange.end.toISOString());
        }
        const personal = await this.db.get<any>(personalQuery, personalParams);
        const personalTotal = personal?.total || 0;
        const personalSurvived = personal?.survived || 0;

        return {
            player_id: playerId,
            total_rounds: stats?.total_rounds || 0,
            completed_rounds: stats?.completed_rounds || 0,
            avg_duration_minutes: stats?.avg_duration_minutes || 0,
            avg_survival_rate: stats?.avg_survival_rate || 0,
            personal_survival_rate: personalTotal > 0 ? personalSurvived / personalTotal : 0,
            personal_total_rounds: personalTotal,
            personal_survived: personalSurvived,
            time_range: timeRange,
        };
    }

    async getTerrorStatistics(terrorName?: string, timeRange?: { start: Date; end: Date }, playerId?: string): Promise<any[]> {
        let query = `
            SELECT 
                ta.terror_name,
                COUNT(*) as appearance_count,
                COUNT(DISTINCT ta.round_id) as rounds_with_terror,
                AVG(JSON_LENGTH(ta.desire_players)) as avg_desire_count,
                AVG(rc.terror_count_in_round) as avg_terrors_in_round
            FROM terror_appearances ta
            JOIN rounds r ON ta.round_id = r.round_id
            LEFT JOIN (
                SELECT round_id, COUNT(*) as terror_count_in_round
                FROM terror_appearances
                GROUP BY round_id
            ) rc ON ta.round_id = rc.round_id
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

        // Get total round count for encounter rate calculation
        let totalRoundsQuery = `SELECT COUNT(*) as cnt FROM rounds WHERE 1=1`;
        const totalRoundsParams: any[] = [];
        if (timeRange) {
            totalRoundsQuery += ` AND start_time BETWEEN ? AND ?`;
            totalRoundsParams.push(timeRange.start.toISOString(), timeRange.end.toISOString());
        }
        const totalRoundsRow = await this.db.get<any>(totalRoundsQuery, totalRoundsParams);
        const totalRounds = totalRoundsRow?.cnt || 0;

        // 遭遇率（同ラウンド内）: ラウンド種類別に このterrorが出現した割合
        // Single bulk query: for each (terror_name, round_key) pair, count distinct rounds
        // and total rounds of that type. Then group per terror_name.
        let rtBreakdownParams: any[] = [];
        let rtWhereClause = '';
        if (timeRange) {
            rtWhereClause = ` AND r.start_time BETWEEN ? AND ?`;
            rtBreakdownParams.push(timeRange.start.toISOString(), timeRange.end.toISOString());
        }
        if (terrorName) {
            rtWhereClause += ` AND ta.terror_name = ?`;
            rtBreakdownParams.push(terrorName);
        }
        const rtBreakdownRows = await this.db.all<any>(`
            SELECT
                ta.terror_name,
                r.round_key,
                COUNT(DISTINCT ta.round_id) as rounds_with_terror_in_type,
                (SELECT COUNT(*) FROM rounds r2 WHERE r2.round_key = r.round_key) as total_rounds_of_type
            FROM terror_appearances ta
            JOIN rounds r ON ta.round_id = r.round_id
            WHERE 1=1 ${rtWhereClause}
            GROUP BY ta.terror_name, r.round_key
            ORDER BY ta.terror_name, rounds_with_terror_in_type DESC
        `, rtBreakdownParams);

        // Build a Map<terrorName, breakdown[]>
        const rtBreakdownMap = new Map<string, any[]>();
        for (const br of rtBreakdownRows) {
            if (!rtBreakdownMap.has(br.terror_name)) {
                rtBreakdownMap.set(br.terror_name, []);
            }
            rtBreakdownMap.get(br.terror_name)!.push({
                round_key: br.round_key,
                rounds_with_terror_in_type: br.rounds_with_terror_in_type || 0,
                total_rounds_of_type: br.total_rounds_of_type || 0,
                encounter_rate_in_type: (br.total_rounds_of_type || 0) > 0
                    ? (br.rounds_with_terror_in_type || 0) / (br.total_rounds_of_type || 0)
                    : 0,
            });
        }

        // 個人生存率（このterror出現時）: reporter_user_idがplayerIdのラウンドでの生死
        const personalSurvivalMap = new Map<string, { survived: number; total: number }>();
        if (playerId) {
            let personalParams: any[] = [playerId];
            let personalWhere = '';
            if (timeRange) {
                personalWhere = ` AND r.start_time BETWEEN ? AND ?`;
                personalParams.push(timeRange.start.toISOString(), timeRange.end.toISOString());
            }
            if (terrorName) {
                personalWhere += ` AND ta.terror_name = ?`;
                personalParams.push(terrorName);
            }
            const personalRows = await this.db.all<any>(`
                SELECT
                    ta.terror_name,
                    COUNT(*) as total,
                    SUM(CASE WHEN r.status = 'COMPLETED' THEN 1 ELSE 0 END) as survived
                FROM terror_appearances ta
                JOIN rounds r ON ta.round_id = r.round_id
                WHERE JSON_UNQUOTE(JSON_EXTRACT(r.metadata, '$.reporter_user_id')) = ?
                  ${personalWhere}
                GROUP BY ta.terror_name
            `, personalParams);
            for (const pr of personalRows) {
                personalSurvivalMap.set(pr.terror_name, {
                    survived: pr.survived || 0,
                    total: pr.total || 0,
                });
            }
        }

        return rows.map(row => {
            const appearanceCount = row.appearance_count || 0;
            const roundsWithTerror = row.rounds_with_terror || 0;
            const breakdown = rtBreakdownMap.get(row.terror_name) || [];
            const personal = personalSurvivalMap.get(row.terror_name);
            return {
                terror_name: row.terror_name,
                appearance_count: appearanceCount,
                rounds_with_terror: roundsWithTerror,
                avg_desire_count: row.avg_desire_count,
                popularity_rank: rows.indexOf(row) + 1,
                // 遭遇率（全体）: このterrorが出現したラウンド数 / 全ラウンド数
                encounter_rate: totalRounds > 0 ? roundsWithTerror / totalRounds : 0,
                // 遭遇率（同ラウンド内）: ラウンド種類別の遭遇率一覧
                round_type_encounter_rates: breakdown,
                // 同ラウンド内平均テラー数（後方互換）
                avg_terrors_per_round: row.avg_terrors_in_round || 0,
                // 個人生存率（このterror出現時）
                personal_survival_rate_with_terror: personal && personal.total > 0
                    ? personal.survived / personal.total
                    : null,
                personal_encounters: personal?.total ?? null,
                personal_survived_with_terror: personal?.survived ?? null,
            };
        });
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
            default:
                // Defensive: caller passed something not in the type — fall back to day.
                // This prevents an undefined dateFormat producing an invalid SQL fragment
                // and also blocks any attempt to influence the format string from input.
                dateFormat = '%Y-%m-%d';
                break;
        }

        // Clamp limit to a sane range (caller is untrusted).
        const safeLimit = Math.min(Math.max(Number.isFinite(limit) ? Math.trunc(limit) : 30, 1), 365);

        const query = `
            SELECT 
                DATE_FORMAT(start_time, '${dateFormat}') as period,
                COUNT(*) as round_count,
                AVG(CASE WHEN initial_player_count > 0
                         THEN survivor_count * 1.0 / initial_player_count
                         ELSE NULL END) as avg_survival_rate,
                AVG(TIMESTAMPDIFF(MINUTE, start_time, end_time)) as avg_duration_minutes
            FROM rounds
            WHERE status = 'COMPLETED' AND end_time IS NOT NULL
            GROUP BY period
            ORDER BY period DESC
            LIMIT ?
        `;

        const rows = await this.db.all<any>(query, [safeLimit]);

        return rows.map(row => ({
            period: row.period,
            round_count: row.round_count,
            avg_survival_rate: row.avg_survival_rate,
            avg_duration_minutes: row.avg_duration_minutes,
        }));
    }

    async getRoundTypeStatistics(playerId: string, timeRange?: { start: Date; end: Date }): Promise<any[]> {
        let query = `
            SELECT
                r.round_key,
                COUNT(*) as round_count,
                SUM(CASE WHEN r.status = 'COMPLETED' THEN 1 ELSE 0 END) as survived_count,
                SUM(CASE WHEN r.status = 'FAILED' THEN 1 ELSE 0 END) as failed_count,
                AVG(TIMESTAMPDIFF(MINUTE, r.start_time, r.end_time)) as avg_duration_minutes,
                AVG(CASE WHEN r.initial_player_count > 0
                         THEN r.survivor_count * 1.0 / r.initial_player_count
                         ELSE NULL END) as avg_survival_rate
            FROM rounds r
            WHERE JSON_UNQUOTE(JSON_EXTRACT(r.metadata, '$.reporter_user_id')) = ?
        `;
        const params: any[] = [playerId];
        if (timeRange) {
            query += ` AND r.start_time BETWEEN ? AND ?`;
            params.push(timeRange.start.toISOString(), timeRange.end.toISOString());
        }
        query += ` GROUP BY r.round_key ORDER BY round_count DESC`;

        const rows = await this.db.all<any>(query, params);
        const total = rows.reduce((sum: number, r: any) => sum + (r.round_count || 0), 0);

        return rows.map(row => ({
            round_key: row.round_key,
            round_count: row.round_count || 0,
            survived_count: row.survived_count || 0,
            failed_count: row.failed_count || 0,
            personal_survival_rate: row.round_count > 0 ? (row.survived_count || 0) / row.round_count : 0,
            avg_duration_minutes: row.avg_duration_minutes || 0,
            avg_survival_rate: row.avg_survival_rate || 0,
            round_share: total > 0 ? (row.round_count || 0) / total : 0,
        }));
    }

    async getInstanceStatistics(instanceId: string): Promise<any> {
        const query = `
            SELECT 
                COUNT(DISTINCT r.round_id) as total_rounds,
                COUNT(DISTINCT im.player_id) as unique_players,
                AVG(CASE WHEN r.initial_player_count > 0
                         THEN r.survivor_count * 1.0 / r.initial_player_count
                         ELSE NULL END) as avg_survival_rate,
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
                COUNT(CASE WHEN final_decision IN ('Proceed', 'Continue') THEN 1 END) as proceed_count,
                COUNT(CASE WHEN final_decision IN ('Cancel', 'Skip') THEN 1 END) as cancel_count,
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
        filters?: Record<string, any>,
        ownerUserId?: string
    ): Promise<string> {
        logger.info({ format, dataType, filters, ownerUserId }, 'Exporting data');

        let data: any[] = [];

        switch (dataType) {
            case 'rounds':
                data = await this.exportRounds(filters, ownerUserId);
                break;
            case 'players':
                data = await this.exportPlayers(filters, ownerUserId);
                break;
            case 'terrors':
                data = await this.exportTerrors(filters, ownerUserId);
                break;
        }

        if (format === 'json') {
            return JSON.stringify(data, null, 2);
        } else {
            return this.convertToCSV(data);
        }
    }

    private buildFilterClause(
        allowed: Record<string, string>,
        filters: Record<string, any> | undefined
    ): { where: string; params: any[] } {
        if (!filters || typeof filters !== 'object') {
            return { where: '', params: [] };
        }
        const clauses: string[] = [];
        const params: any[] = [];
        for (const [key, column] of Object.entries(allowed)) {
            const value = filters[key];
            if (value === undefined || value === null) continue;
            if (key === 'start_time' || key === 'end_time') {
                clauses.push(`${column} ${key === 'start_time' ? '>=' : '<='} ?`);
                params.push(value instanceof Date ? value.toISOString() : value);
            } else {
                clauses.push(`${column} = ?`);
                params.push(value);
            }
        }
        return { where: clauses.length > 0 ? ` WHERE ${clauses.join(' AND ')}` : '', params };
    }

    private clampLimit(filters?: Record<string, any>, defaultLimit = 1000, maxLimit = 10000): number {
        const raw = filters?.limit;
        const n = Number.isFinite(Number(raw)) ? Math.trunc(Number(raw)) : defaultLimit;
        return Math.min(Math.max(n, 1), maxLimit);
    }

    private async exportRounds(filters?: Record<string, any>, ownerUserId?: string): Promise<any[]> {
        const { where, params } = this.buildFilterClause(
            { instance_id: 'instance_id', status: 'status', start_time: 'start_time', end_time: 'start_time' },
            filters
        );
        const limit = this.clampLimit(filters);
        // Restrict export to rounds the caller participated in (instance member).
        // Without this any authenticated user could dump every round in the
        // database via analytics.export.
        if (ownerUserId) {
            const ownerWhere = where
                ? `${where} AND r.instance_id IN (SELECT instance_id FROM instance_members WHERE player_id = ?)`
                : ` WHERE r.instance_id IN (SELECT instance_id FROM instance_members WHERE player_id = ?)`;
            const query = `SELECT r.* FROM rounds r${ownerWhere} ORDER BY r.start_time DESC LIMIT ?`;
            return await this.db.all<any>(query, [...params, ownerUserId, limit]);
        }
        const query = `SELECT * FROM rounds${where} ORDER BY start_time DESC LIMIT ?`;
        return await this.db.all<any>(query, [...params, limit]);
    }

    private async exportPlayers(filters?: Record<string, any>, ownerUserId?: string): Promise<any[]> {
        const { where, params } = this.buildFilterClause(
            { player_id: 'player_id', player_name: 'player_name' },
            filters
        );
        const limit = this.clampLimit(filters);
        // Restrict to the caller's own profile to prevent bulk PII scraping.
        if (ownerUserId) {
            const ownerWhere = where ? `${where} AND player_id = ?` : ` WHERE player_id = ?`;
            const query = `SELECT * FROM player_profiles${ownerWhere} ORDER BY created_at DESC LIMIT ?`;
            return await this.db.all<any>(query, [...params, ownerUserId, limit]);
        }
        const query = `SELECT * FROM player_profiles${where} ORDER BY created_at DESC LIMIT ?`;
        return await this.db.all<any>(query, [...params, limit]);
    }

    private async exportTerrors(filters?: Record<string, any>, ownerUserId?: string): Promise<any[]> {
        const { where, params } = this.buildFilterClause(
            { terror_name: 'terror_name', round_id: 'round_id' },
            filters
        );
        const limit = this.clampLimit(filters);
        // Restrict to terror appearances that occurred in the caller's rounds.
        if (ownerUserId) {
            const ownerWhere = where
                ? `${where} AND ta.round_id IN (SELECT r.round_id FROM rounds r JOIN instance_members im ON r.instance_id = im.instance_id WHERE im.player_id = ?)`
                : ` WHERE ta.round_id IN (SELECT r.round_id FROM rounds r JOIN instance_members im ON r.instance_id = im.instance_id WHERE im.player_id = ?)`;
            const query = `SELECT ta.* FROM terror_appearances ta${ownerWhere} ORDER BY ta.appearance_time DESC LIMIT ?`;
            return await this.db.all<any>(query, [...params, ownerUserId, limit]);
        }
        const query = `SELECT * FROM terror_appearances${where} ORDER BY appearance_time DESC LIMIT ?`;
        return await this.db.all<any>(query, [...params, limit]);
    }

    private convertToCSV(data: any[]): string {
        if (data.length === 0) {
            return '';
        }

        const headers = Object.keys(data[0]);
        const escape = (raw: any): string => {
            if (raw === null || raw === undefined) return '';
            let value: string;
            if (raw instanceof Date) {
                value = raw.toISOString();
            } else if (typeof raw === 'object') {
                // Stringify nested objects/arrays so the CSV cell carries useful info.
                try { value = JSON.stringify(raw); } catch { value = String(raw); }
            } else {
                value = String(raw);
            }
            // RFC 4180: wrap in quotes and double any embedded quote whenever the
            // value contains comma, quote, CR or LF. Previously commas were the
            // only trigger and embedded quotes were emitted unescaped, breaking
            // any reader that imported the CSV.
            if (/[",\r\n]/.test(value)) {
                return `"${value.replace(/"/g, '""')}"`;
            }
            return value;
        };
        const csv = [
            headers.join(','),
            ...data.map(row => headers.map(header => escape(row[header])).join(',')),
        ].join('\n');

        return csv;
    }
}

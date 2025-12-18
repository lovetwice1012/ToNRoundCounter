/**
 * Round Service
 * Handles round data reporting and storage
 */

import { getDatabase } from '../database/connection';
import { logger } from '../logger';

export class RoundService {
    private db = getDatabase();

    async reportRound(roundData: any): Promise<void> {
        const {
            instance_id,
            round_type,
            terror_name,
            terror_key,
            start_time,
            end_time,
            initial_player_count,
            survivor_count,
            status,
            player_id
        } = roundData;

        const result: any = await this.db.run(
            `INSERT INTO rounds (instance_id, round_type, terror_name, start_time, end_time, initial_player_count, survivor_count, status)
             VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
            [
                instance_id,
                round_type,
                terror_name || terror_key,
                start_time,
                end_time || new Date().toISOString(),
                initial_player_count || 0,
                survivor_count || 0,
                status || 'COMPLETED'
            ]
        );

        const roundId = result.lastID;

        // Record terror appearance if terror_name exists
        if (terror_name || terror_key) {
            await this.db.run(
                `INSERT INTO terror_appearances (round_id, terror_name, desire_players)
                 VALUES (?, ?, ?)`,
                [
                    roundId,
                    terror_name || terror_key,
                    JSON.stringify([]) // Empty for now, can be updated later
                ]
            );
        }

        logger.info({ roundId, instance_id, round_type }, 'Round reported');
    }

    async getRounds(instanceId?: string, limit: number = 100): Promise<any[]> {
        let query = `
            SELECT * FROM rounds
            WHERE 1=1
        `;

        const params: any[] = [];

        if (instanceId) {
            query += ` AND instance_id = ?`;
            params.push(instanceId);
        }

        query += ` ORDER BY start_time DESC LIMIT ?`;
        params.push(limit);

        return await this.db.all<any>(query, params);
    }
}

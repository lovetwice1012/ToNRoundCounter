/**
 * Round Repository
 * Handles all round-related database operations
 */

import { v4 as uuidv4 } from 'uuid';
import { getDatabase } from '../database/connection';
import { Round, RoundEvent, TerrorAppearance } from '../models/types';
import { logger } from '../logger';

export class RoundRepository {
    private db = getDatabase();

    async createRound(
        instanceId: string,
        roundKey: string,
        initialPlayerCount: number
    ): Promise<Round> {
        const roundId = `round_${uuidv4()}`;
        const startTime = new Date();

        await this.db.run(
            `INSERT INTO rounds (round_id, instance_id, round_key, start_time, initial_player_count, survivor_count)
             VALUES (?, ?, ?, ?, ?, ?)`,
            [roundId, instanceId, roundKey, startTime.toISOString(), initialPlayerCount, initialPlayerCount]
        );

        logger.info({ roundId, instanceId, roundKey }, 'Round created');

        return {
            round_id: roundId,
            instance_id: instanceId,
            round_key: roundKey,
            start_time: startTime,
            status: 'ACTIVE',
            survivor_count: initialPlayerCount,
            initial_player_count: initialPlayerCount,
            events: [],
            created_at: new Date(),
        };
    }

    async getRound(roundId: string): Promise<Round | undefined> {
        const row = await this.db.get<any>(
            `SELECT * FROM rounds WHERE round_id = ?`,
            [roundId]
        );

        if (!row) {
            return undefined;
        }

        return this.mapRowToRound(row);
    }

    async updateRoundStatus(roundId: string, status: 'ACTIVE' | 'COMPLETED' | 'CANCELLED'): Promise<void> {
        await this.db.run(
            `UPDATE rounds SET status = ? WHERE round_id = ?`,
            [status, roundId]
        );
    }

    async updateSurvivorCount(roundId: string, count: number): Promise<void> {
        await this.db.run(
            `UPDATE rounds SET survivor_count = ? WHERE round_id = ?`,
            [count, roundId]
        );
    }

    async endRound(roundId: string): Promise<void> {
        await this.db.run(
            `UPDATE rounds SET status = 'COMPLETED', end_time = NOW() WHERE round_id = ?`,
            [roundId]
        );

        logger.info({ roundId }, 'Round ended');
    }

    async addRoundEvent(roundId: string, event: RoundEvent): Promise<void> {
        const round = await this.getRound(roundId);
        if (!round) {
            throw new Error('Round not found');
        }

        round.events.push(event);

        await this.db.run(
            `UPDATE rounds SET events = ? WHERE round_id = ?`,
            [JSON.stringify(round.events), roundId]
        );
    }

    async addTerrorAppearance(
        roundId: string,
        terrorName: string,
        desirePlayers: any[]
    ): Promise<TerrorAppearance> {
        const result = await this.db.execute(
            `INSERT INTO terror_appearances (round_id, terror_name, appearance_time, desire_players)
             VALUES (?, ?, NOW(), ?)`,
            [roundId, terrorName, JSON.stringify(desirePlayers)]
        );

        const insertId = result.insertId;

        logger.info({ roundId, terrorName, desirePlayersCount: desirePlayers.length }, 'Terror appearance recorded');

        return {
            id: insertId,
            round_id: roundId,
            terror_name: terrorName,
            appearance_time: new Date(),
            desire_players: desirePlayers,
            responses: [],
            created_at: new Date(),
        };
    }

    async getRoundsByInstance(instanceId: string, limit: number = 50): Promise<Round[]> {
        const rows = await this.db.all<any>(
            `SELECT * FROM rounds WHERE instance_id = ? ORDER BY created_at DESC LIMIT ?`,
            [instanceId, limit]
        );

        return rows.map(row => this.mapRowToRound(row));
    }

    private mapRowToRound(row: any): Round {
        return {
            round_id: row.round_id,
            instance_id: row.instance_id,
            round_key: row.round_key,
            start_time: new Date(row.start_time),
            end_time: row.end_time ? new Date(row.end_time) : undefined,
            status: row.status,
            survivor_count: row.survivor_count,
            initial_player_count: row.initial_player_count,
            events: JSON.parse(row.events),
            metadata: row.metadata ? JSON.parse(row.metadata) : undefined,
            created_at: new Date(row.created_at),
        };
    }
}

export default new RoundRepository();

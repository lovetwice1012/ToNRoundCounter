/**
 * Player State Service
 * Handles player state updates
 */

import { getDatabase } from '../database/connection';
import { PlayerState } from '../models/types';
import { logger } from '../logger';

export class PlayerStateService {
    private db = getDatabase();
    private wsHandler: any;

    constructor(wsHandler: any) {
        this.wsHandler = wsHandler;
    }

    async updatePlayerState(instanceId: string, playerState: any): Promise<void> {
        const {
            player_id,
            velocity = 0,
            afk_duration = 0,
            items = [],
            damage = 0,
            is_alive = true,
        } = playerState;

        await this.db.run(
            `INSERT INTO player_states (instance_id, player_id, velocity, afk_duration, items, damage, is_alive)
             VALUES (?, ?, ?, ?, ?, ?, ?)`,
            [
                instanceId,
                player_id,
                velocity,
                afk_duration,
                JSON.stringify(items),
                damage,
                is_alive ? 1 : 0,
            ]
        );

        logger.debug({ instanceId, player_id }, 'Player state updated');
    }

    async getPlayerState(instanceId: string, playerId: string): Promise<PlayerState | undefined> {
        const row = await this.db.get<any>(
            `SELECT * FROM player_states 
             WHERE instance_id = ? AND player_id = ?
             ORDER BY timestamp DESC LIMIT 1`,
            [instanceId, playerId]
        );

        if (!row) {
            return undefined;
        }

        return {
            id: row.id,
            instance_id: row.instance_id,
            player_id: row.player_id,
            velocity: row.velocity,
            afk_duration: row.afk_duration,
            items: JSON.parse(row.items),
            damage: row.damage,
            is_alive: row.is_alive === 1,
            timestamp: new Date(row.timestamp),
        };
    }

    async getAllPlayerStates(instanceId: string): Promise<PlayerState[]> {
        const rows = await this.db.all<any>(
            `SELECT ps1.* FROM player_states ps1
             INNER JOIN (
                 SELECT player_id, MAX(timestamp) as max_timestamp
                 FROM player_states
                 WHERE instance_id = ?
                 GROUP BY player_id
             ) ps2 ON ps1.player_id = ps2.player_id AND ps1.timestamp = ps2.max_timestamp
             WHERE ps1.instance_id = ?`,
            [instanceId, instanceId]
        );

        return rows.map(row => ({
            id: row.id,
            instance_id: row.instance_id,
            player_id: row.player_id,
            velocity: row.velocity,
            afk_duration: row.afk_duration,
            items: JSON.parse(row.items),
            damage: row.damage,
            is_alive: row.is_alive === 1,
            timestamp: new Date(row.timestamp),
        }));
    }
}

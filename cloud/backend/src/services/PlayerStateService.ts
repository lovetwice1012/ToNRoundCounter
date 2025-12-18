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
    private lastStates: Map<string, string> = new Map(); // key: `${instanceId}:${playerId}`, value: JSON state

    constructor(wsHandler: any) {
        this.wsHandler = wsHandler;
    }

    async updatePlayerState(instanceId: string, playerState: any): Promise<boolean> {
        const {
            player_id,
            player_name = player_id, // デフォルトでplayer_idを使用
            velocity = 0,
            afk_duration = 0,
            items = [],
            damage = 0,
            is_alive = true,
        } = playerState;

        // Don't sort items - order matters (last item is the current one)
        const itemsArray = Array.isArray(items) ? items : [];

        // Check if state actually changed
        const stateKey = `${instanceId}:${player_id}`;
        const currentStateJson = JSON.stringify({ player_name, velocity, afk_duration, items: itemsArray, damage, is_alive });
        const lastStateJson = this.lastStates.get(stateKey);
        
        if (lastStateJson === currentStateJson) {
            // State hasn't changed, don't broadcast
            logger.debug({ player_id, instanceId }, 'Player state unchanged, skipping broadcast');
            return false;
        }

        // Log what changed
        if (lastStateJson) {
            const lastState = JSON.parse(lastStateJson);
            const currentState = JSON.parse(currentStateJson);
            const changes: any = {};
            
            if (JSON.stringify(lastState.items) !== JSON.stringify(currentState.items)) {
                changes.items = { from: lastState.items, to: currentState.items };
            }
            if (lastState.velocity !== currentState.velocity) {
                changes.velocity = { from: lastState.velocity, to: currentState.velocity };
            }
            if (lastState.afk_duration !== currentState.afk_duration) {
                changes.afk_duration = { from: lastState.afk_duration, to: currentState.afk_duration };
            }
            if (lastState.damage !== currentState.damage) {
                changes.damage = { from: lastState.damage, to: currentState.damage };
            }
            if (lastState.is_alive !== currentState.is_alive) {
                changes.is_alive = { from: lastState.is_alive, to: currentState.is_alive };
            }
            
            logger.info({ player_id, instanceId, changes }, 'Player state changed');
        }

        // Update cache
        this.lastStates.set(stateKey, currentStateJson);

        // Use ON DUPLICATE KEY UPDATE for MySQL (don't sort items, order matters)
        await this.db.run(
            `INSERT INTO player_states (instance_id, player_id, player_name, velocity, afk_duration, items, damage, is_alive, timestamp)
             VALUES (?, ?, ?, ?, ?, ?, ?, ?, NOW())
             ON DUPLICATE KEY UPDATE
                player_name = VALUES(player_name),
                velocity = VALUES(velocity),
                afk_duration = VALUES(afk_duration),
                items = VALUES(items),
                damage = VALUES(damage),
                is_alive = VALUES(is_alive),
                timestamp = NOW()`,
            [
                instanceId,
                player_id,
                player_name,
                velocity,
                afk_duration,
                JSON.stringify(itemsArray),
                damage,
                is_alive ? 1 : 0,
            ]
        );

        logger.debug({ instanceId, player_id, player_name, items: itemsArray }, 'Player state updated');
        return true; // State changed
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
            player_name: row.player_name || row.player_id,
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
            player_name: row.player_name || row.player_id,
            velocity: row.velocity,
            afk_duration: row.afk_duration,
            items: JSON.parse(row.items),
            damage: row.damage,
            is_alive: row.is_alive === 1,
            timestamp: new Date(row.timestamp),
        }));
    }
}

/**
 * Wished Terror Service
 * Handles wished terror management
 */

import { v4 as uuidv4 } from 'uuid';
import { getDatabase } from '../database/connection';
import { WishedTerror } from '../models/types';
import { logger } from '../logger';

export class WishedTerrorService {
    private db = getDatabase();
    private wsHandler: any;

    constructor(wsHandler: any) {
        this.wsHandler = wsHandler;
    }

    async updateWishedTerrors(playerId: string, wishedTerrors: any[]): Promise<void> {
        // Delete existing wished terrors for this player
        await this.db.run(
            `DELETE FROM wished_terrors WHERE player_id = ?`,
            [playerId]
        );

        // Insert new wished terrors
        for (const wished of wishedTerrors) {
            const id = wished.id || uuidv4();
            await this.db.run(
                `INSERT INTO wished_terrors (id, player_id, terror_name, round_key)
                 VALUES (?, ?, ?, ?)`,
                [id, playerId, wished.terror_name, wished.round_key || '']
            );
        }

        logger.info({ playerId, count: wishedTerrors.length }, 'Wished terrors updated');
    }

    async getWishedTerrors(playerId: string): Promise<WishedTerror[]> {
        const rows = await this.db.all<any>(
            `SELECT * FROM wished_terrors WHERE player_id = ? ORDER BY created_at DESC`,
            [playerId]
        );

        return rows.map(row => ({
            id: row.id,
            player_id: row.player_id,
            terror_name: row.terror_name,
            round_key: row.round_key,
            created_at: new Date(row.created_at),
        }));
    }

    async findDesirePlayersForTerror(
        instanceId: string,
        terrorName: string,
        roundKey: string
    ): Promise<any[]> {
        logger.info({ instanceId, terrorName, roundKey }, 'Finding desire players for terror');
        
        // Get all members of the instance
        const members = await this.db.all<any>(
            `SELECT player_id, player_name FROM instance_members
             WHERE instance_id = ? AND status = 'ACTIVE'`,
            [instanceId]
        );

        logger.info({ instanceId, memberCount: members.length, memberIds: members.map(m => m.player_id) }, 'Found instance members');

        const desirePlayers: any[] = [];

        for (const member of members) {
            // Check if player has wished for this terror
            const wishedTerrors = await this.getWishedTerrors(member.player_id);

            logger.debug({ 
                playerId: member.player_id, 
                wishedCount: wishedTerrors.length,
                wishedTerrors: wishedTerrors.map(w => ({ terror: w.terror_name, round: w.round_key }))
            }, 'Player wished terrors');

            const matched = wishedTerrors.some(wished => {
                // Terror name must match
                if (wished.terror_name !== terrorName) {
                    return false;
                }

                // If round_key is empty, it matches all rounds
                if (!wished.round_key || wished.round_key === '') {
                    return true;
                }

                // Otherwise, round_key must match exactly
                return wished.round_key === roundKey;
            });

            if (matched) {
                logger.info({ playerId: member.player_id, terrorName, roundKey }, 'Player matched as desire player');
                desirePlayers.push({
                    player_id: member.player_id,
                    player_name: member.player_name,
                });
            }
        }

        logger.info({ instanceId, terrorName, roundKey, desireCount: desirePlayers.length, desireIds: desirePlayers.map(p => p.player_id) }, 'Desire players result');
        
        return desirePlayers;
    }
}

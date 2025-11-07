/**
 * Threat Service
 * Handles threat/terror-related operations
 */

import { logger } from '../logger';
import { getDatabase } from '../database/connection';
import { RoundRepository } from '../repositories/RoundRepository';

export interface ThreatAnnouncement {
    terror_name: string;
    round_key: string;
    instance_id: string;
    round_id?: string;
    desire_players: Array<{
        player_id: string;
        player_name: string;
    }>;
}

export class ThreatService {
    private wsHandler: any;
    private db = getDatabase();
    private roundRepository = new RoundRepository();

    constructor(wsHandler: any) {
        this.wsHandler = wsHandler;
    }

    /**
     * Announce a threat/terror to an instance
     */
    async announceThreat(announcement: ThreatAnnouncement): Promise<{ threat_id?: number }> {
        logger.info(
            {
                instanceId: announcement.instance_id,
                terrorName: announcement.terror_name,
                roundKey: announcement.round_key,
            },
            'Broadcasting threat announcement'
        );

        let threatId: number | undefined;
        const desirePlayers = announcement.desire_players || [];
        const resolvedRoundId = await this.resolveRoundId(announcement.instance_id, announcement.round_id);

        if (resolvedRoundId) {
            const record = await this.roundRepository.addTerrorAppearance(
                resolvedRoundId,
                announcement.terror_name,
                desirePlayers
            );
            threatId = record.id;
        }

        // Broadcast to all clients in the instance
        this.wsHandler.broadcastToInstance(announcement.instance_id, {
            stream: 'threat.announced',
            data: {
                ...announcement,
                threat_id: threatId,
            },
            timestamp: new Date().toISOString(),
        });

        return { threat_id: threatId };
    }

    /**
     * Handle threat response from a player
     */
    async recordThreatResponse(
        threatId: string,
        playerId: string,
        decision: 'survive' | 'cancel' | 'skip' | 'execute' | 'timeout'
    ): Promise<void> {
        logger.info(
            { threatId, playerId, decision },
            'Threat response recorded'
        );

        const row = await this.db.get<any>(
            `SELECT ta.responses, r.instance_id 
             FROM terror_appearances ta
             JOIN rounds r ON ta.round_id = r.round_id
             WHERE ta.id = ?`,
            [threatId]
        );

        if (!row) {
            throw new Error('Threat not found');
        }

        const responses = row.responses ? JSON.parse(row.responses) : [];
        const timestamp = new Date().toISOString();
        responses.push({
            player_id: playerId,
            decision,
            timestamp,
        });

        await this.db.run(
            `UPDATE terror_appearances SET responses = ? WHERE id = ?`,
            [JSON.stringify(responses), threatId]
        );

        if (row.instance_id) {
            this.wsHandler.broadcastToInstance(row.instance_id, {
                stream: 'threat.response.recorded',
                data: {
                    threat_id: threatId,
                    player_id: playerId,
                    decision,
                    timestamp,
                },
                timestamp,
            });
        }
    }

    private async resolveRoundId(instanceId: string, provided?: string): Promise<string | null> {
        if (provided) {
            return provided;
        }

        const latestRoundId = await this.roundRepository.getLatestRoundId(instanceId);
        return latestRoundId || null;
    }
}

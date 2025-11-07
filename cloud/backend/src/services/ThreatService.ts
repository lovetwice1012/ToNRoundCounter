/**
 * Threat Service
 * Handles threat/terror-related operations
 */

import { logger } from '../logger';

export interface ThreatAnnouncement {
    terror_name: string;
    round_key: string;
    instance_id: string;
    desire_players: Array<{
        player_id: string;
        player_name: string;
    }>;
}

export class ThreatService {
    private wsHandler: any;

    constructor(wsHandler: any) {
        this.wsHandler = wsHandler;
    }

    /**
     * Announce a threat/terror to an instance
     */
    async announceThreat(announcement: ThreatAnnouncement): Promise<void> {
        logger.info(
            {
                instanceId: announcement.instance_id,
                terrorName: announcement.terror_name,
                roundKey: announcement.round_key,
            },
            'Broadcasting threat announcement'
        );

        // Broadcast to all clients in the instance
        this.wsHandler.broadcastToInstance(announcement.instance_id, {
            stream: 'threat.announced',
            data: announcement,
            timestamp: new Date().toISOString(),
        });
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

        // Store in database (implementation depends on schema)
        // For now, just log it
    }
}

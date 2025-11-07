/**
 * Voting Service
 * Handles coordinated voting for auto-suicide
 */

import { v4 as uuidv4 } from 'uuid';
import { getDatabase } from '../database/connection';
import { VotingCampaign, PlayerVote } from '../models/types';
import { logger } from '../logger';

export class VotingService {
    private db = getDatabase();
    private wsHandler: any;
    private timeouts: Map<string, NodeJS.Timeout> = new Map();

    constructor(wsHandler: any) {
        this.wsHandler = wsHandler;
    }

    async startVoting(
        campaignId: string,
        instanceId: string,
        terrorName: string,
        expiresAt: Date
    ): Promise<VotingCampaign> {
        // Get current round key from the most recent active round in this instance
        const roundRow = await this.db.get<any>(
            `SELECT round_key FROM rounds WHERE instance_id = ? AND status = 'ACTIVE' ORDER BY created_at DESC LIMIT 1`,
            [instanceId]
        );
        const roundKey = roundRow?.round_key || '';

        await this.db.run(
            `INSERT INTO voting_campaigns (campaign_id, instance_id, terror_name, round_key, expires_at)
             VALUES (?, ?, ?, ?, ?)`,
            [campaignId, instanceId, terrorName, roundKey, expiresAt.toISOString()]
        );

        // Set timeout for voting expiration
        const timeout = setTimeout(async () => {
            await this.expireVoting(campaignId);
        }, expiresAt.getTime() - Date.now());

        this.timeouts.set(campaignId, timeout);

        logger.info({ campaignId, instanceId, terrorName }, 'Voting campaign started');

        return {
            campaign_id: campaignId,
            instance_id: instanceId,
            terror_name: terrorName,
            round_key: roundKey,
            status: 'PENDING',
            created_at: new Date(),
            expires_at: expiresAt,
        };
    }

    async submitVote(campaignId: string, playerId: string, decision: 'Proceed' | 'Cancel'): Promise<void> {
        await this.db.run(
            `INSERT INTO player_votes (campaign_id, player_id, decision)
             VALUES (?, ?, ?)
             ON DUPLICATE KEY UPDATE decision = VALUES(decision), voted_at = CURRENT_TIMESTAMP`,
            [campaignId, playerId, decision]
        );

        logger.info({ campaignId, playerId, decision }, 'Vote submitted');
    }

    async getCampaign(campaignId: string): Promise<VotingCampaign | undefined> {
        const row = await this.db.get<any>(
            `SELECT * FROM voting_campaigns WHERE campaign_id = ?`,
            [campaignId]
        );

        if (!row) {
            return undefined;
        }

        return {
            campaign_id: row.campaign_id,
            instance_id: row.instance_id,
            terror_name: row.terror_name,
            round_key: row.round_key,
            final_decision: row.final_decision,
            status: row.status,
            created_at: new Date(row.created_at),
            expires_at: new Date(row.expires_at),
            resolved_at: row.resolved_at ? new Date(row.resolved_at) : undefined,
        };
    }

    async getVotes(campaignId: string): Promise<PlayerVote[]> {
        const rows = await this.db.all<any>(
            `SELECT * FROM player_votes WHERE campaign_id = ? ORDER BY voted_at ASC`,
            [campaignId]
        );

        return rows.map(row => ({
            id: row.id,
            campaign_id: row.campaign_id,
            player_id: row.player_id,
            decision: row.decision,
            voted_at: new Date(row.voted_at),
        }));
    }

    async isVotingComplete(campaignId: string): Promise<boolean> {
        const campaign = await this.getCampaign(campaignId);
        if (!campaign) {
            return false;
        }

        // Get total members in instance
        const memberCount = await this.db.get<any>(
            `SELECT COUNT(*) as count FROM instance_members
             WHERE instance_id = ? AND status = 'ACTIVE'`,
            [campaign.instance_id]
        );

        // Get vote count
        const voteCount = await this.db.get<any>(
            `SELECT COUNT(*) as count FROM player_votes WHERE campaign_id = ?`,
            [campaignId]
        );

        return voteCount.count >= memberCount.count;
    }

    async resolveVoting(campaignId: string): Promise<any> {
        const votes = await this.getVotes(campaignId);
        const campaign = await this.getCampaign(campaignId);

        if (!campaign) {
            throw new Error('Campaign not found');
        }

        // Count votes
        const proceedCount = votes.filter(v => v.decision === 'Proceed').length;
        const cancelCount = votes.filter(v => v.decision === 'Cancel').length;
        const totalVotes = votes.length;

        // Majority wins (> 50%)
        const finalDecision = proceedCount > totalVotes / 2 ? 'Proceed' : 'Cancel';

        // Update campaign
        await this.db.run(
            `UPDATE voting_campaigns 
             SET status = 'RESOLVED', final_decision = ?, resolved_at = NOW()
             WHERE campaign_id = ?`,
            [finalDecision, campaignId]
        );

        // Clear timeout
        const timeout = this.timeouts.get(campaignId);
        if (timeout) {
            clearTimeout(timeout);
            this.timeouts.delete(campaignId);
        }

        logger.info({ campaignId, finalDecision, proceedCount, cancelCount }, 'Voting resolved');

        return {
            campaign_id: campaignId,
            final_decision: finalDecision,
            votes: votes.map(v => ({
                player_id: v.player_id,
                decision: v.decision,
            })),
            vote_count: {
                proceed: proceedCount,
                cancel: cancelCount,
            },
        };
    }

    private async expireVoting(campaignId: string): Promise<void> {
        const campaign = await this.getCampaign(campaignId);

        if (!campaign || campaign.status !== 'PENDING') {
            return;
        }

        // Get all instance members
        const members = await this.db.all<any>(
            `SELECT player_id FROM instance_members
             WHERE instance_id = ? AND status = 'ACTIVE'`,
            [campaign.instance_id]
        );

        // Add Cancel votes for members who haven't voted
        const votes = await this.getVotes(campaignId);
        const votedPlayerIds = new Set(votes.map(v => v.player_id));

        for (const member of members) {
            if (!votedPlayerIds.has(member.player_id)) {
                await this.submitVote(campaignId, member.player_id, 'Cancel');
            }
        }

        // Resolve voting
        const result = await this.resolveVoting(campaignId);

        // Broadcast result
        this.wsHandler.broadcastToInstance(campaign.instance_id, {
            stream: 'coordinated.voting.resolved',
            data: result,
            timestamp: new Date().toISOString(),
        });

        logger.info({ campaignId }, 'Voting expired and resolved');
    }
}

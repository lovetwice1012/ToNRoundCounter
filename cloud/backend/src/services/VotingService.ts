/**
 * Voting Service
 * Handles coordinated voting for auto-suicide
 */

import { v4 as uuidv4 } from 'uuid';
import { getDatabase } from '../database/connection';
import { PlayerVote, VoteDecision, VotingCampaign } from '../models/types';
import { logger } from '../logger';
import { CoordinatedAutoSuicideService } from './CoordinatedAutoSuicideService';

/**
 * Convert Date to MySQL/MariaDB DATETIME format (YYYY-MM-DD HH:MM:SS).
 * MySQL TIMESTAMP/DATETIME columns reject ISO 8601 strings that include the
 * 'T' separator, fractional seconds, and 'Z' suffix produced by toISOString().
 */
function toMySQLDatetime(date: Date): string {
    return date.toISOString().slice(0, 19).replace('T', ' ');
}

export function normalizeVoteDecision(decision: unknown): VoteDecision {
    const normalized = String(decision ?? '').trim().toLocaleLowerCase();
    if (normalized === 'continue' || normalized === 'proceed') {
        return 'Continue';
    }
    if (normalized === 'skip' || normalized === 'cancel') {
        return 'Skip';
    }

    throw new Error('decision must be Continue or Skip');
}

export class VotingService {
    private db = getDatabase();
    private wsHandler: any;
    private timeouts: Map<string, NodeJS.Timeout> = new Map();
    private coordinatedAutoSuicideService = new CoordinatedAutoSuicideService();

    constructor(wsHandler: any) {
        this.wsHandler = wsHandler;
    }

    async startVoting(
        campaignId: string,
        instanceId: string,
        terrorName: string,
        expiresAt: Date,
        requestedRoundKey?: string,
    ): Promise<VotingCampaign> {
        const roundKey = String(requestedRoundKey ?? '').trim() || await this.getCurrentRoundKey(instanceId);

        await this.db.run(
            `INSERT INTO voting_campaigns (campaign_id, instance_id, terror_name, round_key, expires_at)
             VALUES (?, ?, ?, ?, ?)`,
            [campaignId, instanceId, terrorName, roundKey, toMySQLDatetime(expiresAt)]
        );

        // Set timeout for voting expiration
        const delayMs = Math.max(0, expiresAt.getTime() - Date.now());
        const timeout = setTimeout(async () => {
            await this.expireVoting(campaignId);
        }, delayMs);

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

    private async getCurrentRoundKey(instanceId: string): Promise<string> {
        const roundRow = await this.db.get<any>(
            `SELECT round_key FROM rounds WHERE instance_id = ? AND status = 'ACTIVE' ORDER BY created_at DESC LIMIT 1`,
            [instanceId]
        );

        return roundRow?.round_key || '';
    }

    async submitVote(campaignId: string, playerId: string, decision: unknown): Promise<void> {
        // Reject votes for campaigns that no longer accept input. Without this
        // check, late votes (after expireVoting/resolveVoting fired) would still
        // be persisted and skew vote counts shown in subsequent UI fetches.
        const campaign = await this.getCampaign(campaignId);
        if (!campaign) {
            throw new Error('Campaign not found');
        }
        if (campaign.status !== 'PENDING') {
            throw new Error(`Voting is already ${campaign.status.toLowerCase()}`);
        }
        if (campaign.expires_at.getTime() <= Date.now()) {
            throw new Error('Voting has expired');
        }

        await this.persistVote(campaignId, playerId, normalizeVoteDecision(decision));
    }

    private async persistVote(campaignId: string, playerId: string, decision: VoteDecision): Promise<void> {
        await this.db.run(
            `INSERT INTO player_votes (campaign_id, player_id, decision)
             VALUES (?, ?, ?)
             ON DUPLICATE KEY UPDATE decision = ?, voted_at = CURRENT_TIMESTAMP`,
            [campaignId, playerId, decision, decision]
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

        return this.rowToCampaign(row);
    }

    /**
     * Returns the most recent still-active (PENDING and not yet expired)
     * voting campaign for the given instance, or undefined.
     *
     * Needed so newly-mounted dashboards (and reconnecting clients) can render
     * a voting that started before they subscribed to broadcast events.
     */
    async getActiveCampaignForInstance(instanceId: string): Promise<VotingCampaign | undefined> {
        const row = await this.db.get<any>(
            `SELECT * FROM voting_campaigns
             WHERE instance_id = ? AND status = 'PENDING' AND expires_at > NOW()
             ORDER BY created_at DESC
             LIMIT 1`,
            [instanceId]
        );

        if (!row) {
            return undefined;
        }

        return this.rowToCampaign(row);
    }

    private rowToCampaign(row: any): VotingCampaign {
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
            decision: normalizeVoteDecision(row.decision),
            voted_at: new Date(row.voted_at),
        }));
    }

    async getCampaignSummary(campaignId: string, viewerPlayerId?: string): Promise<any | undefined> {
        const campaign = await this.getCampaign(campaignId);
        if (!campaign) {
            return undefined;
        }

        const votes = await this.getVotes(campaignId);
        const continueCount = votes.filter(v => v.decision === 'Continue').length;
        const skipCount = votes.filter(v => v.decision === 'Skip').length;
        const viewerVote = viewerPlayerId
            ? votes.find(v => v.player_id === viewerPlayerId)?.decision
            : undefined;

        return {
            ...campaign,
            continue_count: continueCount,
            skip_count: skipCount,
            proceed_count: continueCount,
            cancel_count: skipCount,
            total_votes: votes.length,
            my_vote: viewerVote,
        };
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
        const continueCount = votes.filter(v => v.decision === 'Continue').length;
        const skipCount = votes.filter(v => v.decision === 'Skip').length;
        const totalVotes = votes.length;

        // Majority wins (> 50%)
        const finalDecision: VoteDecision = continueCount > totalVotes / 2 ? 'Continue' : 'Skip';

        // Atomic guard: only the request that flips PENDING -> RESOLVED gets to
        // own the resolution. Concurrent vote submissions could otherwise both
        // see isVotingComplete=true, both call resolveVoting, both update, and
        // both broadcast `coordinated.voting.resolved` to the instance.
        const result: any = await this.db.run(
            `UPDATE voting_campaigns
             SET status = 'RESOLVED', final_decision = ?, resolved_at = NOW()
             WHERE campaign_id = ? AND status = 'PENDING'`,
            [finalDecision, campaignId]
        );
        const affected = (result && (result.affectedRows ?? result.changes)) ?? 0;
        if (affected === 0) {
            // Another caller already resolved this campaign; surface its data
            // without re-broadcasting.
            const existing = await this.getCampaign(campaignId);
            return {
                campaign_id: campaignId,
                final_decision: existing?.final_decision || finalDecision,
                votes: votes.map(v => ({
                    player_id: v.player_id,
                    decision: v.decision,
                })),
                vote_count: {
                    continue: continueCount,
                    skip: skipCount,
                    proceed: continueCount,
                    cancel: skipCount,
                },
                already_resolved: true,
            };
        }

        // Clear timeout
        const timeout = this.timeouts.get(campaignId);
        if (timeout) {
            clearTimeout(timeout);
            this.timeouts.delete(campaignId);
        }

        if (finalDecision === 'Skip') {
            const skipState = await this.coordinatedAutoSuicideService.addVoteSkipEntry(
                campaign.instance_id,
                campaign.terror_name,
                campaign.round_key,
                'voting',
            );

            this.wsHandler.broadcastToInstance(campaign.instance_id, {
                stream: 'coordinated.autoSuicide.updated',
                data: {
                    instance_id: campaign.instance_id,
                    state: skipState,
                },
                timestamp: new Date().toISOString(),
            });
        }

        logger.info({ campaignId, finalDecision, continueCount, skipCount }, 'Voting resolved');

        return {
            campaign_id: campaignId,
            final_decision: finalDecision,
            votes: votes.map(v => ({
                player_id: v.player_id,
                decision: v.decision,
            })),
            vote_count: {
                continue: continueCount,
                skip: skipCount,
                proceed: continueCount,
                cancel: skipCount,
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
                // Bypass status/expiry checks: at this point the campaign is
                // expired and we're filling in defaults before resolveVoting.
                await this.persistVote(campaignId, member.player_id, 'Skip');
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

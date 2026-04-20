/**
 * Instance Service
 * Handles instance-related operations
 */

import InstanceRepository from '../repositories/InstanceRepository';
import { Instance, InstanceSettings, ErrorCodes } from '../models/types';
import { logger } from '../logger';

export class InstanceService {
    private wsHandler: any;

    constructor(wsHandler: any) {
        this.wsHandler = wsHandler;
    }

    async createInstance(
        instanceId: string,
        creatorId: string,
        maxPlayers: number,
        settings: InstanceSettings
    ): Promise<Instance> {
        const instance = await InstanceRepository.createInstance(
            instanceId,
            creatorId,
            maxPlayers,
            settings
        );

        logger.info({ instanceId: instance.instance_id }, 'Instance created');

        return instance;
    }

    async joinInstance(instanceId: string, playerId: string, playerName: string): Promise<any> {
        let instance = await InstanceRepository.getInstance(instanceId);

        if (!instance) {
            // Auto-create instance when it doesn't exist yet (first joiner becomes creator)
            instance = await this.createInstance(instanceId, playerId, 10, {
                auto_suicide_mode: 'Individual' as const,
                voting_timeout: 30,
            });
            logger.info({ instanceId }, 'Auto-created instance on join');
        }

        // Idempotent: if the user is already a member just return the current
        // membership instead of throwing. Reconnecting clients (especially the
        // C# client after a transient drop) call instance.join again to resume
        // their server-side subscription and would otherwise see spurious
        // "Already joined this instance" errors.
        const alreadyMember = await InstanceRepository.isMemberInInstance(instanceId, playerId);
        if (alreadyMember) {
            const existingMembers = await InstanceRepository.getMembers(instanceId);
            return {
                instance_id: instanceId,
                members: existingMembers.map(m => ({
                    player_id: m.player_id,
                    player_name: m.player_name,
                    joined_at: m.joined_at.toISOString(),
                })),
            };
        }

        if (instance.member_count >= instance.max_players) {
            throw new Error('Instance is full');
        }

        try {
            await InstanceRepository.addMember(instanceId, playerId, playerName);
        } catch (err: any) {
            // Two concurrent join requests can both pass the alreadyMember check
            // above and then race on INSERT. The instance_members table has a
            // UNIQUE (instance_id, player_id) key, so the second one throws
            // ER_DUP_ENTRY. Treat it as a successful idempotent join.
            const code = err?.code || err?.errno;
            if (code !== 'ER_DUP_ENTRY' && code !== '23000' && code !== 1062) {
                throw err;
            }
            const existingMembers = await InstanceRepository.getMembers(instanceId);
            return {
                instance_id: instanceId,
                members: existingMembers.map(m => ({
                    player_id: m.player_id,
                    player_name: m.player_name,
                    joined_at: m.joined_at.toISOString(),
                })),
            };
        }

        const members = await InstanceRepository.getMembers(instanceId);

        // Broadcast member joined event
        this.wsHandler.broadcastToInstance(instanceId, {
            stream: 'instance.member.joined',
            data: {
                instance_id: instanceId,
                player_id: playerId,
                player_name: playerName,
            },
            timestamp: new Date().toISOString(),
        });

        return {
            instance_id: instanceId,
            members: members.map(m => ({
                player_id: m.player_id,
                player_name: m.player_name,
                joined_at: m.joined_at.toISOString(),
            })),
        };
    }

    async leaveInstance(instanceId: string, playerId: string): Promise<void> {
        // Idempotent: skip the entire flow if the user was never a member.
        // Without this guard, instance.leave / disconnect cleanup would emit
        // spurious instance.member.left events for non-members (polluting the
        // dashboard) and could even auto-delete instances that they never
        // joined when member_count happens to be 0.
        const wasMember = await InstanceRepository.isMemberInInstance(instanceId, playerId);
        if (!wasMember) {
            logger.debug({ instanceId, playerId }, 'leaveInstance: not a member, skipping');
            return;
        }

        await InstanceRepository.removeMember(instanceId, playerId);

        // Broadcast member left event
        this.wsHandler.broadcastToInstance(instanceId, {
            stream: 'instance.member.left',
            data: {
                instance_id: instanceId,
                player_id: playerId,
            },
            timestamp: new Date().toISOString(),
        });

        logger.info({ instanceId, playerId }, 'Player left instance');

        // Auto-delete instance if no members remain
        const updatedInstance = await InstanceRepository.getInstance(instanceId);
        if (updatedInstance && updatedInstance.member_count === 0) {
            await this.deleteInstance(instanceId);
            logger.info({ instanceId }, 'Auto-deleted empty instance');
        }
    }

    async isMemberInInstance(instanceId: string, playerId: string): Promise<boolean> {
        return await InstanceRepository.isMemberInInstance(instanceId, playerId);
    }

    async getInstancesForPlayer(playerId: string): Promise<Instance[]> {
        return await InstanceRepository.getInstancesForPlayer(playerId);
    }

    async getInstanceMembers(instanceId: string): Promise<any[]> {
        return await InstanceRepository.getMembers(instanceId);
    }

    async listInstances(filter: 'available' | 'active' | 'all', limit: number, offset: number): Promise<any> {
        const instances = await InstanceRepository.getInstances(filter, limit, offset);
        const total = await InstanceRepository.getTotalInstanceCount(filter);

        return {
            instances: instances.map(i => ({
                instance_id: i.instance_id,
                creator_id: i.creator_id,
                member_count: i.member_count,
                current_player_count: i.member_count,
                max_players: i.max_players,
                status: i.status,
                settings: i.settings,
                created_at: i.created_at.toISOString(),
                updated_at: i.updated_at.toISOString(),
            })),
            total,
            limit,
            offset,
        };
    }

    async getInstance(instanceId: string): Promise<Instance | undefined> {
        return await InstanceRepository.getInstance(instanceId);
    }

    async getInstanceWithMembers(instanceId: string): Promise<any> {
        const instance = await InstanceRepository.getInstance(instanceId);
        
        if (!instance) {
            return null;
        }

        const members = await InstanceRepository.getMembers(instanceId);

        return {
            instance_id: instance.instance_id,
            creator_id: instance.creator_id,
            max_players: instance.max_players,
            member_count: instance.member_count,
            status: instance.status,
            members: members.map(m => ({
                player_id: m.player_id,
                player_name: m.player_name,
                joined_at: m.joined_at.toISOString(),
            })),
            settings: instance.settings,
            created_at: instance.created_at.toISOString(),
            updated_at: instance.updated_at.toISOString(),
        };
    }

    async updateInstance(instanceId: string, updates: Partial<{ max_players: number; settings: InstanceSettings }>): Promise<Instance> {
        const instance = await InstanceRepository.getInstance(instanceId);

        if (!instance) {
            throw new Error(`Instance ${instanceId} not found`);
        }

        const updatedInstance = await InstanceRepository.updateInstance(instanceId, updates);

        // Broadcast instance updated event
        this.wsHandler.broadcastToInstance(instanceId, {
            stream: 'instance.updated',
            data: {
                instance_id: instanceId,
                updates,
            },
            timestamp: new Date().toISOString(),
        });

        logger.info({ instanceId, updates }, 'Instance updated');

        return updatedInstance;
    }

    async deleteInstance(instanceId: string): Promise<void> {
        const instance = await InstanceRepository.getInstance(instanceId);

        if (!instance) {
            throw new Error(`Instance ${instanceId} not found`);
        }

        // Broadcast instance deleted event to members
        this.wsHandler.broadcastToInstance(instanceId, {
            stream: 'instance.deleted',
            data: {
                instance_id: instanceId,
            },
            timestamp: new Date().toISOString(),
        });

        await InstanceRepository.deleteInstance(instanceId);

        // Drop the in-memory subscription bookkeeping for this instance.
        // Otherwise the wsHandler would hold dead subscribers indefinitely
        // (until each socket disconnected), and any later auto-recreate of
        // the same instance id would inherit a polluted subscriber set.
        if (this.wsHandler && typeof this.wsHandler.clearInstanceSubscriptions === 'function') {
            this.wsHandler.clearInstanceSubscriptions(instanceId);
        }

        logger.info({ instanceId }, 'Instance deleted');
    }
}

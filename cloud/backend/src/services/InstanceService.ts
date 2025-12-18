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
        const instance = await InstanceRepository.getInstance(instanceId);

        if (!instance) {
            throw new Error(`Instance ${instanceId} not found`);
        }

        if (instance.member_count >= instance.max_players) {
            throw new Error('Instance is full');
        }

        const isMember = await InstanceRepository.isMemberInInstance(instanceId, playerId);
        if (isMember) {
            throw new Error('Already joined this instance');
        }

        await InstanceRepository.addMember(instanceId, playerId, playerName);

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
                member_count: i.member_count,
                max_players: i.max_players,
                created_at: i.created_at.toISOString(),
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
            members: members.map(m => ({
                player_id: m.player_id,
                player_name: m.player_name,
                joined_at: m.joined_at.toISOString(),
            })),
            settings: instance.settings,
            created_at: instance.created_at.toISOString(),
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

        logger.info({ instanceId }, 'Instance deleted');
    }
}

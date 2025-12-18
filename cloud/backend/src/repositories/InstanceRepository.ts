/**
 * Instance Repository
 * Handles all instance-related database operations
 */

import { v4 as uuidv4 } from 'uuid';
import { getDatabase } from '../database/connection';
import { Instance, InstanceMember, InstanceSettings } from '../models/types';
import { logger } from '../logger';

export class InstanceRepository {
    private db = getDatabase();

    async createInstance(
        instanceId: string,
        creatorId: string,
        maxPlayers: number,
        settings: InstanceSettings
    ): Promise<Instance> {
        // Use provided instanceId (VRChat instance ID) instead of generating UUID
        await this.db.run(
            `INSERT INTO instances (instance_id, creator_id, max_players, settings)
             VALUES (?, ?, ?, ?)`,
            [instanceId, creatorId, maxPlayers, JSON.stringify(settings)]
        );

        logger.info({ instanceId, creatorId }, 'Instance created');

        return {
            instance_id: instanceId,
            creator_id: creatorId,
            max_players: maxPlayers,
            member_count: 0,
            settings,
            status: 'ACTIVE',
            created_at: new Date(),
            updated_at: new Date(),
        };
    }

    async getInstance(instanceId: string): Promise<Instance | undefined> {
        const row = await this.db.get<any>(
            `SELECT * FROM instances WHERE instance_id = ?`,
            [instanceId]
        );

        if (!row) {
            return undefined;
        }

        return this.mapRowToInstance(row);
    }

    async getInstances(filter: 'available' | 'active' | 'all', limit: number, offset: number): Promise<Instance[]> {
        let query = `SELECT * FROM instances`;
        const params: any[] = [];

        if (filter === 'available') {
            query += ` WHERE status = 'ACTIVE' AND member_count < max_players`;
        } else if (filter === 'active') {
            query += ` WHERE status = 'ACTIVE'`;
        }

        query += ` ORDER BY created_at DESC LIMIT ? OFFSET ?`;
        params.push(limit, offset);

        const rows = await this.db.all<any>(query, params);
        return rows.map(row => this.mapRowToInstance(row));
    }

    async getTotalInstanceCount(filter: 'available' | 'active' | 'all'): Promise<number> {
        let query = `SELECT COUNT(*) as count FROM instances`;

        if (filter === 'available') {
            query += ` WHERE status = 'ACTIVE' AND member_count < max_players`;
        } else if (filter === 'active') {
            query += ` WHERE status = 'ACTIVE'`;
        }

        const row = await this.db.get<any>(query);
        return row?.count || 0;
    }

    async updateMemberCount(instanceId: string, count: number): Promise<void> {
        await this.db.run(
            `UPDATE instances SET member_count = ?, updated_at = NOW() WHERE instance_id = ?`,
            [count, instanceId]
        );

        // Update status if full
        const instance = await this.getInstance(instanceId);
        if (instance && instance.member_count >= instance.max_players) {
            await this.updateStatus(instanceId, 'FULL');
        } else if (instance && instance.status === 'FULL' && instance.member_count < instance.max_players) {
            await this.updateStatus(instanceId, 'ACTIVE');
        }
    }

    async updateStatus(instanceId: string, status: 'ACTIVE' | 'INACTIVE' | 'FULL'): Promise<void> {
        await this.db.run(
            `UPDATE instances SET status = ?, updated_at = NOW() WHERE instance_id = ?`,
            [status, instanceId]
        );
    }

    async updateSettings(instanceId: string, settings: InstanceSettings): Promise<void> {
        await this.db.run(
            `UPDATE instances SET settings = ?, updated_at = NOW() WHERE instance_id = ?`,
            [JSON.stringify(settings), instanceId]
        );
    }

    async updateInstance(instanceId: string, updates: Partial<{ max_players: number; settings: InstanceSettings }>): Promise<Instance> {
        const instance = await this.getInstance(instanceId);
        
        if (!instance) {
            throw new Error(`Instance ${instanceId} not found`);
        }

        if (updates.max_players !== undefined) {
            await this.db.run(
                `UPDATE instances SET max_players = ?, updated_at = NOW() WHERE instance_id = ?`,
                [updates.max_players, instanceId]
            );
        }

        if (updates.settings !== undefined) {
            await this.updateSettings(instanceId, updates.settings);
        }

        return await this.getInstance(instanceId) as Instance;
    }

    async deleteInstance(instanceId: string): Promise<void> {
        // First delete all members
        await this.db.run(`DELETE FROM instance_members WHERE instance_id = ?`, [instanceId]);
        
        // Then delete the instance
        await this.db.run(`DELETE FROM instances WHERE instance_id = ?`, [instanceId]);
        
        logger.info({ instanceId }, 'Instance deleted');
    }

    // Instance Members
    async addMember(instanceId: string, playerId: string, playerName: string): Promise<void> {
        await this.db.run(
            `INSERT INTO instance_members (instance_id, player_id, player_name)
             VALUES (?, ?, ?)`,
            [instanceId, playerId, playerName]
        );

        // Update member count
        const members = await this.getMembers(instanceId);
        await this.updateMemberCount(instanceId, members.length);

        logger.info({ instanceId, playerId }, 'Member added to instance');
    }

    async removeMember(instanceId: string, playerId: string): Promise<void> {
        await this.db.run(
            `UPDATE instance_members SET status = 'LEFT', left_at = NOW()
             WHERE instance_id = ? AND player_id = ? AND status = 'ACTIVE'`,
            [instanceId, playerId]
        );

        // Update member count
        const members = await this.getMembers(instanceId);
        await this.updateMemberCount(instanceId, members.length);

        logger.info({ instanceId, playerId }, 'Member removed from instance');
    }

    async getMembers(instanceId: string): Promise<InstanceMember[]> {
        const rows = await this.db.all<any>(
            `SELECT * FROM instance_members WHERE instance_id = ? AND status = 'ACTIVE'
             ORDER BY joined_at ASC`,
            [instanceId]
        );

        return rows.map(row => ({
            id: row.id,
            instance_id: row.instance_id,
            player_id: row.player_id,
            player_name: row.player_name,
            joined_at: new Date(row.joined_at),
            left_at: row.left_at ? new Date(row.left_at) : undefined,
            status: row.status,
        }));
    }

    async isMemberInInstance(instanceId: string, playerId: string): Promise<boolean> {
        const row = await this.db.get<any>(
            `SELECT COUNT(*) as count FROM instance_members
             WHERE instance_id = ? AND player_id = ? AND status = 'ACTIVE'`,
            [instanceId, playerId]
        );

        return (row?.count || 0) > 0;
    }

    async getInstancesForPlayer(playerId: string): Promise<Instance[]> {
        const rows = await this.db.all<any>(
            `SELECT DISTINCT i.* FROM instances i
             INNER JOIN instance_members im ON i.instance_id = im.instance_id
             WHERE im.player_id = ? AND im.status = 'ACTIVE'`,
            [playerId]
        );

        return rows.map(row => this.mapRowToInstance(row));
    }

    private mapRowToInstance(row: any): Instance {
        return {
            instance_id: row.instance_id,
            creator_id: row.creator_id,
            max_players: row.max_players,
            member_count: row.member_count,
            settings: JSON.parse(row.settings),
            status: row.status,
            created_at: new Date(row.created_at),
            updated_at: new Date(row.updated_at),
        };
    }
}

export default new InstanceRepository();

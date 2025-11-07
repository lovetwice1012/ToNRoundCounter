/**
 * Profile Service
 * Handles player profile management
 */

import { getDatabase } from '../database/connection';
import { PlayerProfile, TerrorStats } from '../models/types';
import { logger } from '../logger';

export class ProfileService {
    private db = getDatabase();

    async getProfile(playerId: string): Promise<PlayerProfile | undefined> {
        const row = await this.db.get<any>(
            `SELECT * FROM player_profiles WHERE player_id = ?`,
            [playerId]
        );

        if (!row) {
            // Create default profile if doesn't exist
            return await this.createDefaultProfile(playerId);
        }

        return {
            player_id: row.player_id,
            player_name: row.player_name,
            skill_level: row.skill_level,
            terror_stats: JSON.parse(row.terror_stats),
            total_rounds: row.total_rounds,
            total_survived: row.total_survived,
            last_active: new Date(row.last_active),
            created_at: new Date(row.created_at),
        };
    }

    async createDefaultProfile(playerId: string): Promise<PlayerProfile> {
        const profile: PlayerProfile = {
            player_id: playerId,
            player_name: playerId,
            skill_level: 0,
            terror_stats: {},
            total_rounds: 0,
            total_survived: 0,
            last_active: new Date(),
            created_at: new Date(),
        };

        await this.db.run(
            `INSERT INTO player_profiles (player_id, player_name, skill_level, terror_stats, total_rounds, total_survived)
             VALUES (?, ?, ?, ?, ?, ?)`,
            [playerId, playerId, 0, '{}', 0, 0]
        );

        logger.info({ playerId }, 'Default profile created');

        return profile;
    }

    async updateProfile(playerId: string, updates: Partial<PlayerProfile>): Promise<PlayerProfile> {
        const fields: string[] = [];
        const values: any[] = [];

        if (updates.player_name !== undefined) {
            fields.push('player_name = ?');
            values.push(updates.player_name);
        }

        if (updates.skill_level !== undefined) {
            fields.push('skill_level = ?');
            values.push(updates.skill_level);
        }

        if (updates.terror_stats !== undefined) {
            fields.push('terror_stats = ?');
            values.push(JSON.stringify(updates.terror_stats));
        }

        if (updates.total_rounds !== undefined) {
            fields.push('total_rounds = ?');
            values.push(updates.total_rounds);
        }

        if (updates.total_survived !== undefined) {
            fields.push('total_survived = ?');
            values.push(updates.total_survived);
        }

        fields.push('last_active = NOW()');
        values.push(playerId);

        if (fields.length > 0) {
            const query = `UPDATE player_profiles SET ${fields.join(', ')} WHERE player_id = ?`;
            await this.db.run(query, values);
        }

        logger.info({ playerId }, 'Profile updated');

        // Return updated profile
        const updatedProfile = await this.getProfile(playerId);
        if (!updatedProfile) {
            throw new Error('Failed to retrieve updated profile');
        }
        return updatedProfile;
    }

    async updateTerrorStats(
        playerId: string,
        terrorName: string,
        survived: boolean
    ): Promise<void> {
        const profile = await this.getProfile(playerId);
        if (!profile) {
            return;
        }

        const terrorStats = profile.terror_stats[terrorName] || {
            survival_rate: 0,
            total_rounds: 0,
            survived: 0,
        };

        terrorStats.total_rounds += 1;
        if (survived) {
            terrorStats.survived += 1;
        }
        terrorStats.survival_rate = terrorStats.survived / terrorStats.total_rounds;

        profile.terror_stats[terrorName] = terrorStats;
        profile.total_rounds += 1;
        if (survived) {
            profile.total_survived += 1;
        }

        const updatedProfile = await this.updateProfile(playerId, {
            terror_stats: profile.terror_stats,
            total_rounds: profile.total_rounds,
            total_survived: profile.total_survived,
        });

        logger.info({ playerId, terrorName, survived }, 'Terror stats updated');
    }
}

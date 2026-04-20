/**
 * REST API Controllers
 * Handles HTTP requests
 */

import { Request, Response } from 'express';
import { InstanceService } from '../services/InstanceService';
import { ProfileService } from '../services/ProfileService';
import { AnalyticsService } from '../services/AnalyticsService';
import { logger } from '../logger';
import { ErrorCodes } from '../models/types';

export class ApiController {
    private instanceService: InstanceService;
    private profileService: ProfileService;
    private analyticsService: AnalyticsService;

    constructor(wsHandler: any) {
        this.instanceService = new InstanceService(wsHandler);
        this.profileService = new ProfileService();
        this.analyticsService = new AnalyticsService();
    }

    private resolveRequestUserId(req: Request): string | null {
        const explicitUserId = (req as any).userId as string | undefined;
        return explicitUserId || null;
    }

    private requireRequestUserId(req: Request, res: Response): string | null {
        const userId = this.resolveRequestUserId(req);
        if (!userId) {
            res.status(401).json({
                error: {
                    code: ErrorCodes.AUTH_REQUIRED,
                    message: 'Authenticated user is required',
                },
            });
            return null;
        }

        return userId;
    }

    // GET /api/v1/instances
    async getInstances(req: Request, res: Response): Promise<void> {
        try {
            const userId = this.requireRequestUserId(req, res);
            if (!userId) {
                return;
            }

            const filter = (req.query.filter as string) || 'available';
            const limit = parseInt(req.query.limit as string) || 20;
            const offset = parseInt(req.query.offset as string) || 0;

            const result = await this.instanceService.listInstances(filter as any, limit, offset);
            const visibleInstances = await Promise.all(
                (Array.isArray(result.instances) ? result.instances : []).map(async (instance: any) => {
                    const isOwner = instance.creator_id === userId;
                    const isMember = await this.instanceService.isMemberInInstance(instance.instance_id, userId);
                    return isOwner || isMember ? instance : null;
                })
            );

            res.json({
                ...result,
                instances: visibleInstances.filter((instance): instance is NonNullable<typeof instance> => instance !== null),
            });
        } catch (error: any) {
            logger.error({ error }, 'Error getting instances');
            res.status(500).json({
                error: {
                    code: ErrorCodes.INTERNAL_ERROR,
                    message: error.message,
                },
            });
        }
    }

    // GET /api/v1/instances/:instanceId
    async getInstance(req: Request, res: Response): Promise<void> {
        try {
            const userId = this.requireRequestUserId(req, res);
            if (!userId) {
                return;
            }

            const { instanceId } = req.params;

            const instance = await this.instanceService.getInstanceWithMembers(instanceId);

            if (!instance) {
                res.status(404).json({
                    error: {
                        code: ErrorCodes.NOT_FOUND,
                        message: 'Instance not found',
                    },
                });
                return;
            }

            const isOwner = instance.creator_id === userId;
            const isMember = await this.instanceService.isMemberInInstance(instanceId, userId);
            if (!isOwner && !isMember) {
                res.status(403).json({
                    error: {
                        code: ErrorCodes.PERMISSION_DENIED,
                        message: 'Access denied: not authorized to view this instance',
                    },
                });
                return;
            }

            res.json(instance);
        } catch (error: any) {
            logger.error({ error }, 'Error getting instance');
            res.status(500).json({
                error: {
                    code: ErrorCodes.INTERNAL_ERROR,
                    message: error.message,
                },
            });
        }
    }

    // POST /api/v1/instances
    async createInstance(req: Request, res: Response): Promise<void> {
        try {
            const userId = this.requireRequestUserId(req, res);
            if (!userId) {
                return;
            }

            const { max_players = 6, settings = { auto_suicide_mode: 'Individual' as const, voting_timeout: 30 } } = req.body;

            // Generate a unique instance ID
            const instanceId = `inst_${Date.now()}_${Math.random().toString(36).substring(7)}`;
            const instance = await this.instanceService.createInstance(instanceId, userId, max_players, settings);

            res.status(201).json({
                instance_id: instance.instance_id,
                created_at: instance.created_at.toISOString(),
            });
        } catch (error: any) {
            logger.error({ error }, 'Error creating instance');
            res.status(500).json({
                error: {
                    code: ErrorCodes.INTERNAL_ERROR,
                    message: error.message,
                },
            });
        }
    }

    // PUT /api/v1/instances/:instanceId
    async updateInstance(req: Request, res: Response): Promise<void> {
        try {
            const userId = this.requireRequestUserId(req, res);
            if (!userId) {
                return;
            }

            const { instanceId } = req.params;
            const { max_players, settings } = req.body;

            const existing = await this.instanceService.getInstance(instanceId);
            if (!existing) {
                res.status(404).json({
                    error: {
                        code: ErrorCodes.NOT_FOUND,
                        message: 'Instance not found',
                    },
                });
                return;
            }

            if (existing.creator_id !== userId) {
                res.status(403).json({
                    error: {
                        code: ErrorCodes.PERMISSION_DENIED,
                        message: 'Access denied: only host can update instance',
                    },
                });
                return;
            }

            const updates: any = {};
            if (max_players !== undefined) updates.max_players = max_players;
            if (settings !== undefined) updates.settings = settings;

            const instance = await this.instanceService.updateInstance(instanceId, updates);

            res.json({
                instance_id: instance.instance_id,
                updated_at: instance.updated_at.toISOString(),
            });
        } catch (error: any) {
            logger.error({ error }, 'Error updating instance');
            res.status(500).json({
                error: {
                    code: ErrorCodes.INTERNAL_ERROR,
                    message: error.message,
                },
            });
        }
    }

    // DELETE /api/v1/instances/:instanceId
    async deleteInstance(req: Request, res: Response): Promise<void> {
        try {
            const userId = this.requireRequestUserId(req, res);
            if (!userId) {
                return;
            }

            const { instanceId } = req.params;

            const existing = await this.instanceService.getInstance(instanceId);
            if (!existing) {
                res.status(404).json({
                    error: {
                        code: ErrorCodes.NOT_FOUND,
                        message: 'Instance not found',
                    },
                });
                return;
            }

            if (existing.creator_id !== userId) {
                res.status(403).json({
                    error: {
                        code: ErrorCodes.PERMISSION_DENIED,
                        message: 'Access denied: only host can delete instance',
                    },
                });
                return;
            }

            await this.instanceService.deleteInstance(instanceId);

            res.json({ success: true });
        } catch (error: any) {
            logger.error({ error }, 'Error deleting instance');
            res.status(500).json({
                error: {
                    code: ErrorCodes.INTERNAL_ERROR,
                    message: error.message,
                },
            });
        }
    }

    // GET /api/v1/profiles/:playerId
    async getProfile(req: Request, res: Response): Promise<void> {
        try {
            const requesterId = this.requireRequestUserId(req, res);
            if (!requesterId) {
                return;
            }

            const { playerId } = req.params;

            // Restrict reads to the requester's own profile to prevent
            // arbitrary cross-user profile lookups.
            if (playerId !== requesterId) {
                res.status(403).json({
                    error: {
                        code: ErrorCodes.PERMISSION_DENIED,
                        message: 'Access denied: cannot read another user profile',
                    },
                });
                return;
            }

            const profile = await this.profileService.getProfile(playerId);

            if (!profile) {
                res.status(404).json({
                    error: {
                        code: ErrorCodes.NOT_FOUND,
                        message: 'Profile not found',
                    },
                });
                return;
            }

            res.json({
                player_id: profile.player_id,
                player_name: profile.player_name,
                skill_level: profile.skill_level,
                terror_stats: profile.terror_stats,
                last_active: profile.last_active.toISOString(),
            });
        } catch (error: any) {
            logger.error({ error }, 'Error getting profile');
            res.status(500).json({
                error: {
                    code: ErrorCodes.INTERNAL_ERROR,
                    message: error.message,
                },
            });
        }
    }

    // PUT /api/v1/profiles/:playerId
    async updateProfile(req: Request, res: Response): Promise<void> {
        try {
            const requesterId = this.requireRequestUserId(req, res);
            if (!requesterId) {
                return;
            }

            const { playerId } = req.params;

            if (playerId !== requesterId) {
                res.status(403).json({
                    error: {
                        code: ErrorCodes.PERMISSION_DENIED,
                        message: 'Access denied: cannot update another user profile',
                    },
                });
                return;
            }

            const { player_name } = req.body;

            const profile = await this.profileService.updateProfile(playerId, { player_name });

            res.json({
                player_id: profile.player_id,
                player_name: profile.player_name,
                updated_at: new Date().toISOString(),
            });
        } catch (error: any) {
            logger.error({ error }, 'Error updating profile');
            res.status(500).json({
                error: {
                    code: ErrorCodes.INTERNAL_ERROR,
                    message: error.message,
                },
            });
        }
    }

    // GET /api/v1/stats/terrors
    async getTerrorStats(req: Request, res: Response): Promise<void> {
        try {
            const requesterId = this.requireRequestUserId(req, res);
            if (!requesterId) {
                return;
            }

            const playerId = req.query.player_id as string;

            // If a player-specific overlay is requested, only allow the
            // authenticated requester to query their own personal stats.
            if (playerId && playerId !== requesterId) {
                res.status(403).json({
                    error: {
                        code: ErrorCodes.PERMISSION_DENIED,
                        message: 'Access denied: cannot read another player stats',
                    },
                });
                return;
            }

            // Get global terror statistics from all players
            const globalStats = await this.analyticsService.getTerrorStatistics();

            // If a specific player is requested, merge with their personal stats
            if (playerId) {
                const profile = await this.profileService.getProfile(playerId);
                
                const terrorStats = globalStats.map(globalStat => {
                    const playerStat = profile?.terror_stats[globalStat.terror_name];
                    
                    return {
                        terror_name: globalStat.terror_name,
                        // Global stats
                        global_appearance_count: globalStat.appearance_count,
                        global_rounds: globalStat.rounds_with_terror,
                        // Player-specific stats
                        player_total_rounds: playerStat?.total_rounds || 0,
                        player_survival_rate: playerStat?.survival_rate || 0,
                        player_survived: playerStat?.survived || 0,
                        player_died: playerStat?.died || 0,
                        // Calculated difficulty
                        difficulty: playerStat?.survival_rate 
                            ? (playerStat.survival_rate > 0.7 ? 'easy' : playerStat.survival_rate > 0.4 ? 'medium' : 'hard')
                            : 'unknown',
                    };
                });

                res.json({ terror_stats: terrorStats });
            } else {
                // Return only global stats
                const terrorStats = globalStats.map(stat => ({
                    terror_name: stat.terror_name,
                    total_appearances: stat.appearance_count,
                    rounds_with_terror: stat.rounds_with_terror,
                    avg_desire_count: stat.avg_desire_count,
                    popularity_rank: stat.popularity_rank,
                }));

                res.json({ terror_stats: terrorStats });
            }
        } catch (error: any) {
            logger.error({ error }, 'Error getting terror stats');
            res.status(500).json({
                error: {
                    code: ErrorCodes.INTERNAL_ERROR,
                    message: error.message,
                },
            });
        }
    }
}

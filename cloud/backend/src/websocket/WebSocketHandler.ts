/**
 * WebSocket Server Implementation
 * Handles WebSocket connections and RPC methods
 */

import { Server as WebSocketServer, WebSocket } from 'ws';
import { v4 as uuidv4 } from 'uuid';
import { Server as HttpServer } from 'http';
import { logger } from '../logger';
import { WebSocketMessage, ErrorCodes } from '../models/types';
import SessionRepository from '../repositories/SessionRepository';
import InstanceRepository from '../repositories/InstanceRepository';
import { AuthService } from '../services/AuthService';
import { InstanceService } from '../services/InstanceService';
import { PlayerStateService } from '../services/PlayerStateService';
import { WishedTerrorService } from '../services/WishedTerrorService';
import { VotingService } from '../services/VotingService';
import { ProfileService } from '../services/ProfileService';
import { SettingsService } from '../services/SettingsService';
import { MonitoringService } from '../services/MonitoringService';
// import { RemoteControlService } from '../services/RemoteControlService'; // Removed - security risk
import { AnalyticsService } from '../services/AnalyticsService';
import { BackupService } from '../services/BackupService';
import { ThreatService } from '../services/ThreatService';

interface ExtendedWebSocket extends WebSocket {
    isAlive?: boolean;
    sessionId?: string;
    userId?: string;
    playerId?: string;
    instanceId?: string;
}

export class WebSocketHandler {
    private wss: WebSocketServer;
    private clients: Map<string, ExtendedWebSocket> = new Map();
    private instanceClients: Map<string, Set<string>> = new Map();
    
    private authService: AuthService;
    private instanceService: InstanceService;
    private playerStateService: PlayerStateService;
    private wishedTerrorService: WishedTerrorService;
    private votingService: VotingService;
    private profileService: ProfileService;
    private settingsService: SettingsService;
    private monitoringService: MonitoringService;
    // private remoteControlService: RemoteControlService; // Removed - security risk
    private analyticsService: AnalyticsService;
    private backupService: BackupService;
    private threatService: ThreatService;

    constructor(server: HttpServer) {
        this.wss = new WebSocketServer({ server, path: '/ws' });
        
        // Initialize services
        this.authService = new AuthService();
        this.instanceService = new InstanceService(this);
        this.playerStateService = new PlayerStateService(this);
        this.wishedTerrorService = new WishedTerrorService(this);
        this.votingService = new VotingService(this);
        this.profileService = new ProfileService();
        this.settingsService = new SettingsService(this);
        this.monitoringService = new MonitoringService(this);
        // this.remoteControlService = new RemoteControlService(this); // Removed - security risk
        this.analyticsService = new AnalyticsService();
        this.backupService = new BackupService();
        this.threatService = new ThreatService(this);

        this.setupWebSocketServer();
        this.startHeartbeat();
    }

    private setupWebSocketServer(): void {
        this.wss.on('connection', (ws: ExtendedWebSocket) => {
            const clientId = uuidv4();
            ws.isAlive = true;
            this.clients.set(clientId, ws);

            logger.info({ clientId }, 'WebSocket client connected');

            // Send connection acknowledgment
            this.send(ws, {
                type: 'connected',
                session_id: clientId,
            });

            ws.on('pong', () => {
                ws.isAlive = true;
            });

            ws.on('message', async (data: Buffer) => {
                try {
                    const message: WebSocketMessage = JSON.parse(data.toString());
                    await this.handleMessage(ws, message, clientId);
                } catch (error: any) {
                    logger.error({ error, clientId }, 'Error handling WebSocket message');
                    this.sendError(ws, ErrorCodes.INVALID_PARAMS, 'Invalid message format');
                }
            });

            ws.on('close', () => {
                logger.info({ clientId, sessionId: ws.sessionId }, 'WebSocket client disconnected');
                this.clients.delete(clientId);
                
                // Remove from instance clients
                if (ws.instanceId) {
                    const instanceClients = this.instanceClients.get(ws.instanceId);
                    if (instanceClients) {
                        instanceClients.delete(clientId);
                    }
                }
            });

            ws.on('error', (error) => {
                logger.error({ error, clientId }, 'WebSocket error');
            });
        });

        logger.info('WebSocket server initialized');
    }

    private startHeartbeat(): void {
        setInterval(() => {
            this.wss.clients.forEach((ws: WebSocket) => {
                const extWs = ws as ExtendedWebSocket;
                if (extWs.isAlive === false) {
                    return extWs.terminate();
                }
                extWs.isAlive = false;
                extWs.ping();
            });
        }, 30000);
    }

    private async handleMessage(ws: ExtendedWebSocket, message: WebSocketMessage, clientId: string): Promise<void> {
        const { id, rpc, params } = message;

        if (!rpc) {
            this.sendError(ws, ErrorCodes.INVALID_PARAMS, 'RPC method not specified');
            return;
        }

        logger.debug({ rpc, params, clientId }, 'Handling RPC');

        try {
            let result: any;

            switch (rpc) {
                // Auth methods
                case 'auth.login':
                    result = await this.handleAuthLogin(ws, params, clientId);
                    break;
                case 'auth.logout':
                    result = await this.handleAuthLogout(ws);
                    break;
                case 'auth.refresh':
                    result = await this.handleAuthRefresh(ws);
                    break;

                // Instance methods
                case 'instance.create':
                    result = await this.handleInstanceCreate(ws, params);
                    break;
                // instance.join and instance.leave - REMOVED (VRChat constraint violation)
                // VRChat does not allow external applications to control world joining
                // Players must manually join worlds through VRChat client
                case 'instance.join':
                case 'instance.leave':
                    this.sendError(ws, ErrorCodes.INVALID_PARAMS, 'Remote instance join/leave is not supported due to VRChat platform constraints');
                    return;
                case 'instance.list':
                    result = await this.handleInstanceList(params);
                    break;
                case 'instance.update':
                    result = await this.handleInstanceUpdate(ws, params);
                    break;
                case 'instance.delete':
                    result = await this.handleInstanceDelete(ws, params);
                    break;

                // Player state methods
                case 'player.state.update':
                    result = await this.handlePlayerStateUpdate(ws, params);
                    break;

                // Threat methods
                case 'threat.announce':
                    result = await this.handleThreatAnnounce(ws, params);
                    break;
                case 'threat.response':
                    result = await this.handleThreatResponse(ws, params);
                    break;

                // Voting methods
                case 'coordinated.voting.start':
                    result = await this.handleVotingStart(ws, params);
                    break;
                case 'coordinated.voting.vote':
                    result = await this.handleVotingVote(ws, params);
                    break;

                // Wished terrors methods
                case 'wished.terrors.update':
                    result = await this.handleWishedTerrorsUpdate(ws, params);
                    break;
                case 'wished.terrors.get':
                    result = await this.handleWishedTerrorsGet(ws, params);
                    break;

                // Profile methods
                case 'profile.get':
                    result = await this.handleProfileGet(ws, params);
                    break;

                // Settings methods
                case 'settings.get':
                    result = await this.handleSettingsGet(ws, params);
                    break;
                case 'settings.update':
                    result = await this.handleSettingsUpdate(ws, params);
                    break;
                case 'settings.sync':
                    result = await this.handleSettingsSync(ws, params);
                    break;

                // Monitoring methods
                case 'monitoring.report':
                    result = await this.handleMonitoringReport(ws, params);
                    break;
                case 'monitoring.status':
                    result = await this.handleMonitoringStatus(ws, params);
                    break;
                case 'monitoring.errors':
                    result = await this.handleMonitoringErrors(ws, params);
                    break;

                // Remote control methods - REMOVED FOR SECURITY
                // These endpoints allowed remote command execution which is a critical security vulnerability
                case 'remote.command.create':
                case 'remote.command.execute':
                case 'remote.command.status':
                    this.sendError(ws, ErrorCodes.INVALID_PARAMS, 'Remote control functionality has been disabled for security reasons');
                    return;

                // Analytics methods
                case 'analytics.player':
                    result = await this.handleAnalyticsPlayer(ws, params);
                    break;
                case 'analytics.terror':
                    result = await this.handleAnalyticsTerror(ws, params);
                    break;
                case 'analytics.trends':
                    result = await this.handleAnalyticsTrends(ws, params);
                    break;
                case 'analytics.export':
                    result = await this.handleAnalyticsExport(ws, params);
                    break;

                // Backup methods
                case 'backup.create':
                    result = await this.handleBackupCreate(ws, params);
                    break;
                case 'backup.restore':
                    result = await this.handleBackupRestore(ws, params);
                    break;
                case 'backup.list':
                    result = await this.handleBackupList(ws, params);
                    break;

                default:
                    this.sendError(ws, ErrorCodes.INVALID_PARAMS, `Unknown RPC method: ${rpc}`);
                    return;
            }

            this.sendResponse(ws, rpc, result, id);
        } catch (error: any) {
            logger.error({ error, rpc, params }, 'Error executing RPC');
            this.sendError(ws, ErrorCodes.INTERNAL_ERROR, error.message, id);
        }
    }

    // Auth handlers
    private async handleAuthLogin(ws: ExtendedWebSocket, params: any, clientId: string): Promise<any> {
        const { player_id, client_version } = params;

        if (!player_id || !client_version) {
            throw new Error('player_id and client_version are required');
        }

        const session = await this.authService.createSession(player_id, client_version);
        
        ws.sessionId = session.session_id;
        ws.userId = session.user_id;
        ws.playerId = session.player_id;

        return {
            session_token: session.session_token,
            player_id: session.player_id,
            expires_at: session.expires_at.toISOString(),
        };
    }

    private async handleAuthLogout(ws: ExtendedWebSocket): Promise<any> {
        if (ws.sessionId) {
            await SessionRepository.deleteSession(ws.sessionId);
        }

        return { success: true };
    }

    private async handleAuthRefresh(ws: ExtendedWebSocket): Promise<any> {
        this.requireAuth(ws);

        const session = await this.authService.refreshSession(ws.sessionId!);

        return {
            session_token: session.session_token,
            expires_at: session.expires_at.toISOString(),
        };
    }

    // Instance handlers
    private async handleInstanceCreate(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);

        const { max_players = 6, settings = { auto_suicide_mode: 'Individual' as const, voting_timeout: 30 } } = params;

        // Generate a unique instance ID
        const instanceId = `inst_${Date.now()}_${Math.random().toString(36).substring(7)}`;
        const instance = await this.instanceService.createInstance(
            instanceId,
            ws.userId!,
            max_players,
            settings
        );

        return {
            instance_id: instance.instance_id,
            created_at: instance.created_at.toISOString(),
        };
    }

    // Instance handlers - handleInstanceJoin and handleInstanceLeave REMOVED
    // VRChat platform does not allow external control of world joining/leaving
    // These operations must be performed manually by users through VRChat client

    private async handleInstanceList(params: any): Promise<any> {
        const { filter = 'available', limit = 20, offset = 0 } = params;

        return await this.instanceService.listInstances(filter, limit, offset);
    }

    private async handleInstanceUpdate(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);

        const { instance_id, max_players, settings } = params;

        if (!instance_id) {
            throw new Error('instance_id is required');
        }

        const updates: any = {};
        if (max_players !== undefined) updates.max_players = max_players;
        if (settings !== undefined) updates.settings = settings;

        const instance = await this.instanceService.updateInstance(instance_id, updates);

        return {
            instance_id: instance.instance_id,
            updated_at: instance.updated_at.toISOString(),
        };
    }

    private async handleInstanceDelete(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);

        const { instance_id } = params;

        if (!instance_id) {
            throw new Error('instance_id is required');
        }

        await this.instanceService.deleteInstance(instance_id);

        return { success: true };
    }

    // Player state handlers
    private async handlePlayerStateUpdate(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);

        const { instance_id, player_state } = params;

        if (!instance_id || !player_state) {
            throw new Error('instance_id and player_state are required');
        }

        await this.playerStateService.updatePlayerState(instance_id, player_state);

        // Broadcast to all clients in instance
        this.broadcastToInstance(instance_id, {
            stream: 'player.state.updated',
            data: {
                instance_id,
                player_state,
            },
            timestamp: new Date().toISOString(),
        });

        return {
            success: true,
            timestamp: new Date().toISOString(),
        };
    }

    // Threat handlers
    private async handleThreatAnnounce(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);

        const { instance_id, terror_name, round_key, desire_players } = params;

        if (!instance_id || !terror_name) {
            throw new Error('instance_id and terror_name are required');
        }

        await this.threatService.announceThreat({
            terror_name,
            round_key: round_key || '',
            instance_id,
            desire_players: desire_players || [],
        });

        return { success: true };
    }

    private async handleThreatResponse(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);

        const { threat_id, player_id, decision } = params;

        if (!threat_id || !player_id || !decision) {
            throw new Error('threat_id, player_id, and decision are required');
        }

        await this.threatService.recordThreatResponse(threat_id, player_id, decision);

        return { success: true };
    }

    // Voting handlers
    private async handleVotingStart(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);

        const { instance_id, campaign_id, terror_name, expires_at } = params;

        const campaign = await this.votingService.startVoting(
            campaign_id || `campaign_${uuidv4()}`,
            instance_id,
            terror_name,
            new Date(expires_at)
        );

        // Broadcast to instance
        this.broadcastToInstance(instance_id, {
            stream: 'coordinated.voting.started',
            data: campaign,
            timestamp: new Date().toISOString(),
        });

        return {
            campaign_id: campaign.campaign_id,
            expires_at: campaign.expires_at.toISOString(),
        };
    }

    private async handleVotingVote(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);

        const { campaign_id, player_id, decision } = params;

        await this.votingService.submitVote(campaign_id, player_id, decision);

        // Check if all votes are in
        const campaign = await this.votingService.getCampaign(campaign_id);
        if (campaign && await this.votingService.isVotingComplete(campaign_id)) {
            const result = await this.votingService.resolveVoting(campaign_id);
            
            // Broadcast results
            this.broadcastToInstance(campaign.instance_id, {
                stream: 'coordinated.voting.resolved',
                data: result,
                timestamp: new Date().toISOString(),
            });
        }

        return { success: true };
    }

    // Wished terrors handlers
    private async handleWishedTerrorsUpdate(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);

        const { player_id, wished_terrors } = params;

        await this.wishedTerrorService.updateWishedTerrors(player_id, wished_terrors);

        return {
            success: true,
            updated_at: new Date().toISOString(),
        };
    }

    private async handleWishedTerrorsGet(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);

        const { player_id } = params;

        const wishedTerrors = await this.wishedTerrorService.getWishedTerrors(player_id);

        return { wished_terrors: wishedTerrors };
    }

    // Profile handlers
    private async handleProfileGet(ws: ExtendedWebSocket, params: any): Promise<any> {
        const { player_id } = params;

        const profile = await this.profileService.getProfile(player_id);

        if (!profile) {
            throw new Error('Profile not found');
        }

        return profile;
    }

    // Helper methods
    private requireAuth(ws: ExtendedWebSocket): void {
        if (!ws.sessionId || !ws.userId) {
            throw new Error('Authentication required');
        }
    }

    private send(ws: WebSocket, message: any): void {
        if (ws.readyState === WebSocket.OPEN) {
            ws.send(JSON.stringify(message));
        }
    }

    private sendResponse(ws: WebSocket, rpc: string, result: any, id?: string): void {
        this.send(ws, {
            id,
            rpc,
            result,
        });
    }

    private sendError(ws: WebSocket, code: string, message: string, id?: string): void {
        this.send(ws, {
            id,
            error: {
                code,
                message,
            },
        });
    }

    public broadcastToInstance(instanceId: string, message: any): void {
        const clientIds = this.instanceClients.get(instanceId);
        if (!clientIds) {
            return;
        }

        clientIds.forEach(clientId => {
            const ws = this.clients.get(clientId);
            if (ws) {
                this.send(ws, message);
            }
        });
    }

    public getWss(): WebSocketServer {
        return this.wss;
    }

    // Settings handlers
    private async handleSettingsGet(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);
        const { user_id } = params;
        return await this.settingsService.getSettings(user_id || ws.userId!);
    }

    private async handleSettingsUpdate(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);
        const { user_id, settings } = params;
        return await this.settingsService.updateSettings(user_id || ws.userId!, settings);
    }

    private async handleSettingsSync(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);
        const { user_id, local_settings, local_version } = params;
        return await this.settingsService.syncSettings(
            user_id || ws.userId!,
            local_settings,
            local_version
        );
    }

    // Monitoring handlers
    private async handleMonitoringReport(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);
        const { instance_id, status_data } = params;
        return await this.monitoringService.reportStatus(
            ws.userId!,
            instance_id,
            status_data
        );
    }

    private async handleMonitoringStatus(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);
        const { user_id, limit } = params;
        return await this.monitoringService.getStatusHistory(
            user_id || ws.userId!,
            limit
        );
    }

    private async handleMonitoringErrors(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);
        const { user_id, severity, limit } = params;
        return await this.monitoringService.getErrors(
            user_id || ws.userId!,
            severity,
            limit
        );
    }

    // Remote control handlers
    // Remote control handlers - REMOVED FOR SECURITY
    // These methods allowed remote command execution which poses a critical security risk
    // All functionality has been permanently disabled

    // Analytics handlers
    private async handleAnalyticsPlayer(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);
        const { player_id, time_range } = params;

        const timeRange = time_range ? {
            start: new Date(time_range.start),
            end: new Date(time_range.end),
        } : undefined;

        return await this.analyticsService.getPlayerStatistics(
            player_id || ws.playerId!,
            timeRange
        );
    }

    private async handleAnalyticsTerror(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);
        const { terror_name, time_range } = params;

        const timeRange = time_range ? {
            start: new Date(time_range.start),
            end: new Date(time_range.end),
        } : undefined;

        return await this.analyticsService.getTerrorStatistics(terror_name, timeRange);
    }

    private async handleAnalyticsTrends(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);
        const { group_by = 'day', limit = 30 } = params;
        return await this.analyticsService.getRoundTrends(group_by, limit);
    }

    private async handleAnalyticsExport(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);
        const { format, data_type, filters } = params;

        if (!format || !data_type) {
            throw new Error('format and data_type are required');
        }

        return await this.analyticsService.exportData(format, data_type, filters);
    }

    // Backup handlers
    private async handleBackupCreate(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);
        const { type = 'FULL', compress = true, encrypt = false, description } = params;

        return await this.backupService.createBackup(ws.userId!, {
            type,
            compress,
            encrypt,
            description,
        });
    }

    private async handleBackupRestore(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);
        const { backup_id, validate_before_restore = true, create_backup_before_restore = true } = params;

        if (!backup_id) {
            throw new Error('backup_id is required');
        }

        await this.backupService.restoreBackup(backup_id, {
            validateBeforeRestore: validate_before_restore,
            createBackupBeforeRestore: create_backup_before_restore,
        });

        return { success: true };
    }

    private async handleBackupList(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);
        const { user_id } = params;
        return await this.backupService.listBackups(user_id || ws.userId!);
    }
}

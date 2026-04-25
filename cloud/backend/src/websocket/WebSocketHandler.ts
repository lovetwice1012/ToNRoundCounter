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
import { CoordinatedAutoSuicideService } from '../services/CoordinatedAutoSuicideService';
import { ProfileService } from '../services/ProfileService';
import { SettingsService } from '../services/SettingsService';
import { MonitoringService } from '../services/MonitoringService';
// import { RemoteControlService } from '../services/RemoteControlService'; // Removed - security risk
import { AnalyticsService } from '../services/AnalyticsService';
import { BackupService } from '../services/BackupService';
import { ThreatService } from '../services/ThreatService';
import { RoundService } from '../services/RoundService';

interface ExtendedWebSocket extends WebSocket {
    isAlive?: boolean;
    sessionId?: string;
    userId?: string;
    playerId?: string;
    instanceId?: string;
    /** All instance IDs this socket is currently subscribed to. */
    subscribedInstances?: Set<string>;
    /** Distinguishes between desktop, web, and external SDK clients (defaults to 'csharp'). */
    clientType?: 'csharp' | 'web' | 'sdk' | 'unknown';
    appId?: string;
    appScopes?: string[];
    deviceId?: string;
    deviceName?: string;
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
    private coordinatedAutoSuicideService: CoordinatedAutoSuicideService;
    private profileService: ProfileService;
    private settingsService: SettingsService;
    private monitoringService: MonitoringService;
    // private remoteControlService: RemoteControlService; // Removed - security risk
    private analyticsService: AnalyticsService;
    private backupService: BackupService;
    private threatService: ThreatService;
    private roundService: RoundService;

    constructor(server: HttpServer) {
        this.wss = new WebSocketServer({ server, path: '/ws' });
        
        // Initialize services
        this.authService = new AuthService();
        this.instanceService = new InstanceService(this);
        this.playerStateService = new PlayerStateService(this);
        this.wishedTerrorService = new WishedTerrorService(this);
        this.votingService = new VotingService(this);
        this.coordinatedAutoSuicideService = new CoordinatedAutoSuicideService();
        this.profileService = new ProfileService();
        this.settingsService = new SettingsService(this);
        this.monitoringService = new MonitoringService(this);
        // this.remoteControlService = new RemoteControlService(this); // Removed - security risk
        this.analyticsService = new AnalyticsService();
        this.backupService = new BackupService();
        this.threatService = new ThreatService(this);
        this.roundService = new RoundService();

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
                logger.debug({ 
                    sessionId: ws.sessionId, 
                    playerId: ws.playerId 
                }, 'Received pong from client');
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

            ws.on('close', async () => {
                logger.info({ clientId, sessionId: ws.sessionId, playerId: ws.playerId }, 'WebSocket client disconnected');

                // Auto-leave from all instances when disconnected.
                // Use ws.userId (the authenticated user) as the membership key, because
                // joinInstance is keyed by user_id, not playerId.
                const leaveKey = ws.userId || ws.playerId;
                if (leaveKey) {
                    try {
                        const instances = await this.instanceService.getInstancesForPlayer(leaveKey);
                        for (const instance of instances) {
                            await this.instanceService.leaveInstance(instance.instance_id, leaveKey);
                            logger.info({
                                instanceId: instance.instance_id,
                                userId: leaveKey
                            }, 'Auto-left instance on disconnect');
                        }
                    } catch (error: any) {
                        logger.error({ error, userId: leaveKey }, 'Error auto-leaving instances on disconnect');
                    }
                }

                this.clients.delete(clientId);

                // Remove this client from every instance subscription set.
                if (ws.subscribedInstances) {
                    for (const subbedId of ws.subscribedInstances) {
                        const subscribers = this.instanceClients.get(subbedId);
                        if (subscribers) {
                            subscribers.delete(clientId);
                            if (subscribers.size === 0) {
                                this.instanceClients.delete(subbedId);
                            }
                        }
                    }
                    ws.subscribedInstances.clear();
                }
            });

            ws.on('error', (error) => {
                logger.error({ error, clientId }, 'WebSocket error');
            });
        });

        logger.info('WebSocket server initialized');
    }

    private startHeartbeat(): void {
        // Send ping every 60 seconds (increased from 30 seconds)
        // Client has 60 seconds to respond before being disconnected
        setInterval(() => {
            this.wss.clients.forEach((ws: WebSocket) => {
                const extWs = ws as ExtendedWebSocket;
                if (extWs.isAlive === false) {
                    logger.warn({ 
                        sessionId: extWs.sessionId, 
                        playerId: extWs.playerId,
                        userId: extWs.userId 
                    }, 'Client failed to respond to ping within 60 seconds, terminating connection');
                    return extWs.terminate();
                }
                extWs.isAlive = false;
                extWs.ping();
            });
        }, 60000); // Increased from 30000 to 60000ms
    }

    /**
     * Track that the given client is subscribed to instance broadcasts.
     * Idempotent. Called whenever a client creates/joins an instance or
     * implicitly participates via player.state.update auto-join.
     */
    private subscribeClientToInstance(ws: ExtendedWebSocket, clientId: string, instanceId: string): void {
        if (!instanceId) {
            return;
        }
        let subscribers = this.instanceClients.get(instanceId);
        if (!subscribers) {
            subscribers = new Set<string>();
            this.instanceClients.set(instanceId, subscribers);
        }
        subscribers.add(clientId);

        if (!ws.subscribedInstances) {
            ws.subscribedInstances = new Set<string>();
        }
        ws.subscribedInstances.add(instanceId);
        ws.instanceId = instanceId;
    }

    /**
     * Stop tracking a client's subscription to an instance.
     */
    private unsubscribeClientFromInstance(ws: ExtendedWebSocket, clientId: string, instanceId: string): void {
        if (!instanceId) {
            return;
        }
        const subscribers = this.instanceClients.get(instanceId);
        if (subscribers) {
            subscribers.delete(clientId);
            if (subscribers.size === 0) {
                this.instanceClients.delete(instanceId);
            }
        }
        ws.subscribedInstances?.delete(instanceId);
        if (ws.instanceId === instanceId) {
            ws.instanceId = undefined;
        }
    }

    /**
     * Locate a client's id from the underlying socket. Falls back to scanning
     * the clients map when needed (rare; only used during RPC dispatch).
     */
    private findClientId(ws: ExtendedWebSocket): string | undefined {
        for (const [id, candidate] of this.clients) {
            if (candidate === ws) {
                return id;
            }
        }
        return undefined;
    }

    private async handleMessage(ws: ExtendedWebSocket, message: WebSocketMessage, clientId: string): Promise<void> {
        const { id, rpc, params } = message;

        if (!rpc) {
            this.sendError(ws, ErrorCodes.INVALID_PARAMS, 'RPC method not specified');
            return;
        }

        logger.debug({ rpc, params: this.sanitizeRpcParams(params), clientId }, 'Handling RPC');

        try {
            await this.enforceSdkAppAuthorization(ws, message);
            const dispatchRpc = this.getDispatchRpc(ws, rpc);
            this.enforceSdkScope(ws, dispatchRpc);

            let result: any;

            switch (dispatchRpc) {
                // Auth methods
                case 'auth.register':
                    result = await this.handleAuthRegister(ws, params, clientId);
                    break;
                case 'auth.login':
                    result = await this.handleAuthLogin(ws, params, clientId);
                    break;
                case 'auth.loginWithApiKey':
                    result = await this.handleAuthLoginWithApiKey(ws, params, clientId);
                    break;
                case 'auth.revokeAppToken':
                    result = await this.handleAuthRevokeAppToken(params);
                    break;
                case 'auth.generateOneTimeToken':
                    result = await this.handleAuthGenerateOneTimeToken(params);
                    break;
                case 'auth.loginWithOneTimeToken':
                    result = await this.handleAuthLoginWithOneTimeToken(ws, params, clientId);
                    break;
                case 'auth.logout':
                    result = await this.handleAuthLogout(ws);
                    break;
                case 'auth.refresh':
                    result = await this.handleAuthRefresh(ws);
                    break;
                case 'auth.validateSession':
                    result = await this.handleAuthValidateSession(ws, message, clientId);
                    break;
                case 'custom.rpc.send':
                    result = await this.handleSdkCustomRpcSend(ws, params);
                    break;

                // Instance methods
                case 'instance.create':
                    result = await this.handleInstanceCreate(ws, params, clientId);
                    break;
                // Instance methods
                // Note: C# client communication is trusted
                // VRChat does not allow programmatic world joining, but we track
                // instance membership for analytics and state management
                case 'instance.join':
                    result = await this.handleInstanceJoin(ws, params, clientId);
                    break;
                case 'instance.leave':
                    result = await this.handleInstanceLeave(ws, params, clientId);
                    break;
                case 'instance.list':
                    result = await this.handleInstanceList(ws, params);
                    break;
                case 'instance.update':
                    result = await this.handleInstanceUpdate(ws, params);
                    break;
                case 'instance.delete':
                    result = await this.handleInstanceDelete(ws, params);
                    break;
                case 'instance.get':
                    result = await this.handleInstanceGet(ws, params);
                    break;

                // Player state methods
                case 'player.state.update':
                    result = await this.handlePlayerStateUpdate(ws, params, clientId);
                    break;
                case 'player.states.get':
                    result = await this.handlePlayerStatesGet(ws, params, clientId);
                    break;
                case 'player.state.get':
                    result = await this.handlePlayerStateGet(ws, params, clientId);
                    break;
                case 'player.instance.get':
                    result = await this.handlePlayerInstanceGet(ws, params, clientId);
                    break;

                // Round methods
                case 'round.report':
                    result = await this.handleRoundReport(ws, params);
                    break;
                case 'round.list':
                    result = await this.handleRoundList(ws, params);
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
                case 'coordinated.voting.getCampaign':
                    result = await this.handleVotingGetCampaign(ws, params);
                    break;
                case 'coordinated.voting.getActive':
                    result = await this.handleVotingGetActive(ws, params);
                    break;
                case 'coordinated.voting.getVotes':
                    result = await this.handleVotingGetVotes(ws, params);
                    break;
                case 'coordinated.autoSuicide.get':
                    result = await this.handleCoordinatedAutoSuicideGet(ws, params);
                    break;
                case 'coordinated.autoSuicide.update':
                    result = await this.handleCoordinatedAutoSuicideUpdate(ws, params);
                    break;

                // Wished terrors methods
                case 'wished.terrors.update':
                    result = await this.handleWishedTerrorsUpdate(ws, params);
                    break;
                case 'wished.terrors.get':
                    result = await this.handleWishedTerrorsGet(ws, params);
                    break;
                case 'wished.terrors.findDesirePlayers':
                    result = await this.handleWishedTerrorsFindDesirePlayers(ws, params);
                    break;

                // Profile methods
                case 'profile.get':
                    result = await this.handleProfileGet(ws, params);
                    break;
                case 'profile.update':
                    result = await this.handleProfileUpdate(ws, params);
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
                case 'settings.history':
                    result = await this.handleSettingsHistory(ws, params);
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

                // Client status methods
                case 'client.status.get':
                    result = await this.handleClientStatusGet(ws, params);
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
                case 'analytics.instance':
                    result = await this.handleAnalyticsInstance(ws, params);
                    break;
                case 'analytics.voting':
                    result = await this.handleAnalyticsVoting(ws, params);
                    break;
                case 'analytics.roundTypes':
                    result = await this.handleAnalyticsRoundTypes(ws, params);
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
                case 'backup.delete':
                    result = await this.handleBackupDelete(ws, params);
                    break;

                default:
                    this.sendError(ws, ErrorCodes.INVALID_PARAMS, `Unknown RPC method: ${rpc}`);
                    return;
            }

            logger.info({ rpc, id, resultKeys: result ? Object.keys(result) : [] }, 'Sending response');
            this.sendResponse(ws, rpc, result, id);
            logger.info({ rpc, id }, 'Response sent');
        } catch (error: any) {
            logger.error({ error, rpc, params: this.sanitizeRpcParams(params) }, 'Error executing RPC');
            this.sendError(ws, ErrorCodes.INTERNAL_ERROR, error.message, id);
        }
    }

    // Auth handlers
    private async handleAuthRegister(ws: ExtendedWebSocket, params: any, clientId: string): Promise<any> {
        const { player_id, client_version, client_type } = params;

        if (!player_id || !client_version) {
            throw new Error('player_id and client_version are required');
        }

        const normalizedClientType = this.normalizeClientType(client_type) ?? 'unknown';
        const ipAddress = (ws as any)._socket?.remoteAddress || undefined;

        const result = await this.authService.registerUser(
            player_id,
            client_version,
            normalizedClientType,
            ipAddress
        );

        return {
            user_id: result.user_id,
            api_key: result.api_key,
            is_new: result.is_new,
            message: result.is_new 
                ? 'User registered successfully. Please save your API key securely!'
                : 'User registered successfully. Please save your API key securely!',
        };
    }

    private async handleAuthLoginWithApiKey(ws: ExtendedWebSocket, params: any, clientId: string): Promise<any> {
        const { player_id, api_key, client_version, client_type, device_info, app_id, app_token } = params;

        logger.info({ player_id, hasApiKey: !!api_key, hasAppId: !!app_id, hasAppToken: !!app_token, client_version }, 'Auth.loginWithApiKey called');

        if (!player_id || !api_key || !client_version) {
            throw new Error('player_id, api_key, and client_version are required');
        }

        const ipAddress = (ws as any)._socket?.remoteAddress || undefined;
        const userAgent = (ws as any).upgradeReq?.headers['user-agent'] || undefined;
        const normalizedClientType = this.normalizeClientType(client_type) ?? 'csharp';

        const session = await this.authService.createSessionWithApiKey(
            player_id,
            api_key,
            client_version,
            ipAddress,
            userAgent,
            normalizedClientType,
            device_info,
            app_id,
            app_token
        );

        if (normalizedClientType === 'csharp' && this.hasOtherOpenCSharpClient(session.user_id, ws)) {
            await SessionRepository.deleteSession(session.session_id);
            await SessionRepository.deleteLoginDeviceForSession(session.session_id);
            logger.warn({ userId: session.user_id, playerId: session.player_id, clientId }, 'Rejected duplicate C# client login');
            throw new Error('A C# client is already connected for this API key');
        }

        ws.sessionId = session.session_id;
        ws.userId = session.user_id;
        ws.playerId = session.player_id;
        ws.clientType = normalizedClientType;
        ws.appId = normalizedClientType === 'sdk' ? session.app_id : undefined;
        ws.appScopes = normalizedClientType === 'sdk' ? session.app_scopes : undefined;
        ws.deviceId = typeof device_info?.device_id === 'string' ? device_info.device_id : undefined;
        ws.deviceName = typeof device_info?.device_name === 'string'
            ? device_info.device_name
            : (typeof device_info?.machine_name === 'string' ? device_info.machine_name : undefined);

        logger.info({ 
            sessionId: ws.sessionId, 
            userId: ws.userId, 
            playerId: ws.playerId 
        }, 'Auth.loginWithApiKey successful - credentials set');

        return {
            session_id: session.session_id,
            session_token: session.session_token,
            player_id: session.player_id,
            user_id: session.user_id,
            scopes: session.app_scopes ?? [],
            expires_at: session.expires_at.toISOString(),
        };
    }

    private async handleAuthRevokeAppToken(params: any): Promise<any> {
        const { player_id, api_key, app_id } = params;

        if (!player_id || !api_key || !app_id) {
            throw new Error('player_id, api_key, and app_id are required');
        }

        await this.authService.revokeUserAppToken(player_id, api_key, app_id);

        return {
            success: true,
            app_id,
        };
    }

    private async handleAuthGenerateOneTimeToken(params: any): Promise<any> {
        const { player_id, api_key } = params;

        if (!player_id || !api_key) {
            throw new Error('player_id and api_key are required');
        }

        const token = await this.authService.generateOneTimeToken(player_id, api_key);

        return {
            token,
            expires_in: 300, // 5 minutes in seconds
            login_url: `${this.getFrontendBaseUrl()}/api/auth/one-time-token`,
        };
    }

    private async handleAuthLoginWithOneTimeToken(ws: ExtendedWebSocket, params: any, clientId: string): Promise<any> {
        throw new Error('One-time token login must use POST /api/auth/one-time-token');
    }

    private async handleAuthLogin(ws: ExtendedWebSocket, params: any, clientId: string): Promise<any> {
        const { player_id, client_version, access_key, client_type, device_info } = params;

        if (!player_id || !client_version) {
            throw new Error('player_id and client_version are required');
        }

        // Get IP address and user agent from WebSocket request
        // Note: These may not be available in all environments
        const ipAddress = (ws as any)._socket?.remoteAddress || undefined;
        const userAgent = (ws as any).upgradeReq?.headers['user-agent'] || undefined;

        const session = await this.authService.createSession(
            player_id, 
            client_version,
            ipAddress,
            userAgent,
            access_key,
            this.normalizeClientType(client_type) ?? 'unknown',
            device_info
        );
        
        ws.sessionId = session.session_id;
        ws.userId = session.user_id;
        ws.playerId = session.player_id;
        ws.clientType = this.normalizeClientType(client_type);

        return {
            session_id: session.session_id,
            session_token: session.session_token,
            player_id: session.player_id,
            user_id: session.user_id,
            scopes: session.app_scopes ?? [],
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
        const effectiveAppScopes = session.client_type === 'sdk'
            ? (ws.appScopes ?? session.app_scopes ?? [])
            : undefined;
        ws.sessionId = session.session_id;
        ws.userId = session.user_id;
        ws.playerId = session.player_id;
        ws.clientType = session.client_type;
        ws.appId = session.client_type === 'sdk' ? session.app_id : undefined;
        ws.appScopes = session.client_type === 'sdk' ? effectiveAppScopes : undefined;

        return {
            session_id: session.session_id,
            session_token: session.session_token,
            player_id: session.player_id,
            user_id: session.user_id,
            scopes: effectiveAppScopes ?? [],
            expires_at: session.expires_at.toISOString(),
        };
    }

    private async handleAuthValidateSession(ws: ExtendedWebSocket, message: WebSocketMessage, clientId: string): Promise<any> {
        const params = message.params && typeof message.params === 'object' && !Array.isArray(message.params)
            ? message.params
            : {};
        const { session_token, player_id, client_type, app_id, app_token } = params;

        if (!session_token || !player_id) {
            throw new Error('session_token and player_id are required');
        }

        // Validate session token with AuthService
        const session = await this.authService.validateSession(session_token);
        
        if (!session) {
            throw new Error('Invalid session token');
        }

        const normalizedClientType = this.normalizeClientType(client_type);
        const effectiveClientType = normalizedClientType ?? session.client_type;
        let effectiveAppScopes: string[] | undefined;
        if (session.client_type === 'sdk' || normalizedClientType === 'sdk') {
            if (session.client_type !== 'sdk' || !session.app_id) {
                throw new Error('Session was not created for an SDK app authorization');
            }

            const credentials = this.getMessageAppCredentials(message);
            const ipAddress = (ws as any)._socket?.remoteAddress || undefined;
            const appAuthorization = await this.authService.validateExternalAppAuthorization(
                session.user_id,
                credentials.appId ?? app_id,
                credentials.appToken ?? app_token,
                ipAddress
            );

            if (appAuthorization.appId !== session.app_id) {
                throw new Error('SDK app authorization does not match the session');
            }

            effectiveAppScopes = appAuthorization.scopes;
        }

        if (normalizedClientType === 'csharp' && this.hasOtherOpenCSharpClient(session.user_id, ws)) {
            logger.warn({ userId: session.user_id, playerId: session.player_id, clientId }, 'Rejected duplicate C# client session validation');
            throw new Error('A C# client is already connected for this API key');
        }
        
        // Set WebSocket authentication
        ws.sessionId = session.session_id;
        ws.userId = session.user_id;
        ws.playerId = session.player_id;
        ws.clientType = effectiveClientType;
        ws.appId = ws.clientType === 'sdk' ? session.app_id : undefined;
        ws.appScopes = ws.clientType === 'sdk' ? effectiveAppScopes ?? session.app_scopes : undefined;

        return {
            session_id: session.session_id,
            session_token: session.session_token,
            player_id: session.player_id,
            user_id: session.user_id,
            scopes: ws.appScopes ?? [],
            expires_at: session.expires_at.toISOString(),
        };
    }

    private async handleSdkCustomRpcSend(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);

        if (ws.clientType !== 'sdk' || !ws.appId) {
            throw new Error('Custom app RPC is only available to authenticated SDK clients');
        }

        const method = typeof params?.method === 'string' ? params.method.trim() : '';
        if (!method || method.length > 128) {
            throw new Error('method is required and must be 128 characters or fewer');
        }

        if (method.startsWith('SDK.app.') || method.startsWith('auth.')) {
            throw new Error('method must be an app-local custom method name');
        }

        const instanceId = typeof params?.instance_id === 'string' && params.instance_id.trim()
            ? params.instance_id.trim()
            : undefined;
        const includeSelf = params?.include_self === true;
        const targetUserIds = Array.isArray(params?.target_user_ids)
            ? new Set(
                params.target_user_ids
                    .filter((value: any) => typeof value === 'string' && value.trim())
                    .slice(0, 100)
                    .map((value: string) => value.trim())
            )
            : undefined;

        const stream = `SDK.app.${ws.appId}.custom.rpc`;
        const timestamp = new Date().toISOString();
        let deliveredCount = 0;

        for (const client of this.clients.values()) {
            if (client.readyState !== WebSocket.OPEN) {
                continue;
            }
            if (client.clientType !== 'sdk' || client.appId !== ws.appId) {
                continue;
            }
            if (!this.hasSdkScope(client, 'app:custom_rpc')) {
                continue;
            }
            if (!includeSelf && client === ws) {
                continue;
            }
            if (targetUserIds && !targetUserIds.has(client.userId ?? '') && !targetUserIds.has(client.playerId ?? '')) {
                continue;
            }
            if (instanceId && client.instanceId !== instanceId && !client.subscribedInstances?.has(instanceId)) {
                continue;
            }

            this.send(client, {
                type: 'sdk_app_rpc',
                stream,
                app_id: ws.appId,
                data: {
                    method,
                    payload: params?.payload ?? null,
                    from_user_id: ws.userId,
                    from_player_id: ws.playerId,
                    instance_id: instanceId,
                    timestamp,
                },
                timestamp,
            });
            deliveredCount++;
        }

        return {
            success: true,
            app_id: ws.appId,
            method,
            instance_id: instanceId,
            delivered_count: deliveredCount,
            target_user_count: targetUserIds?.size ?? null,
            timestamp,
        };
    }

    // Instance handlers
    private async handleInstanceCreate(ws: ExtendedWebSocket, params: any, clientId: string): Promise<any> {
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

        // Host implicitly subscribes to their newly created instance.
        this.subscribeClientToInstance(ws, clientId, instance.instance_id);

        return {
            instance_id: instance.instance_id,
            created_at: instance.created_at.toISOString(),
        };
    }

    private async handleInstanceJoin(ws: ExtendedWebSocket, params: any, clientId: string): Promise<any> {
        this.requireAuth(ws);

        const { instance_id, player_name } = params;

        if (!instance_id) {
            throw new Error('instance_id is required');
        }

        const actualPlayerId = ws.userId;
        const actualPlayerName = player_name || ws.playerId || ws.userId;

        if (!actualPlayerId) {
            throw new Error('player_id is required');
        }

        const result = await this.instanceService.joinInstance(
            instance_id,
            actualPlayerId,
            actualPlayerName
        );

        // Subscribe this socket to instance broadcasts now that the user is a member.
        this.subscribeClientToInstance(ws, clientId, instance_id);

        logger.info({ instance_id, player_id: actualPlayerId }, 'Player joined instance');

        return result;
    }

    private async handleInstanceLeave(ws: ExtendedWebSocket, params: any, clientId: string): Promise<any> {
        this.requireAuth(ws);

        const { instance_id } = params;

        if (!instance_id) {
            throw new Error('instance_id is required');
        }

        const actualPlayerId = ws.userId;

        if (!actualPlayerId) {
            throw new Error('player_id is required');
        }

        await this.instanceService.leaveInstance(instance_id, actualPlayerId);

        // Drop this client's instance subscription so it stops receiving broadcasts.
        this.unsubscribeClientFromInstance(ws, clientId, instance_id);

        logger.info({ instance_id, player_id: actualPlayerId }, 'Player left instance');

        return {
            success: true,
            instance_id,
            player_id: actualPlayerId,
        };
    }

    /**
     * Get a list of instances (filtered by availability and pagination).
    * BUG FIX #3 (HIGH): Added privacy filter—now only returns instances the user owns or is a member of.
     * Previously: Returned all instances in database; any authenticated user could enumerate all other players' private instances.
    * Now: Respects ownership/membership boundaries and does not leak unrelated instances.
     */
    private async handleInstanceList(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);

        const { filter = 'available', limit = 20, offset = 0 } = params;

        // SECURITY FIX #3: Only return instances the user owns or is a member of,
        // plus publicly available instances (not private to other users)
        const result = await this.instanceService.listInstances(filter, limit, offset);
        
        // Filter results: include only instances user owns or is a member of.
        // There is no persisted public-visibility flag today, so anything else
        // would leak other users' instances.
        if (result.instances && Array.isArray(result.instances)) {
            const visibleInstances: any[] = [];
            for (const instance of result.instances) {
                const isOwner = instance.creator_id === ws.userId;
                const isMember = await this.instanceService.isMemberInInstance(instance.instance_id, ws.userId!);
                if (isOwner || isMember) {
                    visibleInstances.push(instance);
                }
            }
            return {
                instances: visibleInstances,
                total: visibleInstances.length,  // Note: counts filtered results, not global total
            };
        }
        
        return result;
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

        const current = await this.instanceService.getInstance(instance_id);
        if (!current) {
            throw new Error('Instance not found');
        }

        if (current.creator_id !== ws.userId) {
            throw new Error('Access denied: only host can update instance');
        }

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

        const current = await this.instanceService.getInstance(instance_id);
        if (!current) {
            throw new Error('Instance not found');
        }

        if (current.creator_id !== ws.userId) {
            throw new Error('Access denied: only host can delete instance');
        }

        await this.instanceService.deleteInstance(instance_id);
        this.clearInstanceSubscriptions(instance_id);

        return { success: true };
    }

    private async handleInstanceGet(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);

        const { instance_id } = params;

        if (!instance_id) {
            throw new Error('instance_id is required');
        }

        const instance = await this.instanceService.getInstance(instance_id);
        if (!instance) {
            throw new Error('Instance not found');
        }

        const isOwner = instance.creator_id === ws.userId;
        const isMember = await this.instanceService.isMemberInInstance(instance_id, ws.userId!);
        if (!isOwner && !isMember) {
            throw new Error('Access denied: not authorized to view this instance');
        }

        const members = await this.instanceService.getInstanceMembers(instance_id);

        // BUG FIX #5 (MEDIUM): Check members is non-null before calling .map()
        // Previously: If getInstanceMembers returned null, .map() threw TypeError: Cannot read property 'map' of null
        // Now: Treat null/undefined as empty array
        const memberList = (members && Array.isArray(members)) ? members : [];

        return {
            instance_id: instance.instance_id,
            creator_id: instance.creator_id,
            host_user_id: instance.creator_id,
            max_players: instance.max_players,
            member_count: instance.member_count,
            status: instance.status,
            settings: instance.settings,
            created_at: instance.created_at.toISOString(),
            updated_at: instance.updated_at.toISOString(),
            members: memberList.map((m: any) => ({
                player_id: m.player_id,
                player_name: m.player_name,
                status: m.status,
                joined_at: m.joined_at.toISOString(),
                left_at: m.left_at ? m.left_at.toISOString() : null,
            })),
        };
    }

    // Player state handlers
    private async handlePlayerStateUpdate(ws: ExtendedWebSocket, params: any, clientId: string): Promise<any> {
        this.requireAuth(ws);

        const { instance_id, player_state } = params;

        if (!instance_id || !player_state) {
            throw new Error('instance_id and player_state are required');
        }

        // インスタンスが存在しない場合は自動作成
        // 並行リクエストでの check-then-create レースに耐えるため、
        // 重複キー(ER_DUP_ENTRY/23000) は「他リクエストが先に作った」とみなして無視する。
        let instanceCreated = false;
        try {
            const instance = await this.instanceService.getInstance(instance_id);
            if (!instance) {
                try {
                    await this.instanceService.createInstance(
                        instance_id,
                        ws.userId!,
                        21,
                        {} as any
                    );
                    instanceCreated = true;
                    logger.info({ instance_id, user_id: ws.userId }, 'Auto-created instance for player state update');
                } catch (createErr: any) {
                    const code = createErr?.code || createErr?.errno;
                    if (code === 'ER_DUP_ENTRY' || code === '23000' || code === 1062) {
                        logger.debug({ instance_id }, 'Instance was concurrently created by another request');
                    } else {
                        throw createErr;
                    }
                }
            }
        } catch (error) {
            logger.warn({ instance_id, user_id: ws.userId, error }, 'Failed to ensure instance exists for player state update');
        }

        // インスタンスを作成した場合、またはメンバーでない場合は自動参加
        if (instanceCreated || ws.userId) {
            try {
                const isMember = await this.instanceService.isMemberInInstance(instance_id, ws.userId!);
                if (!isMember) {
                    // プレイヤーをメンバーとして追加
                    await this.instanceService.joinInstance(instance_id, ws.userId!, ws.playerId || ws.userId!);
                    logger.info({ instance_id, user_id: ws.userId }, 'Auto-joined instance for player state update');
                }
            } catch (error) {
                logger.warn({ instance_id, user_id: ws.userId, error }, 'Failed to auto-join instance');
            }
        }

        // Ensure this socket is subscribed to instance broadcasts. Without this,
        // peers in the same instance never see player.state.updated/voting/threat
        // events because instanceClients was never populated for the auto-join path.
        this.subscribeClientToInstance(ws, clientId, instance_id);

        // Force the canonical authenticated identity onto the broadcast payload.
        // The C# client already sets player_id from its local userId, but we cannot
        // trust client-supplied ids when relaying to other instance members.
        if (player_state && typeof player_state === 'object' && !Array.isArray(player_state)) {
            (player_state as any).player_id = ws.userId;
        }

        const stateChanged = await this.playerStateService.updatePlayerState(instance_id, player_state);

        // Only broadcast if state actually changed
        if (stateChanged) {
            const broadcastTs = new Date().toISOString();
            // C# 側は timestamp を送ってこないため、broadcast 時に補う。
            // これが無いとフロントの表示が「更新: 不明」に振れてしまう。
            const playerStateWithTs =
                player_state && typeof player_state === 'object' && !Array.isArray(player_state)
                    ? { ...(player_state as Record<string, any>), timestamp: (player_state as any).timestamp ?? broadcastTs }
                    : player_state;
            this.broadcastToInstance(instance_id, {
                stream: 'player.state.updated',
                data: {
                    instance_id,
                    player_state: playerStateWithTs,
                },
                timestamp: broadcastTs,
            });
        }

        return {
            success: true,
            timestamp: new Date().toISOString(),
        };
    }

    private async handlePlayerStatesGet(ws: ExtendedWebSocket, params: any, clientId?: string): Promise<any> {
        this.requireAuth(ws);

        const instanceId = params?.instanceId ?? params?.instance_id;

        if (!instanceId) {
            throw new Error('instanceId or instance_id is required');
        }

        const isMember = await this.instanceService.isMemberInInstance(instanceId, ws.userId!);
        if (!isMember) {
            throw new Error('Access denied: not a member of this instance');
        }

        // Ensure this socket receives live `player.state.updated` broadcasts for
        // the instance. Read-only consumers (e.g., the web dashboard) never call
        // instance.create/join/player.state.update, so without this subscribe
        // they would only ever see the initial fetch and miss every subsequent
        // change pushed via broadcastToInstance.
        if (clientId) {
            this.subscribeClientToInstance(ws, clientId, instanceId);
        }

        const playerStates = await this.playerStateService.getAllPlayerStates(instanceId);

        return {
            player_states: playerStates,
            instance_id: instanceId,
            count: playerStates.length,
            timestamp: new Date().toISOString(),
        };
    }

    private async handlePlayerStateGet(ws: ExtendedWebSocket, params: any, clientId?: string): Promise<any> {
        this.requireAuth(ws);

        const instanceId = params?.instance_id ?? params?.instanceId;
        if (!instanceId) {
            throw new Error('instance_id or instanceId is required');
        }

        const isMember = await this.instanceService.isMemberInInstance(instanceId, ws.userId!);
        if (!isMember) {
            throw new Error('Access denied: not a member of this instance');
        }

        // Subscribe this socket to instance broadcasts so subsequent live state
        // updates are pushed to the caller (e.g., the web dashboard).
        if (clientId) {
            this.subscribeClientToInstance(ws, clientId, instanceId);
        }

        const playerId = ws.userId!;
        const state = await this.playerStateService.getPlayerState(instanceId, playerId);
        if (!state) {
            return null;
        }

        return state;
    }

    private async handlePlayerInstanceGet(ws: ExtendedWebSocket, params: any, clientId?: string): Promise<any> {
        this.requireAuth(ws);

        const playerId = ws.userId;

        if (!playerId) {
            throw new Error('player_id is required');
        }

        // プレイヤーが参加しているインスタンスを取得
        const instances = await this.instanceService.getInstancesForPlayer(playerId);

        // 最新のインスタンスを取得（通常は1つのみ）
        if (instances.length > 0) {
            const currentInstance = instances[0];
            const members = await this.instanceService.getInstanceMembers(currentInstance.instance_id);

            // Subscribe this socket to instance broadcasts so the caller (typically
            // the web dashboard) immediately starts receiving player.state.updated,
            // voting, and threat events for the instance it just discovered.
            if (clientId) {
                this.subscribeClientToInstance(ws, clientId, currentInstance.instance_id);
            }

            return {
                instance_id: currentInstance.instance_id,
                member_count: currentInstance.member_count,
                max_players: currentInstance.max_players,
                current_player_count: currentInstance.member_count,
                status: currentInstance.status,
                created_at: currentInstance.created_at.toISOString(),
                members: members.map((m: any) => ({
                    player_id: m.player_id,
                    player_name: m.player_name,
                    joined_at: m.joined_at.toISOString(),
                })),
            };
        }

        return null;
    }

    // Threat handlers
    private async handleThreatAnnounce(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);

        const { instance_id, terror_name, round_key, desire_players } = params;

        if (!instance_id || !terror_name) {
            throw new Error('instance_id and terror_name are required');
        }

        const instance = await this.instanceService.getInstance(instance_id);
        if (!instance) {
            throw new Error('Instance not found');
        }

        // Only members of the instance may announce threats. Otherwise any
        // authenticated client could spam threat broadcasts to any instance.
        const isMember = await this.instanceService.isMemberInInstance(instance_id, ws.userId!);
        if (!isMember) {
            throw new Error('Access denied: not a member of this instance');
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

        const { threat_id, decision } = params;

        if (!threat_id || !decision) {
            throw new Error('threat_id and decision are required');
        }

        await this.threatService.recordThreatResponse(threat_id, ws.userId!, decision);

        return { success: true };
    }

    // Voting handlers
    private async handleVotingStart(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);

        const { instance_id, campaign_id, terror_name, expires_at, round_key } = params;

        if (!instance_id || !terror_name || !expires_at) {
            throw new Error('instance_id, terror_name and expires_at are required');
        }

        const instance = await this.instanceService.getInstance(instance_id);
        if (!instance) {
            throw new Error('Instance not found');
        }

        // Only members of the instance may start a voting campaign for it.
        // Without this check any authenticated client could spawn campaigns on
        // arbitrary instances (and trigger broadcasts to their members).
        const isMember = await this.instanceService.isMemberInInstance(instance_id, ws.userId!);
        if (!isMember) {
            throw new Error('Access denied: not a member of this instance');
        }

        const expiresDate = new Date(expires_at);
        if (Number.isNaN(expiresDate.getTime())) {
            throw new Error('expires_at must be a valid ISO timestamp');
        }
        // Bound the campaign lifetime to a sane window. setTimeout fires
        // immediately for past dates and silently overflows beyond ~24.8 days,
        // so clamp to [now+1s, now+10min].
        const now = Date.now();
        const minMs = 1_000;
        const maxMs = 10 * 60_000;
        const delta = expiresDate.getTime() - now;
        if (delta < minMs) {
            expiresDate.setTime(now + minMs);
        } else if (delta > maxMs) {
            expiresDate.setTime(now + maxMs);
        }

        const campaign = await this.votingService.startVoting(
            campaign_id || `campaign_${uuidv4()}`,
            instance_id,
            terror_name,
            expiresDate,
            round_key,
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

        const { campaign_id, decision } = params;

        if (!campaign_id || !decision) {
            throw new Error('campaign_id and decision are required');
        }

        // Authorization: only members of the campaign's instance may vote
        const campaign = await this.votingService.getCampaignSummary(campaign_id, ws.userId!);
        if (!campaign) {
            throw new Error('Campaign not found');
        }
        const isMember = await this.instanceService.isMemberInInstance(campaign.instance_id, ws.userId!);
        if (!isMember) {
            throw new Error('Access denied: not a member of this voting campaign');
        }

        await this.votingService.submitVote(campaign_id, ws.userId!, decision);

        // Check if all votes are in
        if (await this.votingService.isVotingComplete(campaign_id)) {
            const result = await this.votingService.resolveVoting(campaign_id);

            // Only broadcast if THIS request is the one that flipped the
            // campaign to RESOLVED. Otherwise concurrent voters would each
            // emit a duplicate `coordinated.voting.resolved` broadcast.
            if (!result?.already_resolved) {
                this.broadcastToInstance(campaign.instance_id, {
                    stream: 'coordinated.voting.resolved',
                    data: result,
                    timestamp: new Date().toISOString(),
                });
            }
        }

        return { success: true };
    }

    private async handleVotingGetCampaign(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);

        const { campaign_id } = params;
        if (!campaign_id) {
            throw new Error('campaign_id is required');
        }

        const campaign = await this.votingService.getCampaign(campaign_id);
        if (!campaign) {
            throw new Error('Campaign not found');
        }

        const isMember = await this.instanceService.isMemberInInstance(campaign.instance_id, ws.userId!);
        if (!isMember) {
            throw new Error('Access denied: not a member of this campaign instance');
        }

        return this.serializeCampaignSummary(campaign);
    }

    /**
     * Returns the latest active (PENDING, not yet expired) voting campaign for
     * an instance, or { campaign: null } if none. Lets dashboards/clients that
     * connect after a campaign has started still render its UI.
     */
    private async handleVotingGetActive(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);

        const { instance_id } = params || {};
        if (!instance_id) {
            throw new Error('instance_id is required');
        }

        const isMember = await this.instanceService.isMemberInInstance(instance_id, ws.userId!);
        if (!isMember) {
            throw new Error('Access denied: not a member of this instance');
        }

        const activeCampaign = await this.votingService.getActiveCampaignForInstance(instance_id);
        const campaign = activeCampaign
            ? await this.votingService.getCampaignSummary(activeCampaign.campaign_id, ws.userId!)
            : undefined;
        if (!campaign) {
            return { campaign: null };
        }

        return {
            campaign: this.serializeCampaignSummary(campaign),
        };
    }

    private async handleVotingGetVotes(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);

        const { campaign_id } = params;
        if (!campaign_id) {
            throw new Error('campaign_id is required');
        }

        const campaign = await this.votingService.getCampaign(campaign_id);
        if (!campaign) {
            throw new Error('Campaign not found');
        }
        const isMember = await this.instanceService.isMemberInInstance(campaign.instance_id, ws.userId!);
        if (!isMember) {
            throw new Error('Access denied: not a member of this campaign instance');
        }

        const votes = await this.votingService.getVotes(campaign_id);
        return {
            votes: votes.map(v => ({
                ...v,
                voted_at: v.voted_at.toISOString(),
            })),
        };
    }

    private async handleCoordinatedAutoSuicideGet(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);

        const { instance_id } = params || {};
        if (!instance_id) {
            throw new Error('instance_id is required');
        }

        const isMember = await this.instanceService.isMemberInInstance(instance_id, ws.userId!);
        if (!isMember) {
            throw new Error('Access denied: not a member of this instance');
        }

        return await this.coordinatedAutoSuicideService.getState(instance_id);
    }

    private async handleCoordinatedAutoSuicideUpdate(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);

        const { instance_id, state } = params || {};
        if (!instance_id) {
            throw new Error('instance_id is required');
        }

        const isMember = await this.instanceService.isMemberInInstance(instance_id, ws.userId!);
        if (!isMember) {
            throw new Error('Access denied: not a member of this instance');
        }

        const updatedState = await this.coordinatedAutoSuicideService.replaceState(instance_id, state, ws.userId!);

        this.broadcastToInstance(instance_id, {
            stream: 'coordinated.autoSuicide.updated',
            data: {
                instance_id,
                state: updatedState,
            },
            timestamp: new Date().toISOString(),
        });

        return updatedState;
    }

    private serializeCampaignSummary(campaign: any): any {
        return {
            ...campaign,
            created_at: campaign.created_at instanceof Date ? campaign.created_at.toISOString() : campaign.created_at,
            expires_at: campaign.expires_at instanceof Date ? campaign.expires_at.toISOString() : campaign.expires_at,
            resolved_at: campaign.resolved_at instanceof Date
                ? campaign.resolved_at.toISOString()
                : (campaign.resolved_at ?? null),
        };
    }

    // Wished terrors handlers
    private async handleWishedTerrorsUpdate(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);

        const { wished_terrors } = params;

        if (!Array.isArray(wished_terrors)) {
            throw new Error('wished_terrors must be an array');
        }

        await this.wishedTerrorService.updateWishedTerrors(ws.userId!, wished_terrors);

        return {
            success: true,
            updated_at: new Date().toISOString(),
        };
    }

    private async handleWishedTerrorsGet(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);

        const wishedTerrors = await this.wishedTerrorService.getWishedTerrors(ws.userId!);

        return { wished_terrors: wishedTerrors };
    }

    private async handleWishedTerrorsFindDesirePlayers(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);

        const { instance_id, terror_name, round_key } = params;
        if (!instance_id) {
            throw new Error('instance_id is required');
        }
        const isMember = await this.instanceService.isMemberInInstance(instance_id, ws.userId!);
        if (!isMember) {
            throw new Error('Access denied: not a member of this instance');
        }

        logger.info({ instance_id, terror_name, round_key }, 'Finding desire players for terror');

        const desirePlayers = await this.wishedTerrorService.findDesirePlayersForTerror(
            instance_id,
            terror_name,
            round_key || ''
        );

        logger.info({ desirePlayers, count: desirePlayers.length }, 'Found desire players');

        return { desire_players: desirePlayers };
    }

    // Profile handlers
    private async handleProfileGet(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);

        const profile = await this.profileService.getProfile(ws.userId!);

        if (!profile) {
            throw new Error('Profile not found');
        }

        return profile;
    }

    private async handleProfileUpdate(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);

        // Only allow user-editable profile metadata to be set via this RPC.
        // total_rounds / total_survived / terror_stats are aggregated server-side
        // from gameplay events and must not be mutable directly by the client,
        // otherwise any user could trivially fabricate their own statistics.
        const updated = await this.profileService.updateProfile(ws.userId!, {
            player_name: params.player_name,
            skill_level: params.skill_level,
        });

        return updated;
    }

    // Helper methods
    private getFrontendBaseUrl(): string {
        return (process.env.FRONTEND_URL || 'http://localhost:8080').replace(/\/$/, '');
    }

    private hasOtherOpenCSharpClient(userId: string, currentWs: ExtendedWebSocket): boolean {
        for (const client of this.clients.values()) {
            if (client === currentWs) {
                continue;
            }
            if (client.readyState !== WebSocket.OPEN) {
                continue;
            }
            if (client.userId !== userId) {
                continue;
            }

            const effectiveType = client.clientType ?? 'csharp';
            if (effectiveType === 'csharp') {
                return true;
            }
        }

        return false;
    }

    private normalizeClientType(clientType: any): 'csharp' | 'web' | 'sdk' | undefined {
        if (typeof clientType !== 'string') {
            return undefined;
        }
        const lower = clientType.toLowerCase();
        if (lower === 'web' || lower === 'browser' || lower === 'frontend') {
            return 'web';
        }
        if (lower === 'sdk' || lower === 'external-sdk' || lower === 'external' || lower === 'third-party') {
            return 'sdk';
        }
        if (lower === 'csharp' || lower === 'c#' || lower === 'dotnet' || lower === 'app' || lower === 'desktop') {
            return 'csharp';
        }
        return undefined;
    }

    private getMessageAppCredentials(message: WebSocketMessage): { appId?: string; appToken?: string } {
        const params = message.params && typeof message.params === 'object' && !Array.isArray(message.params)
            ? message.params
            : {};

        return {
            appId: typeof message.app_id === 'string'
                ? message.app_id
                : (typeof params.app_id === 'string' ? params.app_id : undefined),
            appToken: typeof message.app_token === 'string'
                ? message.app_token
                : (typeof params.app_token === 'string' ? params.app_token : undefined),
        };
    }

    private isSdkSessionControlRpc(rpc?: string): boolean {
        return rpc === 'auth.loginWithApiKey'
            || rpc === 'auth.logout'
            || rpc === 'auth.refresh'
            || rpc === 'auth.validateSession'
            || rpc === 'auth.generateOneTimeToken'
            || rpc === 'auth.revokeAppToken';
    }

    private getSdkRpcPrefix(appId: string): string {
        return `SDK.app.${appId}.`;
    }

    private isSdkAppCustomRpc(rpc: string): boolean {
        return rpc === 'custom.rpc.send';
    }

    private isSdkDataSendRpc(rpc: string): boolean {
        return new Set([
            'auth.register',
            'auth.login',
            'auth.loginWithOneTimeToken',
            'custom.rpc.send',
            'instance.create',
            'instance.join',
            'instance.leave',
            'instance.update',
            'instance.delete',
            'player.state.update',
            'round.report',
            'threat.announce',
            'threat.response',
            'coordinated.voting.start',
            'coordinated.voting.vote',
            'coordinated.autoSuicide.update',
            'wished.terrors.update',
            'profile.update',
            'settings.update',
            'settings.sync',
            'monitoring.report',
            'backup.create',
            'backup.restore',
            'backup.delete',
        ]).has(rpc);
    }

    private isSdkReadOnlyRpc(rpc: string): boolean {
        return new Set([
            'instance.list',
            'instance.get',
            'player.states.get',
            'player.state.get',
            'player.instance.get',
            'round.list',
            'coordinated.voting.getCampaign',
            'coordinated.voting.getActive',
            'coordinated.voting.getVotes',
            'coordinated.autoSuicide.get',
            'wished.terrors.get',
            'wished.terrors.findDesirePlayers',
            'profile.get',
            'settings.get',
            'settings.history',
            'monitoring.status',
            'monitoring.errors',
            'client.status.get',
            'analytics.player',
            'analytics.terror',
            'analytics.trends',
            'analytics.export',
            'analytics.instance',
            'analytics.voting',
            'analytics.roundTypes',
            'backup.list',
        ]).has(rpc);
    }

    private getDispatchRpc(ws: ExtendedWebSocket, rpc: string): string {
        if (ws.clientType !== 'sdk' || this.isSdkSessionControlRpc(rpc)) {
            return rpc;
        }

        if (!ws.appId) {
            throw new Error('SDK app_id is missing from the authenticated session');
        }

        const expectedPrefix = this.getSdkRpcPrefix(ws.appId);
        if (!rpc.startsWith(expectedPrefix)) {
            if (rpc.startsWith('SDK.app.')) {
                throw new Error(`SDK app RPC must start with ${expectedPrefix}`);
            }

            if (this.isSdkReadOnlyRpc(rpc)) {
                return rpc;
            }

            if (this.isSdkDataSendRpc(rpc)) {
                return rpc;
            }

            throw new Error(`SDK direct RPC is limited to known scoped methods. App custom RPCs must start with ${expectedPrefix}`);
        }

        const innerRpc = rpc.substring(expectedPrefix.length);
        if (!innerRpc) {
            throw new Error('SDK RPC must include a method name after the app namespace');
        }
        if (!this.isSdkAppCustomRpc(innerRpc)) {
            throw new Error('SDK.app namespace is reserved for app-local custom sync RPCs');
        }

        return innerRpc;
    }

    private getRequiredSdkScope(rpc: string): string | undefined {
        const scopeByRpc: Record<string, string> = {
            'custom.rpc.send': 'app:custom_rpc',
            'instance.list': 'read:instances',
            'instance.get': 'read:instances',
            'player.instance.get': 'read:instances',
            'player.states.get': 'read:player_state',
            'player.state.get': 'read:player_state',
            'round.list': 'read:rounds',
            'coordinated.voting.getCampaign': 'read:voting',
            'coordinated.voting.getActive': 'read:voting',
            'coordinated.voting.getVotes': 'read:voting',
            'coordinated.autoSuicide.get': 'read:auto_suicide',
            'wished.terrors.get': 'read:wished_terrors',
            'wished.terrors.findDesirePlayers': 'read:wished_terrors',
            'profile.get': 'read:profiles',
            'settings.get': 'read:settings',
            'settings.history': 'read:settings',
            'monitoring.status': 'read:monitoring',
            'monitoring.errors': 'read:monitoring',
            'client.status.get': 'read:monitoring',
            'analytics.player': 'read:analytics',
            'analytics.terror': 'read:analytics',
            'analytics.trends': 'read:analytics',
            'analytics.export': 'read:analytics',
            'analytics.instance': 'read:analytics',
            'analytics.voting': 'read:analytics',
            'analytics.roundTypes': 'read:analytics',
            'backup.list': 'read:backups',
            'instance.create': 'cloud:instances:write',
            'instance.join': 'cloud:instances:write',
            'instance.leave': 'cloud:instances:write',
            'instance.update': 'cloud:instances:write',
            'instance.delete': 'cloud:instances:write',
            'player.state.update': 'cloud:player_state:write',
            'round.report': 'cloud:rounds:write',
            'threat.announce': 'cloud:threats:write',
            'threat.response': 'cloud:threats:write',
            'coordinated.voting.start': 'cloud:voting:write',
            'coordinated.voting.vote': 'cloud:voting:write',
            'coordinated.autoSuicide.update': 'cloud:auto_suicide:write',
            'wished.terrors.update': 'cloud:wished_terrors:write',
            'profile.update': 'cloud:profiles:write',
            'settings.update': 'cloud:settings:write',
            'settings.sync': 'cloud:settings:write',
            'monitoring.report': 'cloud:monitoring:write',
            'backup.create': 'cloud:backups:write',
            'backup.restore': 'cloud:backups:write',
            'backup.delete': 'cloud:backups:write',
        };

        return scopeByRpc[rpc];
    }

    private hasSdkScope(ws: ExtendedWebSocket, requiredScope: string): boolean {
        const scopes = new Set(ws.appScopes ?? []);
        return scopes.has(requiredScope) || scopes.has('*');
    }

    private enforceSdkScope(ws: ExtendedWebSocket, rpc: string): void {
        if (ws.clientType !== 'sdk' || this.isSdkSessionControlRpc(rpc)) {
            return;
        }

        const requiredScope = this.getRequiredSdkScope(rpc);
        if (!requiredScope) {
            throw new Error('SDK RPC is not available');
        }

        if (!this.hasSdkScope(ws, requiredScope)) {
            throw new Error(`Scope '${requiredScope}' is required for RPC '${rpc}'`);
        }
    }

    private async enforceSdkAppAuthorization(ws: ExtendedWebSocket, message: WebSocketMessage): Promise<void> {
        if (ws.clientType !== 'sdk' || message.rpc === 'auth.loginWithApiKey') {
            return;
        }

        this.requireAuth(ws);

        const { appId, appToken } = this.getMessageAppCredentials(message);
        const ipAddress = (ws as any)._socket?.remoteAddress || undefined;
        const appAuthorization = await this.authService.validateExternalAppAuthorization(
            ws.userId!,
            appId,
            appToken,
            ipAddress
        );

        if (!ws.appId || appAuthorization.appId !== ws.appId) {
            logger.warn({ userId: ws.userId, sessionAppId: ws.appId, requestAppId: appAuthorization.appId }, 'SDK app_id does not match authenticated session');
            throw new Error('SDK app authorization does not match the authenticated session');
        }

        ws.appScopes = appAuthorization.scopes;
    }

    private sanitizeRpcParams(params: any): any {
        if (!params || typeof params !== 'object' || Array.isArray(params)) {
            return params;
        }

        const sanitized = { ...params };
        for (const key of ['api_key', 'app_token', 'session_token', 'access_key', 'token']) {
            if (key in sanitized) {
                sanitized[key] = '[REDACTED]';
            }
        }

        return sanitized;
    }

    private requireAuth(ws: ExtendedWebSocket): void {
        if (!ws.sessionId || !ws.userId) {
            logger.warn({ 
                hasSessionId: !!ws.sessionId, 
                hasUserId: !!ws.userId,
                hasPlayerId: !!ws.playerId 
            }, 'Authentication required - missing credentials');
            throw new Error('Authentication required');
        }
    }

    private send(ws: WebSocket, message: any): void {
        logger.info({ readyState: ws.readyState, isOpen: ws.readyState === WebSocket.OPEN }, 'send() called - checking WebSocket state');
        if (ws.readyState === WebSocket.OPEN) {
            const messageStr = JSON.stringify(message);
            logger.info({ messageLength: messageStr.length, messagePreview: messageStr.substring(0, 200) }, 'Sending WebSocket message');
            ws.send(messageStr);
            logger.info('WebSocket message sent successfully');
        } else {
            logger.error({ readyState: ws.readyState, readyStateNames: ['CONNECTING', 'OPEN', 'CLOSING', 'CLOSED'] }, 'WebSocket not open, cannot send message');
        }
    }

    private sendResponse(ws: WebSocket, rpc: string, result: any, id?: string): void {
        logger.info({ rpc, id, hasResult: !!result }, 'sendResponse called');
        const response = {
            id,
            rpc,
            result,
        };
        logger.debug({ response }, 'Response object created');
        this.send(ws, response);
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
        // Only broadcast to clients that have explicitly subscribed/joined this instance.
        // The previous fallback that fanned out to every authenticated client leaked
        // per-instance events (member joins, votes, threats, ...) to unrelated users.
        const clientIds = this.instanceClients.get(instanceId);
        if (!clientIds || clientIds.size === 0) {
            logger.debug({ instanceId }, 'No instance subscribers; skipping broadcast');
            return;
        }

        clientIds.forEach(clientId => {
            const ws = this.clients.get(clientId);
            if (ws) {
                this.send(ws, message);
            }
        });
    }

    /**
     * Broadcast a message to a specific user across all their connected sockets.
     * BUG FIX #4 (HIGH): Added null-safety guard for concurrent socket disconnection.
     * Previously: A socket could be deleted from this.clients between the identity check
     * and the send() call, or ws could be null from map iteration. Now: Check ws is defined.
     */
    public broadcastToUser(userId: string, message: any): void {
        if (!userId) {
            return;
        }
        this.clients.forEach((ws, key) => {
            // SAFETY: Verify ws exists (could be deleted concurrently) and still connected
            if (ws && ws.userId === userId && ws.readyState === WebSocket.OPEN) {
                try {
                    this.send(ws, message);
                } catch (err) {
                    // Silently drop if send fails (socket may have disconnected mid-iteration)
                    logger.debug({ userId, error: err }, 'Failed to broadcast to user (socket may have disconnected)');
                }
            }
        });
    }

    /**
     * Drop every subscription for an instance after it has been deleted.
     * Without this, clients that were subscribed keep stale entries in their
     * per-ws subscribedInstances set, and the instanceClients map keeps a dead
     * key around until every subscriber disconnects.
     */
    public clearInstanceSubscriptions(instanceId: string): void {
        if (!instanceId) {
            return;
        }
        const subscribers = this.instanceClients.get(instanceId);
        if (subscribers) {
            subscribers.forEach(clientId => {
                const ws = this.clients.get(clientId);
                if (ws?.subscribedInstances) {
                    ws.subscribedInstances.delete(instanceId);
                }
                if (ws && ws.instanceId === instanceId) {
                    ws.instanceId = undefined;
                }
            });
            this.instanceClients.delete(instanceId);
        }
    }

    public getWss(): WebSocketServer {
        return this.wss;
    }

    // Settings handlers
    private async handleSettingsGet(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);
        return await this.settingsService.getSettings(ws.userId!);
    }

    private async handleSettingsUpdate(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);
        const { settings } = params;

        if (!settings || typeof settings !== 'object' || Array.isArray(settings)) {
            throw new Error('settings must be an object');
        }

        return await this.settingsService.updateSettings(ws.userId!, settings);
    }

    private async handleSettingsSync(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);
        const { local_settings, local_version } = params;

        if (!local_settings || typeof local_settings !== 'object' || Array.isArray(local_settings)) {
            throw new Error('local_settings must be an object');
        }

        const normalizedLocalVersion = Number.isFinite(Number(local_version)) ? Number(local_version) : 0;

        return await this.settingsService.syncSettings(
            ws.userId!,
            local_settings,
            normalizedLocalVersion
        );
    }

    private async handleSettingsHistory(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);

        const limit = Number.isFinite(Number(params?.limit)) ? Number(params.limit) : 10;
        const history = await this.settingsService.getSettingsHistory(ws.userId!, limit);

        return {
            history,
        };
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
        const { limit } = params;
        return await this.monitoringService.getStatusHistory(
            ws.userId!,
            limit
        );
    }

    private async handleMonitoringErrors(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);
        const { severity, limit } = params;
        return await this.monitoringService.getErrors(
            ws.userId!,
            severity,
            limit
        );
    }

    // Client status handlers
    private async handleClientStatusGet(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);
        // 認証済みユーザー自身のステータスのみを返す
        const targetUserId = ws.userId;

        if (!targetUserId) {
            throw new Error('player_id is required');
        }

        // Aggregate by clientType across all open sockets for the same user.
        // The previous heuristic could not distinguish web from csharp because
        // both clients are authenticated; that meant the C# vs Web indicators
        // in the dashboard were effectively random.
        let csharpClient: any = null;
        let webClient: any = null;

        this.clients.forEach(client => {
            if (client.userId !== targetUserId) {
                return;
            }
            if (client.readyState !== WebSocket.OPEN) {
                return;
            }

            const status = {
                connected: true,
                player_id: client.playerId,
                user_id: client.userId,
                session_id: client.sessionId,
                device_id: client.deviceId,
                device_name: client.deviceName,
            };

            // Default to csharp when the client did not advertise its type;
            // historically only the C# desktop app speaks to this endpoint.
            const effectiveType = client.clientType ?? 'csharp';
            if (effectiveType === 'web') {
                webClient = status;
            } else {
                csharpClient = status;
            }
        });

        const recentDevices = await SessionRepository.getRecentLoginDevices(targetUserId, 10);

        return {
            player_id: targetUserId,
            csharp_client: csharpClient || { connected: false },
            web_client: webClient || { connected: false },
            recent_devices: recentDevices.map(device => ({
                id: device.id,
                session_id: device.session_id,
                user_id: device.user_id,
                player_id: device.player_id,
                client_type: device.client_type,
                client_version: device.client_version,
                device_id: device.device_id,
                device_name: device.device_name,
                os_description: device.os_description,
                os_architecture: device.os_architecture,
                processor_name: device.processor_name,
                gpu_name: device.gpu_name,
                memory_mb: device.memory_mb,
                ip_address: device.ip_address,
                logged_in_at: device.logged_in_at.toISOString(),
                last_seen_at: device.last_seen_at.toISOString(),
                connected: Array.from(this.clients.values()).some(client =>
                    client.userId === targetUserId &&
                    client.readyState === WebSocket.OPEN &&
                    (client.sessionId === device.session_id || (!!device.device_id && client.deviceId === device.device_id))
                ),
            })),
            timestamp: new Date().toISOString(),
        };
    }

    // Remote control handlers
    // Remote control handlers - REMOVED FOR SECURITY
    // These methods allowed remote command execution which poses a critical security risk
    // All functionality has been permanently disabled

    // Analytics handlers
    private async handleAnalyticsPlayer(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);
        const { time_range } = params;

        const timeRange = time_range ? {
            start: new Date(time_range.start),
            end: new Date(time_range.end),
        } : undefined;

        return await this.analyticsService.getPlayerStatistics(
            ws.userId!,
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

        return await this.analyticsService.getTerrorStatistics(terror_name, timeRange, ws.userId!);
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

        // Always scope export to the caller; previously this returned every
        // user's rows because the service ignored ownership entirely.
        return await this.analyticsService.exportData(format, data_type, filters, ws.userId!);
    }

    private async handleAnalyticsInstance(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);

        const { instance_id } = params;
        if (!instance_id) {
            throw new Error('instance_id is required');
        }

        const isMember = await this.instanceService.isMemberInInstance(instance_id, ws.userId!);
        if (!isMember) {
            throw new Error('Access denied: not a member of this instance');
        }

        return await this.analyticsService.getInstanceStatistics(instance_id);
    }

    private async handleAnalyticsVoting(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);

        const { instance_id } = params;

        if (instance_id) {
            const isMember = await this.instanceService.isMemberInInstance(instance_id, ws.userId!);
            if (!isMember) {
                throw new Error('Access denied: not a member of this instance');
            }
        }

        return await this.analyticsService.getVotingStatistics(instance_id);
    }

    private async handleAnalyticsRoundTypes(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);
        const { time_range } = params;
        const timeRange = time_range
            ? { start: new Date(time_range.start), end: new Date(time_range.end) }
            : undefined;
        return await this.analyticsService.getRoundTypeStatistics(ws.userId!, timeRange);
    }

    // Backup handlers
    private async handleBackupCreate(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);
        const { type = 'FULL', compress = true, encrypt = false, description } = params;

        const normalizedType = typeof type === 'string' ? type.toUpperCase() : 'FULL';
        if (!['FULL', 'DIFFERENTIAL', 'INCREMENTAL'].includes(normalizedType)) {
            throw new Error('type must be one of FULL, DIFFERENTIAL, INCREMENTAL');
        }

        return await this.backupService.createBackup(ws.userId!, {
            type: normalizedType as 'FULL' | 'DIFFERENTIAL' | 'INCREMENTAL',
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
        }, ws.userId!);

        return { success: true };
    }

    private async handleBackupList(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);
        const backups = await this.backupService.listBackups(ws.userId!);
        return { backups };
    }

    private async handleBackupDelete(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);
        const { backup_id } = params;

        if (!backup_id) {
            throw new Error('backup_id is required');
        }

        await this.backupService.deleteBackup(backup_id, ws.userId!);
        return { success: true };
    }

    // Round handlers
    private async handleRoundReport(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);
        const safeParams = (params && typeof params === 'object') ? { ...params } : {};
        const { instance_id } = safeParams;
        if (!instance_id) {
            throw new Error('instance_id is required');
        }
        const isMember = await this.instanceService.isMemberInInstance(instance_id, ws.userId!);
        if (!isMember) {
            throw new Error('Access denied: not a member of this instance');
        }
        // Force the reporter identity to the authenticated user. Otherwise the
        // C# client (or a tampered web client) could attribute rounds to other
        // players inside the same instance.
        safeParams.reporter_user_id = ws.userId;
        await this.roundService.reportRound(safeParams);

        // BUG FIX: Notify subscribers (dashboards) so round-derived statistics
        // refresh in real time. Previously the cloud dashboard's round
        // statistics never updated until the user manually clicked the
        // refresh button because no event was emitted on round.report.
        const reportedAt = new Date().toISOString();
        const eventPayload = {
            stream: 'round.reported',
            data: {
                instance_id,
                reporter_user_id: ws.userId,
                round_id: safeParams.round_id ?? null,
                round_key: safeParams.round_key ?? safeParams.round_type ?? null,
                terror_name: safeParams.terror_name ?? null,
                survived: safeParams.survived ?? null,
                timestamp: reportedAt,
            },
            timestamp: reportedAt,
        };
        // Broadcast to instance subscribers (dashboards watching this instance)
        // and to the reporting user's other sockets (their own dashboards),
        // since per-user statistics are scoped to the authenticated user.
        this.broadcastToInstance(instance_id, eventPayload);
        if (ws.userId) {
            this.broadcastToUser(ws.userId, eventPayload);
        }
        return { success: true };
    }

    private async handleRoundList(ws: ExtendedWebSocket, params: any): Promise<any> {
        this.requireAuth(ws);
        const { instance_id, limit = 100 } = params;
        if (!instance_id) {
            throw new Error('instance_id is required');
        }
        const isMember = await this.instanceService.isMemberInInstance(instance_id, ws.userId!);
        if (!isMember) {
            throw new Error('Access denied: not a member of this instance');
        }
        return await this.roundService.getRounds(instance_id, limit);
    }
}

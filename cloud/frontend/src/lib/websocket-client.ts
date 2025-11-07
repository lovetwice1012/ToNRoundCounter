/**
 * ToNRoundCounter Cloud WebSocket Client
 * Handles WebSocket communication with the backend
 */
import type { SessionInfo } from '@tonroundcounter/types';

export interface WebSocketMessage {
    id?: string;
    version?: string;
    type: 'request' | 'response' | 'stream' | 'error';
    method?: string;
    status?: 'success' | 'error';
    params?: any;
    result?: any;
    error?: {
        code: string;
        message: string;
        details?: Record<string, unknown>;
    };
    event?: string;
    stream?: string;
    data?: any;
    timestamp?: string;
}

export interface Session {
    session_token: string;
    player_id: string;
    expires_at: string;
}

export interface InstanceSummary {
    instance_id: string;
    member_count: number;
    max_players: number;
    status?: string;
    created_at: string;
}

export interface PlayerState {
    player_id: string;
    state: string;
    position?: { x: number; y: number; z: number };
    health?: number;
    timestamp: string;
}

export type StreamCallback = (data: any) => void;
export type ConnectionStateCallback = (state: 'connected' | 'disconnected' | 'reconnecting') => void;

export class ToNRoundCloudClient {
    private ws: WebSocket | null = null;
    private url: string;
    private sessionToken: string | null = null;
    private playerId: string | null = null;
    private reconnectAttempts = 0;
    private maxReconnectAttempts = 5;
    private reconnectDelay = 1000;
    private messageId = 0;
    private pendingRequests = new Map<string, {
        resolve: (value: any) => void;
        reject: (error: any) => void;
        timeout: NodeJS.Timeout;
    }>();
    private streamCallbacks = new Map<string, Set<StreamCallback>>();
    private connectionStateCallbacks = new Set<ConnectionStateCallback>();
    private heartbeatInterval: NodeJS.Timeout | null = null;
    private clientId = 'tonr-dashboard';
    private clientVersion = '1.0.0';
    private capabilities = [
        'instance.list',
        'instance.join',
        'player.state.update',
        'monitoring.report',
        'analytics.player',
    ];
    private sessionInfo: SessionInfo | null = null;

    constructor(url: string) {
        this.url = url;
    }

    /**
     * Connect to WebSocket server
     */
    async connect(): Promise<void> {
        if (this.isConnected()) {
            return;
        }

        return new Promise((resolve, reject) => {
            try {
                this.ws = new WebSocket(this.url);

                this.ws.onopen = async () => {
                    console.log('[ToNRoundCloud] Connected');
                    this.reconnectAttempts = 0;
                    try {
                        await this.performHandshake();
                        this.notifyConnectionState('connected');
                        this.startHeartbeat();
                        resolve();
                    } catch (error) {
                        console.error('[ToNRoundCloud] Handshake failed:', error);
                        this.ws?.close();
                        reject(error);
                    }
                };

                this.ws.onmessage = (event) => {
                    this.handleMessage(event.data);
                };

                this.ws.onerror = (error) => {
                    console.error('[ToNRoundCloud] WebSocket error:', error);
                };

                this.ws.onclose = () => {
                    console.log('[ToNRoundCloud] Disconnected');
                    this.notifyConnectionState('disconnected');
                    this.stopHeartbeat();
                    this.sessionInfo = null;
                    this.attemptReconnect();
                };
            } catch (error) {
                reject(error);
            }
        });
    }

    /**
     * Disconnect from WebSocket server
     */
    disconnect(): void {
        if (this.ws) {
            this.ws.close();
            this.ws = null;
        }
        this.stopHeartbeat();
    }

    /**
     * Check if connected
     */
    isConnected(): boolean {
        return this.ws !== null && this.ws.readyState === WebSocket.OPEN;
    }

    /**
     * Login to the service
     */
    async login(playerId: string, clientVersion: string): Promise<Session> {
        const result = await this.call('auth.login', {
            player_id: playerId,
            client_version: clientVersion,
        });

        this.sessionToken = result.session_token;
        this.playerId = result.player_id;

        return result;
    }

    /**
     * Logout
     */
    async logout(): Promise<void> {
        await this.call('auth.logout', {});
        this.sessionToken = null;
        this.playerId = null;
    }

    /**
     * Refresh session
     */
    async refreshSession(): Promise<Session> {
        const result = await this.call('auth.refresh', {});
        this.sessionToken = result.session_token;
        return result;
    }

    // Instance Management
    async createInstance(maxPlayers: number = 6, settings: any = {}): Promise<InstanceSummary> {
        return await this.call('instance.create', { max_players: maxPlayers, settings });
    }

    async joinInstance(instanceId: string): Promise<void> {
        return await this.call('instance.join', { instance_id: instanceId });
    }

    async leaveInstance(instanceId: string): Promise<void> {
        return await this.call('instance.leave', { instance_id: instanceId });
    }

    async listInstances(
        filter: 'available' | 'active' | 'all' = 'available',
        limit: number = 20,
        offset: number = 0
    ): Promise<{ instances: InstanceSummary[]; total: number; limit: number; offset: number }> {
        return await this.call('instance.list', { filter, limit, offset });
    }

    async updateInstance(instanceId: string, updates: { max_players?: number; settings?: any }): Promise<void> {
        return await this.call('instance.update', {
            instance_id: instanceId,
            ...updates,
        });
    }

    async deleteInstance(instanceId: string): Promise<void> {
        return await this.call('instance.delete', { instance_id: instanceId });
    }

    async getInstance(instanceId: string): Promise<any> {
        return await this.call('instance.get', { instance_id: instanceId });
    }

    // Player State
    async updatePlayerState(playerId: string, state: string, data: any = {}): Promise<void> {
        return await this.call('player.state.update', {
            player_id: playerId,
            state,
            ...data,
        });
    }

    async getPlayerState(instanceId: string, playerId: string): Promise<PlayerState> {
        return await this.call('player.state.get', {
            instance_id: instanceId,
            player_id: playerId,
        });
    }

    async getAllPlayerStates(instanceId: string): Promise<PlayerState[]> {
        const result = await this.call('player.state.getAll', { instance_id: instanceId });
        return result.states;
    }

    // Threat/Terror
    async announceThreat(instanceId: string, terrorName: string, roundKey: string, desirePlayers: Array<{ player_id: string; player_name: string }>): Promise<void> {
        return await this.call('threat.announce', {
            instance_id: instanceId,
            terror_name: terrorName,
            round_key: roundKey,
            desire_players: desirePlayers,
        });
    }

    async respondToThreat(threatId: string, playerId: string, decision: 'survive' | 'cancel' | 'skip' | 'execute' | 'timeout'): Promise<void> {
        return await this.call('threat.response', {
            threat_id: threatId,
            player_id: playerId,
            decision,
        });
    }

    // Voting
    async startVoting(instanceId: string, terrorName: string, expiresAt: Date): Promise<any> {
        return await this.call('coordinated.voting.start', {
            instance_id: instanceId,
            terror_name: terrorName,
            expires_at: expiresAt.toISOString(),
        });
    }

    async submitVote(campaignId: string, playerId: string, decision: 'Proceed' | 'Cancel'): Promise<void> {
        return await this.call('coordinated.voting.vote', {
            campaign_id: campaignId,
            player_id: playerId,
            decision,
        });
    }

    async getVotingCampaign(campaignId: string): Promise<any> {
        return await this.call('coordinated.voting.getCampaign', { campaign_id: campaignId });
    }

    async getVotingVotes(campaignId: string): Promise<any[]> {
        const result = await this.call('coordinated.voting.getVotes', { campaign_id: campaignId });
        return result.votes;
    }

    // Wished Terrors
    async updateWishedTerrors(playerId: string, wishedTerrors: string[]): Promise<void> {
        return await this.call('wished.terrors.update', {
            player_id: playerId,
            wished_terrors: wishedTerrors,
        });
    }

    async getWishedTerrors(playerId: string): Promise<string[]> {
        const result = await this.call('wished.terrors.get', { player_id: playerId });
        return result.wished_terrors;
    }

    async findDesirePlayersForTerror(instanceId: string, terrorName: string, roundKey: string = ''): Promise<Array<{ player_id: string; player_name: string }>> {
        const result = await this.call('wished.terrors.findDesirePlayers', {
            instance_id: instanceId,
            terror_name: terrorName,
            round_key: roundKey,
        });
        return result.desire_players;
    }

    // Profile
    async getProfile(playerId: string): Promise<any> {
        return await this.call('profile.get', { player_id: playerId });
    }

    async updateProfile(playerId: string, updates: {
        player_name?: string;
        skill_level?: number;
        terror_stats?: any;
        total_rounds?: number;
        total_survived?: number;
    }): Promise<any> {
        return await this.call('profile.update', {
            player_id: playerId,
            ...updates,
        });
    }

    // Settings
    async getSettings(userId?: string): Promise<any> {
        return await this.call('settings.get', { user_id: userId });
    }

    async updateSettings(userId: string, settings: any): Promise<any> {
        return await this.call('settings.update', { user_id: userId, settings });
    }

    async syncSettings(userId: string, localSettings: any, localVersion: number): Promise<any> {
        return await this.call('settings.sync', {
            user_id: userId,
            local_settings: localSettings,
            local_version: localVersion,
        });
    }

    async getSettingsHistory(userId: string, limit: number = 10): Promise<any[]> {
        const result = await this.call('settings.history', { user_id: userId, limit });
        return result.history;
    }

    // Monitoring
    async reportStatus(instanceId: string, statusData: any): Promise<any> {
        return await this.call('monitoring.report', {
            instance_id: instanceId,
            status_data: statusData,
        });
    }

    async getMonitoringStatus(userId?: string, limit: number = 10): Promise<any> {
        return await this.call('monitoring.status', { user_id: userId, limit });
    }

    async getMonitoringErrors(userId?: string, severity?: string, limit: number = 50): Promise<any> {
        return await this.call('monitoring.errors', { user_id: userId, severity, limit });
    }

    // Remote Control
    async createRemoteCommand(instanceId: string, commandType: string, action: string, parameters: any = {}, priority: number = 0): Promise<any> {
        return await this.call('remote.command.create', {
            instance_id: instanceId,
            command_type: commandType,
            action,
            parameters,
            priority,
        });
    }

    async executeRemoteCommand(commandId: string): Promise<any> {
        return await this.call('remote.command.execute', { command_id: commandId });
    }

    async getRemoteCommandStatus(commandId?: string, instanceId?: string): Promise<any> {
        return await this.call('remote.command.status', { command_id: commandId, instance_id: instanceId });
    }

    // Analytics
    async getPlayerAnalytics(playerId: string, timeRange?: { start: Date; end: Date }): Promise<any> {
        return await this.call('analytics.player', {
            player_id: playerId,
            time_range: timeRange ? {
                start: timeRange.start.toISOString(),
                end: timeRange.end.toISOString(),
            } : undefined,
        });
    }

    async getTerrorAnalytics(terrorName?: string, timeRange?: { start: Date; end: Date }): Promise<any> {
        return await this.call('analytics.terror', {
            terror_name: terrorName,
            time_range: timeRange ? {
                start: timeRange.start.toISOString(),
                end: timeRange.end.toISOString(),
            } : undefined,
        });
    }

    async getAnalyticsTrends(groupBy: 'day' | 'week' | 'month' = 'day', limit: number = 30): Promise<any> {
        return await this.call('analytics.trends', { group_by: groupBy, limit });
    }

    async getInstanceAnalytics(instanceId: string): Promise<any> {
        return await this.call('analytics.instance', { instance_id: instanceId });
    }

    async getVotingAnalytics(instanceId: string): Promise<any> {
        return await this.call('analytics.voting', { instance_id: instanceId });
    }

    async exportAnalytics(format: 'json' | 'csv', dataType: string, filters?: any): Promise<string> {
        return await this.call('analytics.export', { format, data_type: dataType, filters });
    }

    // Backup
    async createBackup(type: 'FULL' | 'DIFFERENTIAL' | 'INCREMENTAL' = 'FULL', compress: boolean = true, encrypt: boolean = false, description?: string): Promise<any> {
        return await this.call('backup.create', { type, compress, encrypt, description });
    }

    async restoreBackup(backupId: string, validateBeforeRestore: boolean = true, createBackupBeforeRestore: boolean = true): Promise<void> {
        return await this.call('backup.restore', {
            backup_id: backupId,
            validate_before_restore: validateBeforeRestore,
            create_backup_before_restore: createBackupBeforeRestore,
        });
    }

    async listBackups(userId?: string): Promise<any[]> {
        return await this.call('backup.list', { user_id: userId });
    }

    // Stream Subscriptions
    private async performHandshake(): Promise<void> {
        const result = await this.call('auth.connect', {
            clientId: this.clientId,
            clientVersion: this.clientVersion,
            capabilities: this.capabilities,
        });
        this.sessionInfo = result as SessionInfo;
    }

    onPlayerStateUpdate(callback: StreamCallback): () => void {
        return this.subscribe('player.state.updated', callback);
    }

    onInstanceMemberJoined(callback: StreamCallback): () => void {
        return this.subscribe('instance.member.joined', callback);
    }

    onInstanceMemberLeft(callback: StreamCallback): () => void {
        return this.subscribe('instance.member.left', callback);
    }

    onInstanceUpdated(callback: StreamCallback): () => void {
        return this.subscribe('instance.updated', callback);
    }

    onInstanceDeleted(callback: StreamCallback): () => void {
        return this.subscribe('instance.deleted', callback);
    }

    onThreatAnnounced(callback: StreamCallback): () => void {
        return this.subscribe('threat.announced', callback);
    }

    onVotingStarted(callback: StreamCallback): () => void {
        return this.subscribe('coordinated.voting.started', callback);
    }

    onVotingResolved(callback: StreamCallback): () => void {
        return this.subscribe('coordinated.voting.resolved', callback);
    }

    // Connection State
    onConnectionStateChange(callback: ConnectionStateCallback): () => void {
        this.connectionStateCallbacks.add(callback);
        return () => {
            this.connectionStateCallbacks.delete(callback);
        };
    }

    // Private Methods
    private async call(method: string, params: Record<string, unknown> = {}): Promise<any> {
        if (!this.isConnected()) {
            throw new Error('Not connected to server');
        }

        const id = `req-${++this.messageId}`;
        const message: WebSocketMessage = {
            id,
            version: '1.0',
            type: 'request',
            method,
            params,
            timestamp: new Date().toISOString(),
        };

        return new Promise((resolve, reject) => {
            const timeout = setTimeout(() => {
                this.pendingRequests.delete(id);
                reject(new Error(`Request timeout: ${method}`));
            }, 30000);

            this.pendingRequests.set(id, { resolve, reject, timeout });
            this.ws!.send(JSON.stringify(message));
        });
    }

    private subscribe(stream: string, callback: StreamCallback): () => void {
        if (!this.streamCallbacks.has(stream)) {
            this.streamCallbacks.set(stream, new Set());
        }
        this.streamCallbacks.get(stream)!.add(callback);

        return () => {
            const callbacks = this.streamCallbacks.get(stream);
            if (callbacks) {
                callbacks.delete(callback);
            }
        };
    }

    private handleMessage(data: string): void {
        try {
            const message: WebSocketMessage = JSON.parse(data);

            if (message.type === 'response' && message.id && this.pendingRequests.has(message.id)) {
                const pending = this.pendingRequests.get(message.id)!;
                clearTimeout(pending.timeout);
                this.pendingRequests.delete(message.id);

                if (message.status === 'error' || message.error) {
                    const errorPayload = message.error;
                    pending.reject(new Error(errorPayload ? `${errorPayload.code}: ${errorPayload.message}` : 'Unknown error'));
                } else {
                    pending.resolve(message.result);
                }
                return;
            }

            if (message.type === 'error' && message.id && this.pendingRequests.has(message.id)) {
                const pending = this.pendingRequests.get(message.id)!;
                clearTimeout(pending.timeout);
                this.pendingRequests.delete(message.id);
                const errorPayload = message.error;
                pending.reject(new Error(errorPayload ? `${errorPayload.code}: ${errorPayload.message}` : 'Unknown error'));
                return;
            }

            if (message.type !== 'stream') {
                return;
            }

            const eventName = message.event || message.stream;
            if (!eventName) {
                return;
            }

            const callbacks = this.streamCallbacks.get(eventName);
            if (callbacks) {
                callbacks.forEach(callback => callback(message.data));
            }
        } catch (error) {
            console.error('[ToNRoundCloud] Failed to parse message:', error);
        }
    }

    private startHeartbeat(): void {
        this.heartbeatInterval = setInterval(() => {
            if (this.isConnected()) {
                // WebSocket ping is handled by the browser
                // We just keep the connection alive
            }
        }, 25000);
    }

    private stopHeartbeat(): void {
        if (this.heartbeatInterval) {
            clearInterval(this.heartbeatInterval);
            this.heartbeatInterval = null;
        }
    }

    private attemptReconnect(): void {
        if (this.reconnectAttempts >= this.maxReconnectAttempts) {
            console.error('[ToNRoundCloud] Max reconnect attempts reached');
            return;
        }

        this.reconnectAttempts++;
        this.notifyConnectionState('reconnecting');

        const delay = this.reconnectDelay * Math.pow(2, this.reconnectAttempts - 1);
        console.log(`[ToNRoundCloud] Reconnecting in ${delay}ms... (attempt ${this.reconnectAttempts}/${this.maxReconnectAttempts})`);

        setTimeout(() => {
            this.connect().catch(error => {
                console.error('[ToNRoundCloud] Reconnect failed:', error);
            });
        }, delay);
    }

    private notifyConnectionState(state: 'connected' | 'disconnected' | 'reconnecting'): void {
        this.connectionStateCallbacks.forEach(callback => callback(state));
    }
}

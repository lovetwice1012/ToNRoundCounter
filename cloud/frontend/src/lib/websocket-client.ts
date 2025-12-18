/**
 * ToNRoundCounter Cloud WebSocket Client
 * Handles WebSocket communication with the backend
 */

export interface WebSocketMessage {
    id?: string;
    rpc?: string;
    params?: any;
    result?: any;
    error?: {
        code: string;
        message: string;
    };
    type?: string;
    stream?: string;
    data?: any;
    timestamp?: string;
}

export interface Session {
    session_token: string;
    player_id: string;
    expires_at: string;
}

export interface Instance {
    instance_id: string;
    host_user_id: string;
    max_players: number;
    current_player_count: number;
    status: string;
    created_at: string;
}

export interface PlayerState {
    player_id: string;
    player_name?: string;
    velocity?: number;
    afk_duration?: number;
    items?: string[];
    damage?: number;
    is_alive?: boolean;
    state?: string;
    position?: { x: number; y: number; z: number };
    health?: number;
    timestamp?: string;
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

    constructor(url: string) {
        this.url = url;
    }

    /**
     * Connect to WebSocket server
     */
    async connect(): Promise<void> {
        return new Promise((resolve, reject) => {
            try {
                this.ws = new WebSocket(this.url);

                this.ws.onopen = () => {
                    console.log('[ToNRoundCloud] Connected');
                    this.reconnectAttempts = 0;
                    this.notifyConnectionState('connected');
                    this.startHeartbeat();
                    resolve();
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
     * Set session token manually (for reconnection)
     */
    setSessionToken(token: string, playerId: string): void {
        this.sessionToken = token;
        this.playerId = playerId;
        console.log('[ToNRoundCloud] Session token manually set:', !!this.sessionToken, 'Player ID:', this.playerId);
    }

    /**
     * Login to the service
     */
    async login(playerId: string, clientVersion: string, accessKey?: string): Promise<Session> {
        const params: any = {
            player_id: playerId,
            client_version: clientVersion,
        };

        if (accessKey) {
            params.access_key = accessKey;
        }

        const result = await this.call('auth.login', params);

        this.sessionToken = result.session_token;
        this.playerId = result.player_id;

        return result;
    }

    /**
     * Login with one-time token
     */
    async loginWithOneTimeToken(token: string, clientVersion: string): Promise<Session> {
        const result = await this.call('auth.loginWithOneTimeToken', {
            token,
            client_version: clientVersion,
        });

        this.sessionToken = result.session_token;
        this.playerId = result.player_id;

        console.log('[ToNRoundCloud] Session token set after login:', !!this.sessionToken, 'Player ID:', this.playerId);

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

    /**
     * Validate existing session (for reconnection)
     */
    async validateSession(sessionToken: string, playerId: string): Promise<Session> {
        const result = await this.call('auth.validateSession', {
            session_token: sessionToken,
            player_id: playerId,
        });

        this.sessionToken = result.session_token;
        this.playerId = result.player_id;

        console.log('[ToNRoundCloud] Session validated:', !!this.sessionToken, 'Player ID:', this.playerId);

        return result;
    }

    // Instance Management
    async createInstance(maxPlayers: number = 6, settings: any = {}): Promise<Instance> {
        return await this.call('instance.create', { max_players: maxPlayers, settings });
    }

    async joinInstance(instanceId: string): Promise<void> {
        return await this.call('instance.join', { instance_id: instanceId });
    }

    async leaveInstance(instanceId: string): Promise<void> {
        return await this.call('instance.leave', { instance_id: instanceId });
    }

    async listInstances(): Promise<Instance[]> {
        const result = await this.call('instance.list', {});
        // APIは { instances: [...], total, limit, offset } を返すので、instancesだけ抽出
        return result.instances || [];
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

    async getInstance(instanceId: string): Promise<Instance> {
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
        const result = await this.call('player.states.get', { instanceId });
        return result.player_states || [];
    }

    async getCurrentInstance(playerId?: string): Promise<any> {
        return await this.call('player.instance.get', { player_id: playerId });
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

    // Client Status
    async getClientStatus(playerId?: string): Promise<{
        player_id: string;
        csharp_client: { connected: boolean; player_id?: string; user_id?: string; session_id?: string; };
        web_client: { connected: boolean; player_id?: string; user_id?: string; session_id?: string; };
        timestamp: string;
    }> {
        return await this.call('client.status.get', { player_id: playerId });
    }

    // Stream Subscriptions
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
    private async call(rpc: string, params: any): Promise<any> {
        if (!this.isConnected()) {
            throw new Error('Not connected to server');
        }

        const id = `${++this.messageId}`;
        
        // セッショントークンがある場合はparamsに追加
        const enhancedParams = this.sessionToken 
            ? { ...params, session_token: this.sessionToken }
            : params;
        
        const message: WebSocketMessage = { id, rpc, params: enhancedParams };

        // デバッグログ
        console.log('[ToNRoundCloud] Sending RPC:', rpc, 'with session token:', !!this.sessionToken);

        return new Promise((resolve, reject) => {
            const timeout = setTimeout(() => {
                this.pendingRequests.delete(id);
                reject(new Error(`Request timeout: ${rpc}`));
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

            // Handle RPC response
            if (message.id && this.pendingRequests.has(message.id)) {
                const pending = this.pendingRequests.get(message.id)!;
                clearTimeout(pending.timeout);
                this.pendingRequests.delete(message.id);

                if (message.error) {
                    pending.reject(new Error(`${message.error.code}: ${message.error.message}`));
                } else {
                    pending.resolve(message.result);
                }
            }

            // Handle stream event
            if (message.stream) {
                const callbacks = this.streamCallbacks.get(message.stream);
                if (callbacks) {
                    callbacks.forEach(callback => callback(message.data));
                }
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

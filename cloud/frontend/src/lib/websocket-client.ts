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
    /** Internal DB user id — returned by validateSession, login, and loginWithOneTimeToken. */
    user_id?: string;
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
export type ConnectionStateCallback = (state: 'connected' | 'disconnected' | 'reconnecting' | 'auth-required') => void;

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
    private reconnectTimer: NodeJS.Timeout | null = null;
    private manualDisconnect = false;
    /** Set when a disconnect was triggered by an auth error (AUTH_REQUIRED etc.). */
    private authErrorDisconnect = false;
    /** Internal DB user id (ws.userId on the server) — returned by every auth RPC. */
    private _userId: string | null = null;

    get userId(): string | null {
        return this._userId;
    }

    private clearSession(): void {
        this.sessionToken = null;
        this.playerId = null;
        this._userId = null;
    }

    constructor(url: string) {
        this.url = url;
    }

    /**
     * Connect to WebSocket server
     */
    async connect(): Promise<void> {
        this.manualDisconnect = false;
        this.clearReconnectTimer();

        // Guard against double-connect: if a socket already exists (open or
        // still connecting), close it before opening a new one. Without this,
        // rapid `connect()` calls (auto-reconnect timer racing with manual
        // re-login, etc.) leave dangling WebSocket instances whose `onclose`
        // handlers later flip `connected -> disconnected` and corrupt state.
        if (this.ws) {
            try {
                // Detach handlers so the orphaned socket can't drive state.
                this.ws.onopen = null;
                this.ws.onmessage = null;
                this.ws.onerror = null;
                this.ws.onclose = null;
                if (this.ws.readyState === WebSocket.OPEN ||
                    this.ws.readyState === WebSocket.CONNECTING) {
                    this.ws.close();
                }
            } catch {
                /* ignore */
            }
            this.ws = null;
        }

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
                    this.rejectAllPendingRequests(new Error('Connection closed'));
                    if (!this.manualDisconnect) {
                        this.attemptReconnect();
                    }
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
        this.manualDisconnect = true;
        this.clearReconnectTimer();
        if (this.ws) {
            this.ws.close();
            this.ws = null;
        }
        this.rejectAllPendingRequests(new Error('Disconnected by client'));
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
            client_type: 'web',
        };

        if (accessKey) {
            params.access_key = accessKey;
        }

        const result = await this.call('auth.login', params);

        this.sessionToken = result.session_token;
        this.playerId = result.player_id;
        if (typeof result.user_id === 'string') {
            this._userId = result.user_id;
        }

        return result;
    }

    /**
     * Login with one-time token
     */
    async loginWithOneTimeToken(token: string, clientVersion: string): Promise<Session> {
        const result = await this.call('auth.loginWithOneTimeToken', {
            token,
            client_version: clientVersion,
            client_type: 'web',
        });

        this.sessionToken = result.session_token;
        this.playerId = result.player_id;
        if (typeof result.user_id === 'string') {
            this._userId = result.user_id;
        }

        console.log('[ToNRoundCloud] Session token set after login:', !!this.sessionToken, 'Player ID:', this.playerId);

        return result;
    }

    /**
     * Logout
     */
    async logout(): Promise<void> {
        await this.call('auth.logout', {});
        this.clearSession();
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
            client_type: 'web',
        });

        this.sessionToken = result.session_token;
        this.playerId = result.player_id;
        if (typeof result.user_id === 'string') {
            this._userId = result.user_id;
        }

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
    /**
     * Send a player state update for the given instance.
     *
     * Backend signature is `{ instance_id, player_state: { ... } }`. The previous
     * implementation sent `{ player_id, state, ...data }` which the server always
     * rejected with `instance_id and player_state are required`.
     */
    async updatePlayerState(instanceId: string, state: Partial<PlayerState> & Record<string, any>): Promise<void> {
        const { player_id, ...rest } = state;
        return await this.call('player.state.update', {
            instance_id: instanceId,
            player_state: {
                ...rest,
                // The server overwrites player_id with the authenticated user, but
                // include it here so older middleware/logging still sees a value.
                player_id: player_id ?? this.playerId ?? undefined,
            },
        });
    }

    async getPlayerState(instanceId: string, playerId: string): Promise<PlayerState> {
        return await this.call('player.state.get', {
            instance_id: instanceId,
            player_id: playerId,
        });
    }

    async getAllPlayerStates(instanceId: string): Promise<PlayerState[]> {
        const result = await this.call('player.states.get', { instance_id: instanceId, instanceId });
        return Array.isArray(result?.player_states) ? result.player_states : [];
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

    async respondToThreat(threatId: string, _playerId: string, decision: 'survive' | 'cancel' | 'skip' | 'execute' | 'timeout'): Promise<void> {
        // Player identity is taken from the authenticated session on the server.
        return await this.call('threat.response', {
            threat_id: threatId,
            decision,
        });
    }

    // Voting
    async startVoting(instanceId: string, terrorName: string, expiresAt: Date, roundKey?: string): Promise<any> {
        return await this.call('coordinated.voting.start', {
            instance_id: instanceId,
            terror_name: terrorName,
            expires_at: expiresAt.toISOString(),
            round_key: roundKey?.trim() || undefined,
        });
    }

    async submitVote(campaignId: string, _playerId: string, decision: 'Continue' | 'Skip' | 'Proceed' | 'Cancel'): Promise<void> {
        // The server identifies the voter from the authenticated session
        // (ws.userId), so any client-supplied player_id is ignored. Sending it
        // is pure spoofing surface, so we omit it here.
        return await this.call('coordinated.voting.vote', {
            campaign_id: campaignId,
            decision,
        });
    }

    async getVotingCampaign(campaignId: string): Promise<any> {
        return await this.call('coordinated.voting.getCampaign', { campaign_id: campaignId });
    }

    /**
     * Returns the latest still-active (PENDING, not yet expired) voting
     * campaign for an instance, or null if none. Lets the dashboard render
     * voting UI even when the user opens it after a campaign has started.
     */
    async getActiveVotingCampaign(instanceId: string): Promise<any | null> {
        const result = await this.call('coordinated.voting.getActive', { instance_id: instanceId });
        return result?.campaign ?? null;
    }

    async getVotingVotes(campaignId: string): Promise<any[]> {
        const result = await this.call('coordinated.voting.getVotes', { campaign_id: campaignId });
        return Array.isArray(result?.votes) ? result.votes : [];
    }

    async getCoordinatedAutoSuicideState(instanceId: string): Promise<any> {
        return await this.call('coordinated.autoSuicide.get', { instance_id: instanceId });
    }

    async updateCoordinatedAutoSuicideState(instanceId: string, state: any): Promise<any> {
        return await this.call('coordinated.autoSuicide.update', {
            instance_id: instanceId,
            state,
        });
    }

    // Wished Terrors
    // The backend stores per-player wishes as objects ({ id, terror_name, round_key, ... }).
    // Older revisions of this client treated entries as raw strings, which silently
    // dropped round_key matching and caused INSERT failures on the server (NOT NULL
    // terror_name) when the UI sent a list of strings. The server now normalizes
    // both shapes; we keep the rich object shape on the client so the panel UI can
    // display/edit round_key per entry.
    async updateWishedTerrors(playerId: string, wishedTerrors: Array<string | { id?: string; terror_name: string; round_key?: string }>): Promise<void> {
        return await this.call('wished.terrors.update', {
            wished_terrors: wishedTerrors,
        });
    }

    async getWishedTerrors(_playerId?: string): Promise<Array<{ id: string; terror_name: string; round_key: string; created_at?: string }>> {
        const result = await this.call('wished.terrors.get', {});
        if (!result || !Array.isArray(result.wished_terrors)) {
            return [];
        }
        return result.wished_terrors
            .map((entry: any) => {
                if (typeof entry === 'string') {
                    return { id: entry, terror_name: entry, round_key: '' };
                }
                if (entry && typeof entry === 'object' && typeof entry.terror_name === 'string') {
                    return {
                        id: typeof entry.id === 'string' ? entry.id : entry.terror_name,
                        terror_name: entry.terror_name,
                        round_key: typeof entry.round_key === 'string' ? entry.round_key : '',
                        created_at: entry.created_at,
                    };
                }
                return null;
            })
            .filter((e: any): e is { id: string; terror_name: string; round_key: string } => e !== null);
    }

    async findDesirePlayersForTerror(instanceId: string, terrorName: string, roundKey: string = ''): Promise<Array<{ player_id: string; player_name: string }>> {
        const result = await this.call('wished.terrors.findDesirePlayers', {
            instance_id: instanceId,
            terror_name: terrorName,
            round_key: roundKey,
        });
        return Array.isArray(result?.desire_players) ? result.desire_players : [];
    }

    // Profile
    async getProfile(playerId: string): Promise<any> {
        return await this.call('profile.get', {});
    }

    async updateProfile(_playerId: string, updates: {
        player_name?: string;
        skill_level?: number;
    }): Promise<any> {
        // Stats fields (terror_stats/total_rounds/total_survived) are aggregated
        // server-side from gameplay events and are not directly settable by the
        // client — sending them is rejected by the backend.
        const payload: Record<string, any> = {};
        if (typeof updates.player_name === 'string') {
            payload.player_name = updates.player_name;
        }
        if (typeof updates.skill_level === 'number') {
            payload.skill_level = updates.skill_level;
        }
        return await this.call('profile.update', payload);
    }

    // Settings
    async getSettings(userId?: string): Promise<any> {
        return await this.call('settings.get', {});
    }

    async updateSettings(userId: string, settings: any): Promise<any> {
        return await this.call('settings.update', { settings });
    }

    async syncSettings(userId: string, localSettings: any, localVersion: number): Promise<any> {
        return await this.call('settings.sync', {
            local_settings: localSettings,
            local_version: localVersion,
        });
    }

    async getSettingsHistory(userId: string, limit: number = 10): Promise<any[]> {
        const result = await this.call('settings.history', { limit });
        return Array.isArray(result?.history) ? result.history : [];
    }

    // Monitoring
    async reportStatus(instanceId: string, statusData: any): Promise<any> {
        return await this.call('monitoring.report', {
            instance_id: instanceId,
            status_data: statusData,
        });
    }

    async getMonitoringStatus(userId?: string, limit: number = 10): Promise<any> {
        return await this.call('monitoring.status', { limit });
    }

    async getMonitoringErrors(userId?: string, severity?: string, limit: number = 50): Promise<any> {
        return await this.call('monitoring.errors', { severity, limit });
    }

    // Remote Control
    // NOTE: The backend permanently disabled remote.command.* endpoints
    // (arbitrary remote command execution is a critical security risk).
    // These wrappers are kept only to surface a clear, actionable error if any
    // legacy UI still tries to call them — they no longer hit the wire.
    async createRemoteCommand(_instanceId: string, _commandType: string, _action: string, _parameters: any = {}, _priority: number = 0): Promise<any> {
        throw new Error('Remote control has been disabled for security reasons.');
    }

    async executeRemoteCommand(_commandId: string): Promise<any> {
        throw new Error('Remote control has been disabled for security reasons.');
    }

    async getRemoteCommandStatus(_commandId?: string, _instanceId?: string): Promise<any> {
        throw new Error('Remote control has been disabled for security reasons.');
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

    async getRoundTypeAnalytics(timeRange?: { start: Date; end: Date }): Promise<any> {
        return await this.call('analytics.roundTypes', {
            time_range: timeRange ? {
                start: timeRange.start.toISOString(),
                end: timeRange.end.toISOString(),
            } : undefined,
        });
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

    async listBackups(): Promise<any[]> {
        const result = await this.call('backup.list', {});
        if (Array.isArray(result)) {
            return result;
        }
        if (result && Array.isArray(result.backups)) {
            return result.backups;
        }
        return [];
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

    onCoordinatedAutoSuicideUpdated(callback: StreamCallback): () => void {
        return this.subscribe('coordinated.autoSuicide.updated', callback);
    }

    onRoundReported(callback: StreamCallback): () => void {
        return this.subscribe('round.reported', callback);
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

        const message: WebSocketMessage = { id, rpc, params };

        // デバッグログ
        console.log('[ToNRoundCloud] Sending RPC:', rpc, 'with session token:', !!this.sessionToken);

        return new Promise((resolve, reject) => {
            const timeout = setTimeout(() => {
                this.pendingRequests.delete(id);
                reject(new Error(`Request timeout: ${rpc}`));
            }, 30000);

            this.pendingRequests.set(id, { resolve, reject, timeout });

            try {
                this.ws!.send(JSON.stringify(message));
            } catch (sendError) {
                clearTimeout(timeout);
                this.pendingRequests.delete(id);
                reject(sendError);
            }
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
                if (callbacks.size === 0) {
                    this.streamCallbacks.delete(stream);
                }
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
                    // Detect auth expiry / unauthenticated errors and clear session
                    const errCode = String(message.error.code || '').toUpperCase();
                    const errMsg = String(message.error.message || '');
                    if (errCode === 'AUTH_REQUIRED' ||
                        errCode === 'UNAUTHORIZED' ||
                        errCode === 'INVALID_SESSION' ||
                        /authentication required|invalid session|unauthorized/i.test(errMsg)) {
                        console.warn('[ToNRoundCloud] Auth error detected — will attempt re-authentication on reconnect');
                        // Don't clearSession() yet — keep the token so that
                        // attemptReconnect() can try to re-validate / re-attach
                        // the session on a fresh socket. Only clear if
                        // re-validation actually fails.
                        this.authErrorDisconnect = true;
                        // Force a real socket close so the reconnect loop runs.
                        if (this.ws && this.ws.readyState === WebSocket.OPEN) {
                            try { this.ws.close(4001, 'auth-required'); } catch { /* ignore */ }
                        }
                    }
                    pending.reject(new Error(`${message.error.code}: ${message.error.message}`));
                } else {
                    pending.resolve(message.result);
                }
            }

            // Handle stream event
            if (message.stream) {
                const callbacks = this.streamCallbacks.get(message.stream);
                if (callbacks) {
                    callbacks.forEach(callback => {
                        try {
                            callback(message.data);
                        } catch (callbackError) {
                            console.error('[ToNRoundCloud] Stream callback failed:', {
                                stream: message.stream,
                                error: callbackError,
                            });
                        }
                    });
                }
            }
        } catch (error) {
            console.error('[ToNRoundCloud] Failed to parse message:', error);
        }
    }

    private startHeartbeat(): void {
        // The server (`WebSocketHandler.startHeartbeat`) drives the keep-alive
        // by sending WS PING frames every 60s, which the browser automatically
        // answers with a PONG. There is nothing for the client to do here, so
        // we no longer schedule a no-op interval that wastes a timer slot.
    }

    private stopHeartbeat(): void {
        if (this.heartbeatInterval) {
            clearInterval(this.heartbeatInterval);
            this.heartbeatInterval = null;
        }
    }

    private attemptReconnect(): void {
        // ── Auth-error path: try exactly ONE reconnect + re-validate ──
        // If the session token is still valid (e.g. server restarted but
        // tokens persist in DB), the single attempt will succeed.  If the
        // token is truly expired, we immediately force-logout instead of
        // burning through all retry attempts with a stale credential.
        if (this.authErrorDisconnect) {
            this.authErrorDisconnect = false;

            if (!this.sessionToken || !this.playerId) {
                // No credentials to re-validate — force logout immediately.
                console.warn('[ToNRoundCloud] Auth error but no stored credentials — forcing logout');
                this.forceLogout();
                return;
            }

            console.log('[ToNRoundCloud] Auth error — attempting single re-authentication...');
            this.notifyConnectionState('reconnecting');

            this.clearReconnectTimer();
            this.reconnectTimer = setTimeout(() => {
                this.connect()
                    .then(async () => {
                        try {
                            await this.validateSession(this.sessionToken!, this.playerId!);
                            console.log('[ToNRoundCloud] Session re-validated after auth error');
                            this.reconnectAttempts = 0;
                        } catch (validateErr: any) {
                            console.warn('[ToNRoundCloud] Session re-validation failed — forcing logout:', validateErr?.message || validateErr);
                            this.forceLogout();
                        }
                    })
                    .catch(error => {
                        console.error('[ToNRoundCloud] Reconnect failed during auth recovery:', error);
                        this.forceLogout();
                    });
            }, 1000); // single short delay
            return;
        }

        // ── Normal (network) reconnect with exponential backoff ──
        if (this.reconnectAttempts >= this.maxReconnectAttempts) {
            console.error('[ToNRoundCloud] Max reconnect attempts reached');
            return;
        }

        this.reconnectAttempts++;
        this.notifyConnectionState('reconnecting');

        const delay = this.reconnectDelay * Math.pow(2, this.reconnectAttempts - 1);
        console.log(`[ToNRoundCloud] Reconnecting in ${delay}ms... (attempt ${this.reconnectAttempts}/${this.maxReconnectAttempts})`);

        this.clearReconnectTimer();
        this.reconnectTimer = setTimeout(() => {
            this.connect()
                .then(async () => {
                    // After auto-reconnect, the new socket is unauthenticated.
                    // Try to re-attach the existing session silently.
                    if (this.sessionToken && this.playerId) {
                        try {
                            await this.validateSession(this.sessionToken, this.playerId);
                            console.log('[ToNRoundCloud] Session re-validated after reconnect');
                        } catch (validateErr: any) {
                            console.warn('[ToNRoundCloud] Session re-validation failed — forcing logout:', validateErr?.message || validateErr);
                            // Token is stale — don't waste remaining retries.
                            this.forceLogout();
                        }
                    }
                })
                .catch(error => {
                    console.error('[ToNRoundCloud] Reconnect failed:', error);
                });
        }, delay);
    }

    /**
     * Cleanly tear down the connection and signal the UI to redirect to login.
     * Sets manualDisconnect so the socket's onclose handler does not trigger
     * another reconnect cycle.
     */
    private forceLogout(): void {
        this.manualDisconnect = true;
        this.clearReconnectTimer();
        this.clearSession();
        this.stopHeartbeat();
        if (this.ws) {
            try {
                // Detach onclose to prevent an intermediate 'disconnected'
                // notification before we emit 'auth-required'.
                this.ws.onclose = null;
                this.ws.close();
            } catch { /* ignore */ }
            this.ws = null;
        }
        this.rejectAllPendingRequests(new Error('Session expired'));
        this.notifyConnectionState('auth-required');
    }

    private clearReconnectTimer(): void {
        if (this.reconnectTimer) {
            clearTimeout(this.reconnectTimer);
            this.reconnectTimer = null;
        }
    }

    private rejectAllPendingRequests(error: Error): void {
        this.pendingRequests.forEach(pending => {
            clearTimeout(pending.timeout);
            pending.reject(error);
        });
        this.pendingRequests.clear();
    }

    private notifyConnectionState(state: 'connected' | 'disconnected' | 'reconnecting' | 'auth-required'): void {
        this.connectionStateCallbacks.forEach(callback => {
            try {
                callback(state);
            } catch (error) {
                console.error('[ToNRoundCloud] Connection state callback failed:', error);
            }
        });
    }
}

/**
 * REST API Client for ToNRoundCounter Cloud
 * Handles HTTP requests to the backend REST API
 */

// BUG FIX #6 (HIGH): Custom error class to distinguish auth errors from other errors
export class AuthError extends Error {
    constructor(message: string, public statusCode: number) {
        super(message);
        this.name = 'AuthError';
    }
}

export interface RestClientConfig {
    baseUrl: string;
    sessionToken?: string;
}

export interface Instance {
    instance_id: string;
    member_count: number;
    max_players: number;
    created_at: string;
}

export interface InstanceDetails {
    instance_id: string;
    members: Array<{
        player_id: string;
        player_name: string;
        joined_at: string;
    }>;
    settings: any;
    created_at: string;
}

export interface Profile {
    player_id: string;
    player_name: string;
    skill_level: number;
    terror_stats: Record<string, any>;
    last_active: string;
}

export interface TerrorStat {
    terror_name: string;
    total_rounds: number;
    avg_survival_rate: number;
    difficulty: string;
}

export interface OneTimeTokenLoginResponse {
    session_id?: string;
    session_token: string;
    player_id: string;
    user_id?: string;
    expires_at: string;
}

export interface AppAuthorizationResponse {
    app_id: string;
    app_token: string;
    scopes: string[];
    redirect_uri: string;
}

export interface AppPrivilege {
    app_id: string;
    privileged_scopes: string[];
    description?: string | null;
    created_by?: string | null;
    created_at?: string | null;
    updated_at?: string | null;
}

export interface AppPrivilegeListResponse {
    app_privileges: AppPrivilege[];
    privileged_scopes: string[];
    available_scopes: string[];
}

export class RestApiClient {
    private baseUrl: string;
    private sessionToken?: string;

    constructor(config: RestClientConfig) {
        this.baseUrl = config.baseUrl.replace(/\/$/, ''); // Remove trailing slash
        this.sessionToken = config.sessionToken;
    }

    /**
     * Set session token for authenticated requests
     */
    setSessionToken(token: string): void {
        this.sessionToken = token;
    }

    /**
     * Make HTTP request
     * BUG FIX #6 (HIGH): Distinguish auth errors (401/403) from other errors.
     * Previously: All errors thrown as generic Error, no way to detect session expiration.
     * Now: Throw AuthError for 401/403, allowing callers to handle re-authentication.
     */
    private async request<T>(
        method: string,
        path: string,
        body?: any
    ): Promise<T> {
        const headers: Record<string, string> = {
            'Content-Type': 'application/json',
        };

        if (this.sessionToken) {
            headers['Authorization'] = `Bearer ${this.sessionToken}`;
        }

        const response = await fetch(`${this.baseUrl}${path}`, {
            method,
            headers,
            body: body ? JSON.stringify(body) : undefined,
        });

        if (!response.ok) {
            // Extract error details from response
            const errorData = await response.json().catch(() => ({
                error: {
                    code: 'UNKNOWN_ERROR',
                    message: response.statusText,
                },
            }));

            const errorMessage = errorData.error?.message || response.statusText || 'Request failed';

            // BUG FIX #6: Detect authentication errors (401 Unauthorized, 403 Forbidden)
            // and throw AuthError so callers can handle session expiration/refresh
            if (response.status === 401 || response.status === 403) {
                throw new AuthError(errorMessage, response.status);
            }

            // For other errors, throw generic Error with status code context
            const error = new Error(errorMessage);
            (error as any).statusCode = response.status;
            throw error;
        }

        return response.json();
    }

    // Instance Management
    async getInstances(filter: 'available' | 'active' | 'all' = 'available', limit: number = 20, offset: number = 0): Promise<{ instances: Instance[]; total: number }> {
        return this.request('GET', `/api/v1/instances?filter=${filter}&limit=${limit}&offset=${offset}`);
    }

    async getInstance(instanceId: string): Promise<InstanceDetails> {
        return this.request('GET', `/api/v1/instances/${instanceId}`);
    }

    async createInstance(maxPlayers: number = 6, settings: any = {}): Promise<{ instance_id: string; created_at: string }> {
        return this.request('POST', '/api/v1/instances', { max_players: maxPlayers, settings });
    }

    async updateInstance(instanceId: string, updates: { max_players?: number; settings?: any }): Promise<{ instance_id: string; updated_at: string }> {
        return this.request('PUT', `/api/v1/instances/${instanceId}`, updates);
    }

    async deleteInstance(instanceId: string): Promise<{ success: boolean }> {
        return this.request('DELETE', `/api/v1/instances/${instanceId}`);
    }

    // Authentication
    async loginWithOneTimeToken(token: string, clientVersion: string = '1.0.0'): Promise<OneTimeTokenLoginResponse> {
        return this.request('POST', '/api/auth/one-time-token', {
            token,
            client_version: clientVersion,
        });
    }

    async authorizeExternalApp(request: {
        app_id: string;
        redirect_uri: string;
        state?: string;
        scopes?: string[];
        scope?: string;
    }): Promise<AppAuthorizationResponse> {
        return this.request('POST', '/api/v1/app-authorizations', request);
    }

    async listAppPrivileges(): Promise<AppPrivilegeListResponse> {
        return this.request('GET', '/api/v1/admin/app-privileges');
    }

    async updateAppPrivilege(
        appId: string,
        privilegedScopes: string[],
        description?: string
    ): Promise<{ app_privilege: AppPrivilege; privileged_scopes: string[] }> {
        return this.request('PUT', `/api/v1/admin/app-privileges/${encodeURIComponent(appId)}`, {
            privileged_scopes: privilegedScopes,
            description,
        });
    }

    async deleteAppPrivilege(appId: string): Promise<{ success: boolean }> {
        return this.request('DELETE', `/api/v1/admin/app-privileges/${encodeURIComponent(appId)}`);
    }

    // Profile Management
    async getProfile(playerId: string): Promise<Profile> {
        return this.request('GET', `/api/v1/profiles/${playerId}`);
    }

    async updateProfile(playerId: string, updates: { player_name?: string }): Promise<{ player_id: string; player_name: string; updated_at: string }> {
        return this.request('PUT', `/api/v1/profiles/${playerId}`, updates);
    }

    // Statistics
    async getTerrorStats(playerId?: string): Promise<{ terror_stats: TerrorStat[] }> {
        const query = playerId ? `?player_id=${playerId}` : '';
        return this.request('GET', `/api/v1/stats/terrors${query}`);
    }

    // Health Check
    async healthCheck(): Promise<{ status: string; timestamp: string; version: string }> {
        return this.request('GET', '/health');
    }
}

/**
 * Create a new REST API client instance
 */
export function createRestClient(baseUrl: string, sessionToken?: string): RestApiClient {
    return new RestApiClient({ baseUrl, sessionToken });
}

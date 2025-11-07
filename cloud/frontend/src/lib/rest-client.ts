/**
 * REST API Client for ToNRoundCounter Cloud
 * Handles HTTP requests to the backend REST API
 */

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
            const error = await response.json().catch(() => ({
                error: {
                    code: 'UNKNOWN_ERROR',
                    message: response.statusText,
                },
            }));
            throw new Error(error.error?.message || 'Request failed');
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

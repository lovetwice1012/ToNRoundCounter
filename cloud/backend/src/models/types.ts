/**
 * Type Definitions for ToNRoundCounter Cloud
 */

// User related types
export interface User {
    user_id: string;
    username: string;
    email: string;
    password_hash: string;
    display_name?: string;
    avatar?: string;
    roles: string[];
    permissions: string[];
    status: 'ACTIVE' | 'INACTIVE' | 'SUSPENDED';
    mfa_enabled: boolean;
    last_password_change: Date;
    created_at: Date;
    last_login?: Date;
    metadata?: Record<string, any>;
}

export interface Session {
    session_id: string;
    user_id: string;
    session_token: string;
    player_id: string;
    client_version: string;
    expires_at: Date;
    created_at: Date;
    last_activity: Date;
    ip_address?: string;
    user_agent?: string;
}

// Instance related types
export interface Instance {
    instance_id: string;
    creator_id: string;
    max_players: number;
    member_count: number;
    settings: InstanceSettings;
    status: 'ACTIVE' | 'INACTIVE' | 'FULL';
    created_at: Date;
    updated_at: Date;
}

export interface InstanceSettings {
    auto_suicide_mode: 'Disabled' | 'Manual' | 'Individual' | 'Coordinated';
    voting_timeout: number;
    [key: string]: any;
}

export interface InstanceMember {
    id: number;
    instance_id: string;
    player_id: string;
    player_name: string;
    joined_at: Date;
    left_at?: Date;
    status: 'ACTIVE' | 'LEFT';
}

// Player state types
export interface PlayerState {
    id: number;
    instance_id: string;
    player_id: string;
    velocity: number;
    afk_duration: number;
    items: string[];
    damage: number;
    is_alive: boolean;
    timestamp: Date;
}

// Wished Terror types
export interface WishedTerror {
    id: string;
    player_id: string;
    terror_name: string;
    round_key: string;
    created_at: Date;
}

// Voting types
export interface VotingCampaign {
    campaign_id: string;
    instance_id: string;
    terror_name: string;
    round_key: string;
    final_decision?: 'Proceed' | 'Cancel';
    status: 'PENDING' | 'RESOLVED' | 'EXPIRED';
    created_at: Date;
    expires_at: Date;
    resolved_at?: Date;
}

export interface PlayerVote {
    id: number;
    campaign_id: string;
    player_id: string;
    decision: 'Proceed' | 'Cancel';
    voted_at: Date;
}

// Round types
export interface Round {
    round_id: string;
    instance_id: string;
    round_key: string;
    start_time: Date;
    end_time?: Date;
    status: 'ACTIVE' | 'COMPLETED' | 'CANCELLED';
    survivor_count: number;
    initial_player_count: number;
    events: RoundEvent[];
    metadata?: Record<string, any>;
    created_at: Date;
}

export interface RoundEvent {
    type: string;
    timestamp: Date;
    data: any;
}

export interface TerrorAppearance {
    id: number;
    round_id: string;
    terror_name: string;
    appearance_time: Date;
    desire_players: DesirePlayer[];
    responses: TerrorResponse[];
    created_at: Date;
}

export interface DesirePlayer {
    player_id: string;
    player_name: string;
}

export interface TerrorResponse {
    player_id: string;
    decision: 'survive' | 'cancel' | 'skip' | 'execute' | 'timeout';
    timestamp: Date;
}

// Settings types
export interface Settings {
    settings_id: string;
    user_id: string;
    version: number;
    categories: SettingsCategories;
    last_modified: Date;
}

export interface SettingsCategories {
    general?: {
        language?: string;
        theme?: string;
        notifications?: boolean;
        [key: string]: any;
    };
    autoSuicide?: {
        enabled?: boolean;
        rules?: AutoSuicideRule[];
        [key: string]: any;
    };
    recording?: {
        autoRecord?: boolean;
        format?: string;
        quality?: number;
        [key: string]: any;
    };
    [key: string]: any;
}

export interface AutoSuicideRule {
    ruleId: string;
    conditions: Record<string, any>;
    actions: Record<string, any>;
}

// Monitoring types
export interface StatusMonitoring {
    status_id: string;
    user_id: string;
    instance_id?: string;
    application_status: 'RUNNING' | 'STOPPED' | 'ERROR';
    application_version?: string;
    uptime: number;
    memory_usage: number;
    cpu_usage: number;
    osc_status?: 'CONNECTED' | 'DISCONNECTED' | 'ERROR';
    osc_latency?: number;
    vrchat_status?: 'CONNECTED' | 'DISCONNECTED' | 'ERROR';
    vrchat_world_id?: string;
    vrchat_instance_id?: string;
    timestamp: Date;
}

export interface ErrorLog {
    error_id: string;
    user_id?: string;
    instance_id?: string;
    severity: 'INFO' | 'WARNING' | 'ERROR' | 'CRITICAL';
    message: string;
    stack?: string;
    context?: Record<string, any>;
    timestamp: Date;
    acknowledged: boolean;
}

// Backup types
export interface Backup {
    backup_id: string;
    user_id: string;
    type: 'FULL' | 'DIFFERENTIAL' | 'INCREMENTAL';
    creator: string;
    contents: BackupContents;
    metadata: BackupMetadata;
    file_path: string;
    size: number;
    checksum: string;
    compression_type: string;
    encrypted: boolean;
    status: 'PENDING' | 'IN_PROGRESS' | 'COMPLETED' | 'FAILED';
    timestamp: Date;
}

export interface BackupContents {
    settings: {
        included: boolean;
        size: number;
    };
    roundData: {
        included: boolean;
        size: number;
    };
    systemConfig: {
        included: boolean;
        size: number;
    };
}

export interface BackupMetadata {
    version: string;
    checksum: string;
    compression_type: string;
    [key: string]: any;
}

// Player Profile types
export interface PlayerProfile {
    player_id: string;
    player_name: string;
    skill_level: number;
    terror_stats: Record<string, TerrorStats>;
    total_rounds: number;
    total_survived: number;
    last_active: Date;
    created_at: Date;
}

export interface TerrorStats {
    survival_rate: number;
    total_rounds: number;
    survived: number;
    died: number;
}

// Remote Control types
export interface RemoteCommand {
    command_id: string;
    user_id: string;
    instance_id?: string;
    command_type: 'ROUND_CONTROL' | 'SETTINGS_CHANGE' | 'EMERGENCY_STOP';
    action: string;
    parameters: Record<string, any>;
    status: 'PENDING' | 'EXECUTING' | 'COMPLETED' | 'FAILED';
    result?: any;
    error?: string;
    initiator: string;
    priority: number;
    created_at: Date;
    executed_at?: Date;
    completed_at?: Date;
}

// Event Notification types
export interface EventNotification {
    event_id: string;
    event_type: 'ROUND_START' | 'ROUND_END' | 'TERROR_APPEAR' | 'ERROR' | 'WARNING';
    severity: 'INFO' | 'WARNING' | 'ERROR' | 'CRITICAL';
    source: string;
    message: string;
    details?: any;
    context?: Record<string, any>;
    category?: string;
    tags: string[];
    delivered: boolean;
    timestamp: Date;
}

// WebSocket message types
export interface WebSocketMessage {
    id?: string;
    rpc?: string;
    stream?: string;
    params?: any;
    data?: any;
    result?: any;
    error?: WebSocketError;
    timestamp?: string;
}

export interface WebSocketError {
    code: string;
    message: string;
    details?: any;
}

// API Error types
export interface ApiError {
    code: string;
    message: string;
    details?: any;
}

export const ErrorCodes = {
    AUTH_REQUIRED: 'AUTH_REQUIRED',
    AUTH_EXPIRED: 'AUTH_EXPIRED',
    INVALID_TOKEN: 'INVALID_TOKEN',
    PERMISSION_DENIED: 'PERMISSION_DENIED',
    NOT_FOUND: 'NOT_FOUND',
    INSTANCE_FULL: 'INSTANCE_FULL',
    ALREADY_JOINED: 'ALREADY_JOINED',
    INVALID_PARAMS: 'INVALID_PARAMS',
    RATE_LIMIT_EXCEEDED: 'RATE_LIMIT_EXCEEDED',
    INTERNAL_ERROR: 'INTERNAL_ERROR',
} as const;

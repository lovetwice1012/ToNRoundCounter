const fs = require('fs');
const path = require('path');

const schemaContent = `/**
 * Database Schema Definitions
 * ToNRoundCounter Cloud Backend - MariaDB
 */

export const createTables = \`
-- Users Table
CREATE TABLE IF NOT EXISTS users (
    user_id VARCHAR(255) PRIMARY KEY,
    username VARCHAR(255) UNIQUE NOT NULL,
    email VARCHAR(255) UNIQUE NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    display_name VARCHAR(255),
    avatar TEXT,
    roles JSON NOT NULL,
    permissions JSON NOT NULL,
    status VARCHAR(50) NOT NULL DEFAULT 'ACTIVE',
    mfa_enabled BOOLEAN NOT NULL DEFAULT FALSE,
    last_password_change TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    last_login TIMESTAMP NULL,
    metadata JSON,
    INDEX idx_username (username),
    INDEX idx_email (email),
    INDEX idx_status (status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Sessions Table
CREATE TABLE IF NOT EXISTS sessions (
    session_id VARCHAR(255) PRIMARY KEY,
    user_id VARCHAR(255) NOT NULL,
    session_token VARCHAR(255) UNIQUE NOT NULL,
    player_id VARCHAR(255) NOT NULL,
    client_version VARCHAR(50) NOT NULL,
    expires_at TIMESTAMP NOT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    last_activity TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    ip_address VARCHAR(50),
    user_agent TEXT,
    INDEX idx_sessions_user_id (user_id),
    INDEX idx_sessions_token (session_token),
    INDEX idx_sessions_expires (expires_at),
    INDEX idx_sessions_player_id (player_id),
    FOREIGN KEY (user_id) REFERENCES users(user_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Instances Table
CREATE TABLE IF NOT EXISTS instances (
    instance_id VARCHAR(255) PRIMARY KEY,
    creator_id VARCHAR(255) NOT NULL,
    max_players INT NOT NULL DEFAULT 6,
    member_count INT NOT NULL DEFAULT 0,
    settings JSON NOT NULL,
    status VARCHAR(50) NOT NULL DEFAULT 'ACTIVE',
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX idx_instances_status (status),
    INDEX idx_instances_creator (creator_id),
    FOREIGN KEY (creator_id) REFERENCES users(user_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Instance Members Table
CREATE TABLE IF NOT EXISTS instance_members (
    id INT AUTO_INCREMENT PRIMARY KEY,
    instance_id VARCHAR(255) NOT NULL,
    player_id VARCHAR(255) NOT NULL,
    player_name VARCHAR(255) NOT NULL,
    joined_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    left_at TIMESTAMP NULL,
    status VARCHAR(50) NOT NULL DEFAULT 'ACTIVE',
    INDEX idx_instance_members_instance (instance_id),
    INDEX idx_instance_members_player (player_id),
    INDEX idx_instance_members_status (status),
    UNIQUE KEY unique_member (instance_id, player_id),
    FOREIGN KEY (instance_id) REFERENCES instances(instance_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Player States Table
CREATE TABLE IF NOT EXISTS player_states (
    id INT AUTO_INCREMENT PRIMARY KEY,
    instance_id VARCHAR(255) NOT NULL,
    player_id VARCHAR(255) NOT NULL,
    velocity FLOAT NOT NULL DEFAULT 0,
    afk_duration INT NOT NULL DEFAULT 0,
    items JSON NOT NULL,
    damage INT NOT NULL DEFAULT 0,
    is_alive BOOLEAN NOT NULL DEFAULT TRUE,
    timestamp TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_player_states_instance (instance_id),
    INDEX idx_player_states_player (player_id),
    INDEX idx_player_states_timestamp (timestamp),
    FOREIGN KEY (instance_id) REFERENCES instances(instance_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Wished Terrors Table
CREATE TABLE IF NOT EXISTS wished_terrors (
    id VARCHAR(255) PRIMARY KEY,
    player_id VARCHAR(255) NOT NULL,
    terror_name VARCHAR(255) NOT NULL,
    round_key VARCHAR(255) NOT NULL DEFAULT '',
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_wished_terrors_player (player_id),
    INDEX idx_wished_terrors_name (terror_name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Voting Campaigns Table
CREATE TABLE IF NOT EXISTS voting_campaigns (
    campaign_id VARCHAR(255) PRIMARY KEY,
    instance_id VARCHAR(255) NOT NULL,
    terror_name VARCHAR(255) NOT NULL,
    round_key VARCHAR(255) NOT NULL,
    final_decision VARCHAR(50),
    status VARCHAR(50) NOT NULL DEFAULT 'PENDING',
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    expires_at TIMESTAMP NOT NULL,
    resolved_at TIMESTAMP NULL,
    INDEX idx_voting_campaigns_instance (instance_id),
    INDEX idx_voting_campaigns_status (status),
    INDEX idx_voting_campaigns_expires (expires_at),
    FOREIGN KEY (instance_id) REFERENCES instances(instance_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Player Votes Table
CREATE TABLE IF NOT EXISTS player_votes (
    id INT AUTO_INCREMENT PRIMARY KEY,
    campaign_id VARCHAR(255) NOT NULL,
    player_id VARCHAR(255) NOT NULL,
    decision VARCHAR(50) NOT NULL,
    voted_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_player_votes_campaign (campaign_id),
    INDEX idx_player_votes_player (player_id),
    UNIQUE KEY unique_vote (campaign_id, player_id),
    FOREIGN KEY (campaign_id) REFERENCES voting_campaigns(campaign_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Rounds Table
CREATE TABLE IF NOT EXISTS rounds (
    round_id VARCHAR(255) PRIMARY KEY,
    instance_id VARCHAR(255) NOT NULL,
    round_key VARCHAR(255) NOT NULL,
    start_time TIMESTAMP NOT NULL,
    end_time TIMESTAMP NULL,
    status VARCHAR(50) NOT NULL DEFAULT 'ACTIVE',
    survivor_count INT NOT NULL DEFAULT 0,
    initial_player_count INT NOT NULL DEFAULT 0,
    events JSON NOT NULL,
    metadata JSON,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_rounds_instance (instance_id),
    INDEX idx_rounds_status (status),
    INDEX idx_rounds_start_time (start_time),
    FOREIGN KEY (instance_id) REFERENCES instances(instance_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Terror Appearances Table
CREATE TABLE IF NOT EXISTS terror_appearances (
    id INT AUTO_INCREMENT PRIMARY KEY,
    round_id VARCHAR(255) NOT NULL,
    terror_name VARCHAR(255) NOT NULL,
    appearance_time TIMESTAMP NOT NULL,
    desire_players JSON NOT NULL,
    responses JSON NOT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_terror_appearances_round (round_id),
    INDEX idx_terror_appearances_name (terror_name),
    INDEX idx_terror_appearances_time (appearance_time),
    FOREIGN KEY (round_id) REFERENCES rounds(round_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Settings Table
CREATE TABLE IF NOT EXISTS settings (
    settings_id VARCHAR(255) PRIMARY KEY,
    user_id VARCHAR(255) NOT NULL,
    version INT NOT NULL DEFAULT 1,
    categories JSON NOT NULL,
    last_modified TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX idx_settings_user (user_id),
    INDEX idx_settings_version (version),
    FOREIGN KEY (user_id) REFERENCES users(user_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Status Monitoring Table
CREATE TABLE IF NOT EXISTS status_monitoring (
    status_id VARCHAR(255) PRIMARY KEY,
    user_id VARCHAR(255) NOT NULL,
    instance_id VARCHAR(255),
    application_status VARCHAR(50) NOT NULL,
    application_version VARCHAR(50),
    uptime INT NOT NULL DEFAULT 0,
    memory_usage BIGINT NOT NULL DEFAULT 0,
    cpu_usage FLOAT NOT NULL DEFAULT 0,
    osc_status VARCHAR(50),
    osc_latency INT,
    vrchat_status VARCHAR(50),
    vrchat_world_id VARCHAR(255),
    vrchat_instance_id VARCHAR(255),
    timestamp TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_status_monitoring_user (user_id),
    INDEX idx_status_monitoring_instance (instance_id),
    INDEX idx_status_monitoring_timestamp (timestamp),
    FOREIGN KEY (user_id) REFERENCES users(user_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Error Logs Table
CREATE TABLE IF NOT EXISTS error_logs (
    error_id VARCHAR(255) PRIMARY KEY,
    user_id VARCHAR(255),
    instance_id VARCHAR(255),
    severity VARCHAR(50) NOT NULL,
    message TEXT NOT NULL,
    stack TEXT,
    context JSON,
    timestamp TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    acknowledged BOOLEAN NOT NULL DEFAULT FALSE,
    INDEX idx_error_logs_user (user_id),
    INDEX idx_error_logs_severity (severity),
    INDEX idx_error_logs_timestamp (timestamp),
    INDEX idx_error_logs_acknowledged (acknowledged),
    FOREIGN KEY (user_id) REFERENCES users(user_id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Backups Table
CREATE TABLE IF NOT EXISTS backups (
    backup_id VARCHAR(255) PRIMARY KEY,
    user_id VARCHAR(255) NOT NULL,
    type VARCHAR(50) NOT NULL,
    creator VARCHAR(255) NOT NULL,
    contents JSON NOT NULL,
    metadata JSON NOT NULL,
    file_path VARCHAR(500) NOT NULL,
    size BIGINT NOT NULL,
    checksum VARCHAR(255) NOT NULL,
    compression_type VARCHAR(50) NOT NULL DEFAULT 'gzip',
    encrypted BOOLEAN NOT NULL DEFAULT FALSE,
    status VARCHAR(50) NOT NULL DEFAULT 'COMPLETED',
    timestamp TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_backups_user (user_id),
    INDEX idx_backups_type (type),
    INDEX idx_backups_status (status),
    INDEX idx_backups_timestamp (timestamp),
    FOREIGN KEY (user_id) REFERENCES users(user_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Player Profiles Table
CREATE TABLE IF NOT EXISTS player_profiles (
    player_id VARCHAR(255) PRIMARY KEY,
    player_name VARCHAR(255) NOT NULL,
    skill_level FLOAT NOT NULL DEFAULT 0,
    terror_stats JSON NOT NULL,
    total_rounds INT NOT NULL DEFAULT 0,
    total_survived INT NOT NULL DEFAULT 0,
    last_active TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_player_profiles_name (player_name),
    INDEX idx_player_profiles_last_active (last_active)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Remote Control Commands Table
CREATE TABLE IF NOT EXISTS remote_commands (
    command_id VARCHAR(255) PRIMARY KEY,
    user_id VARCHAR(255) NOT NULL,
    instance_id VARCHAR(255),
    command_type VARCHAR(50) NOT NULL,
    action VARCHAR(255) NOT NULL,
    parameters JSON NOT NULL,
    status VARCHAR(50) NOT NULL DEFAULT 'PENDING',
    result TEXT,
    error TEXT,
    initiator VARCHAR(255) NOT NULL,
    priority INT NOT NULL DEFAULT 0,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    executed_at TIMESTAMP NULL,
    completed_at TIMESTAMP NULL,
    INDEX idx_remote_commands_user (user_id),
    INDEX idx_remote_commands_instance (instance_id),
    INDEX idx_remote_commands_status (status),
    INDEX idx_remote_commands_priority (priority),
    INDEX idx_remote_commands_created (created_at),
    FOREIGN KEY (user_id) REFERENCES users(user_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Event Notifications Table
CREATE TABLE IF NOT EXISTS event_notifications (
    event_id VARCHAR(255) PRIMARY KEY,
    event_type VARCHAR(50) NOT NULL,
    severity VARCHAR(50) NOT NULL,
    source VARCHAR(255) NOT NULL,
    message TEXT NOT NULL,
    details TEXT,
    context JSON,
    category VARCHAR(50),
    tags JSON NOT NULL,
    delivered BOOLEAN NOT NULL DEFAULT FALSE,
    timestamp TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_event_notifications_type (event_type),
    INDEX idx_event_notifications_severity (severity),
    INDEX idx_event_notifications_delivered (delivered),
    INDEX idx_event_notifications_timestamp (timestamp)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
\`;
`;

const schemaPath = path.join(__dirname, 'src', 'database', 'schema.ts');
fs.writeFileSync(schemaPath, schemaContent, 'utf8');
console.log('Schema file created successfully at:', schemaPath);

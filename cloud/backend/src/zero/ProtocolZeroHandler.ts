/**
 * Protocol ZERO Backend Handler
 * Hybrid binary protocol: Binary headers + MessagePack payloads
 */

import { WebSocket } from 'ws';
import { encode as msgpackEncode, decode as msgpackDecode } from '@msgpack/msgpack';
import { logger } from '../logger';

// ============================================================================
// Opcodes
// ============================================================================

enum Opcode {
  // Auth
  Login = 0x01,
  Logout = 0x02,
  RefreshSession = 0x03,

  // Round
  RoundStart = 0x10,
  RoundEnd = 0x11,

  // Instance
  InstanceCreate = 0x20,
  InstanceList = 0x21,
  InstanceGet = 0x22,
  InstanceUpdate = 0x23,
  InstanceDelete = 0x24,
  InstanceAlert = 0x25,

  // Player State
  UpdatePlayerState = 0x30,
  GetPlayerState = 0x31,
  GetAllPlayerStates = 0x32,

  // Threat
  AnnounceThreat = 0x40,
  RecordThreatResponse = 0x41,
  FindDesirePlayers = 0x42,

  // Voting
  StartVoting = 0x50,
  SubmitVote = 0x51,
  GetVotingCampaign = 0x52,

  // Profile
  GetProfile = 0x60,
  UpdateProfile = 0x61,

  // Settings
  GetSettings = 0x70,
  UpdateSettings = 0x71,
  SyncSettings = 0x72,

  // Monitoring
  ReportMonitoringStatus = 0x80,
  GetMonitoringHistory = 0x81,
  GetMonitoringErrors = 0x82,
  LogError = 0x83,

  // Analytics
  GetPlayerAnalytics = 0x90,
  GetTerrorAnalytics = 0x91,
  GetInstanceAnalytics = 0x92,
  GetVotingAnalytics = 0x93,
  ExportAnalytics = 0x94,

  // Backup
  CreateBackup = 0xA0,
  RestoreBackup = 0xA1,
  ListBackups = 0xA2,

  // Wished Terrors
  UpdateWishedTerrors = 0xB0,
  GetWishedTerrors = 0xB1,

  // Response
  Success = 0xC0,
  Error = 0xFF,

  // Stream Events
  PlayerStateEvent = 0xD0,
  RoundStartedEvent = 0xD1,
  RoundEndedEvent = 0xD2,
  VotingStartedEvent = 0xD3,
  VotingUpdatedEvent = 0xD4,
  VotingResolvedEvent = 0xD5,
  SettingsUpdatedEvent = 0xD6,

  // Control
  Ping = 0xFE,
  Pong = 0xFD,
}

// ============================================================================
// Connection State
// ============================================================================

interface ConnectionState {
  sessionId?: string;
  userId?: string;
  playerId?: string;
  instanceId?: string;
  lastActivity: number;
}

// ============================================================================
// Protocol ZERO Handler
// ============================================================================

export class ProtocolZeroHandler {
  private ws: WebSocket;
  private state: ConnectionState;
  private sessions = new Map<string, { playerId: string; expiresAt: number }>();
  private instances = new Map<string, Set<WebSocket>>();

  constructor(ws: WebSocket) {
    this.ws = ws;
    this.state = { lastActivity: Date.now() };
    this.setupHandlers();
  }

  private setupHandlers(): void {
    this.ws.on('message', (data: Buffer) => {
      this.handleMessage(data).catch((error) => {
        logger.error({ error }, 'Protocol ZERO message handling error');
      });
    });

    this.ws.on('close', () => {
      this.cleanup();
    });
  }

  private async handleMessage(data: Buffer): Promise<void> {
    this.state.lastActivity = Date.now();

    if (data.length < 4) {
      logger.warn('Message too short (< 4 bytes)');
      return;
    }

    const opcode = data[0] as Opcode;
    const reqId = data[1];
    const payloadLen = (data[2] << 8) | data[3];
    const payload = data.subarray(4, 4 + payloadLen);

    if (payload.length !== payloadLen) {
      logger.warn({ expected: payloadLen, actual: payload.length }, 'Payload length mismatch');
      return;
    }

    try {
      let request: any;
      if (payload.length > 0) {
        request = msgpackDecode(payload);
      }

      switch (opcode) {
        // Auth
        case Opcode.Login:
          await this.handleLogin(reqId, request);
          break;
        case Opcode.Logout:
          await this.handleLogout(reqId, request);
          break;
        case Opcode.RefreshSession:
          await this.handleRefreshSession(reqId, request);
          break;

        // Round
        case Opcode.RoundStart:
          await this.handleRoundStart(reqId, request);
          break;
        case Opcode.RoundEnd:
          await this.handleRoundEnd(reqId, request);
          break;

        // Instance
        case Opcode.InstanceCreate:
          await this.handleInstanceCreate(reqId, request);
          break;
        case Opcode.InstanceList:
          await this.handleInstanceList(reqId, request);
          break;
        case Opcode.InstanceGet:
          await this.handleInstanceGet(reqId, request);
          break;
        case Opcode.InstanceUpdate:
          await this.handleInstanceUpdate(reqId, request);
          break;
        case Opcode.InstanceDelete:
          await this.handleInstanceDelete(reqId, request);
          break;
        case Opcode.InstanceAlert:
          await this.handleInstanceAlert(reqId, request);
          break;

        // Player State
        case Opcode.UpdatePlayerState:
          await this.handleUpdatePlayerState(request);
          break;
        case Opcode.GetPlayerState:
          await this.handleGetPlayerState(reqId, request);
          break;
        case Opcode.GetAllPlayerStates:
          await this.handleGetAllPlayerStates(reqId, request);
          break;

        // Threat
        case Opcode.AnnounceThreat:
          await this.handleAnnounceThreat(reqId, request);
          break;
        case Opcode.RecordThreatResponse:
          await this.handleRecordThreatResponse(reqId, request);
          break;
        case Opcode.FindDesirePlayers:
          await this.handleFindDesirePlayers(reqId, request);
          break;

        // Voting
        case Opcode.StartVoting:
          await this.handleStartVoting(reqId, request);
          break;
        case Opcode.SubmitVote:
          await this.handleSubmitVote(reqId, request);
          break;
        case Opcode.GetVotingCampaign:
          await this.handleGetVotingCampaign(reqId, request);
          break;

        // Profile
        case Opcode.GetProfile:
          await this.handleGetProfile(reqId, request);
          break;
        case Opcode.UpdateProfile:
          await this.handleUpdateProfile(reqId, request);
          break;

        // Settings
        case Opcode.GetSettings:
          await this.handleGetSettings(reqId, request);
          break;
        case Opcode.UpdateSettings:
          await this.handleUpdateSettings(reqId, request);
          break;
        case Opcode.SyncSettings:
          await this.handleSyncSettings(reqId, request);
          break;

        // Monitoring
        case Opcode.ReportMonitoringStatus:
          await this.handleReportMonitoringStatus(reqId, request);
          break;
        case Opcode.GetMonitoringHistory:
          await this.handleGetMonitoringHistory(reqId, request);
          break;
        case Opcode.GetMonitoringErrors:
          await this.handleGetMonitoringErrors(reqId, request);
          break;
        case Opcode.LogError:
          await this.handleLogError(reqId, request);
          break;

        // Analytics
        case Opcode.GetPlayerAnalytics:
          await this.handleGetPlayerAnalytics(reqId, request);
          break;
        case Opcode.GetTerrorAnalytics:
          await this.handleGetTerrorAnalytics(reqId, request);
          break;
        case Opcode.GetInstanceAnalytics:
          await this.handleGetInstanceAnalytics(reqId, request);
          break;
        case Opcode.GetVotingAnalytics:
          await this.handleGetVotingAnalytics(reqId, request);
          break;
        case Opcode.ExportAnalytics:
          await this.handleExportAnalytics(reqId, request);
          break;

        // Backup
        case Opcode.CreateBackup:
          await this.handleCreateBackup(reqId, request);
          break;
        case Opcode.RestoreBackup:
          await this.handleRestoreBackup(reqId, request);
          break;
        case Opcode.ListBackups:
          await this.handleListBackups(reqId, request);
          break;

        // Wished Terrors
        case Opcode.UpdateWishedTerrors:
          await this.handleUpdateWishedTerrors(reqId, request);
          break;
        case Opcode.GetWishedTerrors:
          await this.handleGetWishedTerrors(reqId, request);
          break;

        // Control
        case Opcode.Ping:
          this.sendPong();
          break;

        default:
          logger.warn({ opcode }, 'Unknown opcode');
          this.sendError(reqId, 'Unknown opcode');
      }
    } catch (error: any) {
      logger.error({ error, opcode }, 'Handler error');
      this.sendError(reqId, error.message || 'Internal error');
    }
  }

  // ==========================================================================
  // Auth Handlers
  // ==========================================================================

  private async handleLogin(reqId: number, request: any): Promise<void> {
    const sessionId = this.generateId(16);
    const expiresAt = Date.now() + 24 * 60 * 60 * 1000;

    this.sessions.set(sessionId, { playerId: request.player_id, expiresAt });
    this.state.sessionId = sessionId;
    this.state.playerId = request.player_id;

    logger.info({ playerId: request.player_id, sessionId }, 'Login successful');

    this.sendSuccess(reqId, {
      session_id: sessionId,
      expires_at: expiresAt,
      player_id: request.player_id
    });
  }

  private async handleLogout(reqId: number, request: any): Promise<void> {
    if (this.state.sessionId) {
      this.sessions.delete(this.state.sessionId);
    }
    this.sendSuccess(reqId, { ok: true });
  }

  private async handleRefreshSession(reqId: number, request: any): Promise<void> {
    const expiresAt = Date.now() + 24 * 60 * 60 * 1000;
    this.sendSuccess(reqId, { expires_at: expiresAt });
  }

  // ==========================================================================
  // Round Handlers
  // ==========================================================================

  private async handleRoundStart(reqId: number, request: any): Promise<void> {
    const roundId = this.generateId(16);
    const startedAt = Date.now();

    logger.info({ instanceId: request.instance_id, roundType: request.terror_name, roundId }, 'Round started');

    this.sendSuccess(reqId, {
      round_id: roundId,
      started_at: startedAt
    });
  }

  private async handleRoundEnd(reqId: number, request: any): Promise<void> {
    const endedAt = Date.now();

    logger.info({ roundId: request.round_id, survived: request.survived }, 'Round ended');

    this.sendSuccess(reqId, {
      ended_at: endedAt,
      round_id: request.round_id,
      survived: request.survived
    });
  }

  // ==========================================================================
  // Instance Handlers
  // ==========================================================================

  private async handleInstanceCreate(reqId: number, request: any): Promise<void> {
    const instanceId = this.generateId(16);
    const createdAt = Date.now();

    this.instances.set(instanceId, new Set());

    this.sendSuccess(reqId, {
      instance_id: instanceId,
      created_at: createdAt,
      max_players: request.max_players,
      current_players: 0
    });
  }

  private async handleInstanceList(reqId: number, request: any): Promise<void> {
    const instances = Array.from(this.instances.keys()).slice(request.offset || 0, (request.offset || 0) + (request.limit || 20));

    this.sendSuccess(reqId, {
      instances: instances.map(iid => ({
        instance_id: iid,
        current_players: this.instances.get(iid)?.size || 0,
        max_players: 10
      })),
      total: this.instances.size
    });
  }

  private async handleInstanceGet(reqId: number, request: any): Promise<void> {
    const instanceId = request.instance_id;
    const clients = this.instances.get(instanceId);

    this.sendSuccess(reqId, {
      instance_id: instanceId,
      current_players: clients?.size || 0,
      max_players: 10,
      settings: {},
      created_at: Date.now(),
      updated_at: Date.now()
    });
  }

  private async handleInstanceUpdate(reqId: number, request: any): Promise<void> {
    this.sendSuccess(reqId, {
      instance_id: request.instance_id,
      updated_at: Date.now()
    });
  }

  private async handleInstanceDelete(reqId: number, request: any): Promise<void> {
    const instanceId = request.instance_id;
    this.instances.delete(instanceId);

    this.sendSuccess(reqId, { ok: true });
  }

  private async handleInstanceAlert(reqId: number, request: any): Promise<void> {
    this.sendSuccess(reqId, {
      alert_id: this.generateId(16),
      sent_at: Date.now()
    });
  }

  // ==========================================================================
  // Player State Handlers
  // ==========================================================================

  private async handleUpdatePlayerState(request: any): Promise<void> {
    // Fire-and-forget, no response
    this.broadcastPlayerState(
      request.instance_id,
      request.player_id,
      request.velocity,
      request.afk_duration,
      request.damage,
      request.is_alive
    );
  }

  private async handleGetPlayerState(reqId: number, request: any): Promise<void> {
    this.sendSuccess(reqId, {
      player_id: request.player_id,
      velocity: 0,
      afk_duration: 0,
      damage: 0,
      is_alive: true,
      last_update: Date.now()
    });
  }

  private async handleGetAllPlayerStates(reqId: number, request: any): Promise<void> {
    this.sendSuccess(reqId, {
      states: []
    });
  }

  // ==========================================================================
  // Threat Handlers
  // ==========================================================================

  private async handleAnnounceThreat(reqId: number, request: any): Promise<void> {
    logger.info({ instanceId: request.instance_id, terrorName: request.terror_name }, 'Threat announced');
    this.sendSuccess(reqId, { ok: true });
  }

  private async handleRecordThreatResponse(reqId: number, request: any): Promise<void> {
    this.sendSuccess(reqId, { ok: true });
  }

  private async handleFindDesirePlayers(reqId: number, request: any): Promise<void> {
    this.sendSuccess(reqId, {
      players: []
    });
  }

  // ==========================================================================
  // Voting Handlers
  // ==========================================================================

  private async handleStartVoting(reqId: number, request: any): Promise<void> {
    const campaignId = this.generateId(16);
    const expiresAt = Date.now() + (request.timeout * 1000);

    this.sendSuccess(reqId, {
      campaign_id: campaignId,
      expires_at: expiresAt
    });
  }

  private async handleSubmitVote(reqId: number, request: any): Promise<void> {
    this.sendSuccess(reqId, { ok: true });
  }

  private async handleGetVotingCampaign(reqId: number, request: any): Promise<void> {
    this.sendSuccess(reqId, {
      campaign_id: request.campaign_id,
      status: 'active',
      votes: { proceed: 0, cancel: 0 }
    });
  }

  // ==========================================================================
  // Profile Handlers
  // ==========================================================================

  private async handleGetProfile(reqId: number, request: any): Promise<void> {
    this.sendSuccess(reqId, {
      player_id: request.player_id,
      player_name: 'Player',
      skill_level: 1,
      total_rounds: 0,
      total_survived: 0,
      terror_stats: {}
    });
  }

  private async handleUpdateProfile(reqId: number, request: any): Promise<void> {
    this.sendSuccess(reqId, {
      ok: true,
      updated_at: Date.now()
    });
  }

  // ==========================================================================
  // Settings Handlers
  // ==========================================================================

  private async handleGetSettings(reqId: number, request: any): Promise<void> {
    this.sendSuccess(reqId, {
      settings: {},
      version: 1,
      updated_at: Date.now()
    });
  }

  private async handleUpdateSettings(reqId: number, request: any): Promise<void> {
    this.sendSuccess(reqId, {
      version: 2,
      updated_at: Date.now()
    });
  }

  private async handleSyncSettings(reqId: number, request: any): Promise<void> {
    this.sendSuccess(reqId, {
      action: 'none',
      settings: request.local_settings,
      version: request.local_version
    });
  }

  // ==========================================================================
  // Monitoring Handlers
  // ==========================================================================

  private async handleReportMonitoringStatus(reqId: number, request: any): Promise<void> {
    this.sendSuccess(reqId, { ok: true });
  }

  private async handleGetMonitoringHistory(reqId: number, request: any): Promise<void> {
    this.sendSuccess(reqId, {
      history: []
    });
  }

  private async handleGetMonitoringErrors(reqId: number, request: any): Promise<void> {
    this.sendSuccess(reqId, {
      errors: []
    });
  }

  private async handleLogError(reqId: number, request: any): Promise<void> {
    logger.error({ source: request.source, message: request.message }, 'Client error logged');
    this.sendSuccess(reqId, { ok: true });
  }

  // ==========================================================================
  // Analytics Handlers
  // ==========================================================================

  private async handleGetPlayerAnalytics(reqId: number, request: any): Promise<void> {
    this.sendSuccess(reqId, {
      player_id: request.player_id,
      total_rounds: 0,
      total_survived: 0,
      avg_survival_time: 0,
      terror_stats: {}
    });
  }

  private async handleGetTerrorAnalytics(reqId: number, request: any): Promise<void> {
    this.sendSuccess(reqId, {
      terror_name: request.terror_name,
      total_encounters: 0,
      total_survived: 0,
      avg_damage: 0
    });
  }

  private async handleGetInstanceAnalytics(reqId: number, request: any): Promise<void> {
    this.sendSuccess(reqId, {
      instance_id: request.instance_id,
      total_rounds: 0,
      avg_players: 0,
      uptime: 0
    });
  }

  private async handleGetVotingAnalytics(reqId: number, request: any): Promise<void> {
    this.sendSuccess(reqId, {
      total_campaigns: 0,
      total_votes: 0,
      proceed_rate: 0
    });
  }

  private async handleExportAnalytics(reqId: number, request: any): Promise<void> {
    this.sendSuccess(reqId, {
      export_id: this.generateId(16),
      status: 'pending'
    });
  }

  // ==========================================================================
  // Backup Handlers
  // ==========================================================================

  private async handleCreateBackup(reqId: number, request: any): Promise<void> {
    const backupId = this.generateId(16);

    this.sendSuccess(reqId, {
      backup_id: backupId,
      created_at: Date.now(),
      backup_type: request.backup_type
    });
  }

  private async handleRestoreBackup(reqId: number, request: any): Promise<void> {
    this.sendSuccess(reqId, {
      ok: true,
      restored_at: Date.now()
    });
  }

  private async handleListBackups(reqId: number, request: any): Promise<void> {
    this.sendSuccess(reqId, {
      backups: []
    });
  }

  // ==========================================================================
  // Wished Terrors Handlers
  // ==========================================================================

  private async handleUpdateWishedTerrors(reqId: number, request: any): Promise<void> {
    this.sendSuccess(reqId, { ok: true });
  }

  private async handleGetWishedTerrors(reqId: number, request: any): Promise<void> {
    this.sendSuccess(reqId, {
      terror_names: []
    });
  }

  // ==========================================================================
  // Response Helpers
  // ==========================================================================

  private sendSuccess(reqId: number, data: any): void {
    if (this.ws.readyState !== WebSocket.OPEN) return;

    const payload = msgpackEncode(data);
    const message = Buffer.alloc(4 + payload.length);
    message[0] = Opcode.Success;
    message[1] = reqId;
    message[2] = payload.length >> 8;
    message[3] = payload.length & 0xff;
    Buffer.from(payload).copy(message, 4);

    this.ws.send(message);
  }

  private sendError(reqId: number, errorMsg: string): void {
    if (this.ws.readyState !== WebSocket.OPEN) return;

    const payload = msgpackEncode({ message: errorMsg });
    const message = Buffer.alloc(4 + payload.length);
    message[0] = Opcode.Error;
    message[1] = reqId;
    message[2] = payload.length >> 8;
    message[3] = payload.length & 0xff;
    Buffer.from(payload).copy(message, 4);

    this.ws.send(message);
  }

  private sendPong(): void {
    if (this.ws.readyState !== WebSocket.OPEN) return;

    const message = Buffer.from([Opcode.Pong, 0, 0, 0]);
    this.ws.send(message);
  }

  private broadcastPlayerState(
    instanceId: string,
    playerId: string,
    velocity: number,
    afkDuration: number,
    damage: number,
    isAlive: boolean
  ): void {
    const clients = this.instances.get(instanceId);
    if (!clients) return;

    const payload = msgpackEncode({
      player_id: playerId,
      velocity,
      afk_duration: afkDuration,
      damage,
      is_alive: isAlive
    });

    const message = Buffer.alloc(4 + payload.length);
    message[0] = Opcode.PlayerStateEvent;
    message[1] = 0;
    message[2] = payload.length >> 8;
    message[3] = payload.length & 0xff;
    Buffer.from(payload).copy(message, 4);

    clients.forEach((client) => {
      if (client.readyState === WebSocket.OPEN) {
        client.send(message);
      }
    });
  }

  // ==========================================================================
  // Utilities
  // ==========================================================================

  private generateId(length: number): string {
    const chars = 'abcdefghijklmnopqrstuvwxyz0123456789';
    let result = '';
    for (let i = 0; i < length; i++) {
      result += chars[Math.floor(Math.random() * chars.length)];
    }
    return result;
  }

  private cleanup(): void {
    // Remove from all instances
    this.instances.forEach((clients) => {
      clients.delete(this.ws);
    });

    logger.info({ sessionId: this.state.sessionId }, 'Connection closed');
  }
}

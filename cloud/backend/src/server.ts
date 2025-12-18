import WebSocket from 'ws';
import express, { Express, Request, Response } from 'express';
import { v4 as uuidv4 } from 'uuid';
import { RPCRequest, RPCResponse, RPCRouter, SessionManager, Session, InstanceManager, DatabaseManager } from './core';
import { logInfo, logError, logDebug } from './logger';
import {
  RequestMessage,
  ResponseMessage,
  StreamMessage,
  isRequestMessage,
  isResponseMessage,
  isStreamMessage,
  createResponse,
  createErrorResponse,
  createStreamMessage,
  ErrorCode,
} from '@tonroundcounter/types';
import { AuthService } from './services/AuthService';
import { InstanceService } from './services/InstanceService';
import { PlayerStateService } from './services/PlayerStateService';
import { ThreatService } from './services/ThreatService';
import { VotingService } from './services/VotingService';
import { ProfileService } from './services/ProfileService';
import { SettingsService } from './services/SettingsService';
import { MonitoringService } from './services/MonitoringService';
// RemoteControlService removed - security risk
import { AnalyticsService } from './services/AnalyticsService';
import { BackupService } from './services/BackupService';
import { WishedTerrorService } from './services/WishedTerrorService';
import { RoundRepository } from './repositories/RoundRepository';

/**
 * Main WebSocket server implementation
 */
export class ToNRoundCounterCloudServer {
  private wss!: WebSocket.Server;
  private app: Express;
  private rpcRouter: RPCRouter;
  private sessionManager: SessionManager;
  private instanceManager: InstanceManager;
  private dbManager: DatabaseManager;
  private messageIdToSession = new Map<string, string>(); // Correlation for pending requests
  private wsHandler: any;

  // Services
  private authService: AuthService;
  private instanceService: InstanceService;
  private playerStateService: PlayerStateService;
  private threatService: ThreatService;
  private votingService: VotingService;
  private profileService: ProfileService;
  private settingsService: SettingsService;
  private monitoringService: MonitoringService;
  // private remoteControlService: RemoteControlService; // Removed - security risk
  private analyticsService: AnalyticsService;
  private backupService: BackupService;
  private wishedTerrorService: WishedTerrorService;
  private roundRepository: RoundRepository;

  constructor(private port: number = 8080) {
    this.app = express();
    this.rpcRouter = new RPCRouter();
    this.sessionManager = new SessionManager();
    this.instanceManager = new InstanceManager();
    this.dbManager = new DatabaseManager();

    // Initialize services with wsHandler (will be set properly after server starts)
    this.wsHandler = this.createWebSocketHandler();
    this.authService = new AuthService();
    this.instanceService = new InstanceService(this.wsHandler);
    this.playerStateService = new PlayerStateService(this.wsHandler);
    this.threatService = new ThreatService(this.wsHandler);
    this.votingService = new VotingService(this.wsHandler);
    this.profileService = new ProfileService();
    this.settingsService = new SettingsService(this.wsHandler);
    this.monitoringService = new MonitoringService(this.wsHandler);
    // this.remoteControlService = new RemoteControlService(this.wsHandler); // Removed - security risk
    this.analyticsService = new AnalyticsService();
    this.backupService = new BackupService();
    this.wishedTerrorService = new WishedTerrorService(this.wsHandler);
    this.roundRepository = new RoundRepository();

    this.setupExpress();
    this.setupRPCHandlers();
  }

  /**
   * Create WebSocket handler for services
   */
  private createWebSocketHandler() {
    return {
      broadcastToInstance: (instanceId: string, message: any) => {
        if (!this.wss) return;
        // Broadcast to all clients subscribed to this instance
        this.wss.clients.forEach((client) => {
          if (client.readyState === WebSocket.OPEN) {
            client.send(JSON.stringify(createStreamMessage(message.stream, message.data)));
          }
        });
      },
    };
  }

  /**
   * Setup Express middleware and routes
   */
  private setupExpress(): void {
    this.app.use(express.json());

    // Health check endpoint
    this.app.get('/health', (req: any, res: any) => {
      res.json({
        status: 'ok',
        timestamp: new Date().toISOString(),
        sessions: this.sessionManager.getSessionCount(),
        instances: this.instanceManager.getAllInstances().length,
        version: '1.0.0',
      });
    });

    // Stats endpoint
    this.app.get('/stats', (req: any, res: any) => {
      res.json({
        sessions: this.sessionManager.getSessionCount(),
        instances: this.instanceManager.getAllInstances().length,
      });
    });

    // REST API v1 routes
    // Instance Management
    this.app.get('/api/v1/instances', async (req: any, res: any) => {
      try {
        const filter = (req.query.filter || 'available') as 'available' | 'active' | 'all';
        const limit = parseInt(req.query.limit || '20');
        const offset = parseInt(req.query.offset || '0');
        
        const result = await this.instanceService.listInstances(filter, limit, offset);
        res.json(result);
      } catch (error: any) {
        res.status(500).json({ error: { code: 'INTERNAL_ERROR', message: error.message } });
      }
    });

    this.app.get('/api/v1/instances/:instanceId', async (req: any, res: any) => {
      try {
        const instance = await this.instanceService.getInstanceWithMembers(req.params.instanceId);
        if (!instance) {
          return res.status(404).json({ error: { code: 'NOT_FOUND', message: 'Instance not found' } });
        }
        res.json(instance);
      } catch (error: any) {
        res.status(500).json({ error: { code: 'INTERNAL_ERROR', message: error.message } });
      }
    });

    this.app.post('/api/v1/instances', async (req: any, res: any) => {
      try {
        const { max_players = 6, settings = { auto_suicide_mode: 'Individual', voting_timeout: 30 } } = req.body;
        const userId = req.headers['x-user-id'] || 'anonymous';
        
        // Generate a unique instance ID
        const instanceId = `inst_${Date.now()}_${Math.random().toString(36).substring(7)}`;
        const instance = await this.instanceService.createInstance(instanceId, userId, max_players, settings);
        res.json({ instance_id: instance.instance_id, created_at: instance.created_at });
      } catch (error: any) {
        res.status(500).json({ error: { code: 'INTERNAL_ERROR', message: error.message } });
      }
    });

    this.app.put('/api/v1/instances/:instanceId', async (req: any, res: any) => {
      try {
        await this.instanceService.updateInstance(req.params.instanceId, req.body);
        res.json({ instance_id: req.params.instanceId, updated_at: new Date().toISOString() });
      } catch (error: any) {
        res.status(500).json({ error: { code: 'INTERNAL_ERROR', message: error.message } });
      }
    });

    this.app.delete('/api/v1/instances/:instanceId', async (req: any, res: any) => {
      try {
        await this.instanceService.deleteInstance(req.params.instanceId);
        res.json({ success: true });
      } catch (error: any) {
        res.status(500).json({ error: { code: 'INTERNAL_ERROR', message: error.message } });
      }
    });

    // Profile Management
    this.app.get('/api/v1/profiles/:playerId', async (req: any, res: any) => {
      try {
        const profile = await this.profileService.getProfile(req.params.playerId);
        if (!profile) {
          return res.status(404).json({ error: { code: 'NOT_FOUND', message: 'Profile not found' } });
        }
        res.json(profile);
      } catch (error: any) {
        res.status(500).json({ error: { code: 'INTERNAL_ERROR', message: error.message } });
      }
    });

    this.app.put('/api/v1/profiles/:playerId', async (req: any, res: any) => {
      try {
        const profile = await this.profileService.updateProfile(req.params.playerId, req.body);
        res.json({ 
          player_id: profile.player_id, 
          player_name: profile.player_name, 
          updated_at: profile.last_active 
        });
      } catch (error: any) {
        res.status(500).json({ error: { code: 'INTERNAL_ERROR', message: error.message } });
      }
    });

    // Statistics
    this.app.get('/api/v1/stats/terrors', async (req: any, res: any) => {
      try {
        const playerId = req.query.player_id as string | undefined;
        const terrorStats = await this.analyticsService.getTerrorStatistics();
        
        res.json({ terror_stats: terrorStats });
      } catch (error: any) {
        res.status(500).json({ error: { code: 'INTERNAL_ERROR', message: error.message } });
      }
    });
  }

  /**
   * Register all RPC method handlers
   */
  private setupRPCHandlers(): void {
    // ========== Auth handlers ==========
    this.rpcRouter.register('auth.connect', async (req) => {
      const params = req.message.params as any;
      const clientId = params.clientId || 'unknown';
      const clientVersion = params.clientVersion || '0.0.0';

      const session = this.sessionManager.createSession(clientId, clientVersion);
      logInfo(`Session created: ${session.sessionId} for client ${clientId}`);

      return {
        sessionId: session.sessionId,
        userId: session.userId,
        serverVersion: '1.0.0',
      };
    });

    // ========== Game handlers ==========
    this.rpcRouter.register('game.roundStart', async (req) => {
      const params = req.message.params as any;
      
      // Use RoundRepository for proper round management
      const round = await this.roundRepository.createRound(
        params.instanceId || req.session.sessionId,
        params.roundType || params.roundKey || 'Unknown',
        params.playerCount || 1
      );

      logInfo(`Round started: ${round.round_id} by ${req.session.userId}`, {
        roundType: params.roundType,
        playerName: params.playerName,
      });

      return {
        roundId: round.round_id,
        startTime: round.start_time.toISOString(),
        terrorType: params.terrorType || null,
      };
    });

    this.rpcRouter.register('game.roundEnd', async (req) => {
      const params = req.message.params as any;
      const recordId = uuidv4();

      // Update round status
      if (params.roundId) {
        await this.roundRepository.endRound(params.roundId);
        
        // Update survivor count if provided
        if (params.survivorCount !== undefined) {
          await this.roundRepository.updateSurvivorCount(params.roundId, params.survivorCount);
        }
      }

      // Also save to legacy system for backward compatibility
      await this.dbManager.saveRound(req.session.userId, {
        roundId: params.roundId || recordId,
        roundType: params.roundType || 'Unknown',
        mapName: params.mapName,
        terrorType: params.terrorName,
        survived: params.survived,
        duration: params.duration,
        damageDealt: params.damageDealt,
        itemsObtained: params.itemsObtained,
      });

      logInfo(`Round ended: ${params.roundId}`, {
        survived: params.survived,
        duration: params.duration,
      });

      // Get updated stats using ProfileService
      const profile = await this.profileService.getProfile(req.session.userId);

      return {
        recordId,
        saved: true,
        stats: {
          totalRounds: profile?.total_rounds || 0,
          survivalRate: profile && profile.total_rounds > 0 
            ? profile.total_survived / profile.total_rounds 
            : 0,
          averageDuration: 0, // Would require calculating from rounds
        },
      };
    });

    // ========== Instance handlers ==========
    this.rpcRouter.register('instance.join', async (req) => {
      const params = req.message.params as any;
      const instanceId = params.instanceId;
      const playerId = params.playerId || req.session.userId;
      const playerName = params.playerName || playerId;

      // Check if instance exists, if not create it
      let instance = await this.instanceService.getInstance(instanceId);
      if (!instance) {
        // Auto-create instance when player joins
        await this.instanceService.createInstance(instanceId, playerId, 6, {
          auto_suicide_mode: 'Individual',
          voting_timeout: 30,
        });
        logInfo(`Auto-created instance: ${instanceId} for player ${playerName}`);
      }

      // Add to instance service (database)
      await this.instanceService.joinInstance(instanceId, playerId, playerName);

      // Add to in-memory instance manager
      let memInstance = this.instanceManager.getInstance(instanceId);
      if (!memInstance) {
        memInstance = this.instanceManager.createInstance(instanceId);
      }

      const member = {
        sessionId: req.session.sessionId,
        userId: playerId,
        playerName: playerName,
        playerData: new Map(),
        joinedAt: new Date(),
      };

      const added = this.instanceManager.addMember(instanceId, member);
      if (!added) {
        throw new Error('Instance is full or does not exist');
      }

      req.session.subscribe('instance', `instance_${instanceId}`);

      logInfo(`Player joined instance: ${instanceId}`, {
        playerName: playerName,
        memberCount: this.instanceManager.getMemberCount(instanceId),
      });

      // Broadcast member joined event to all instance members
      this.wsHandler.broadcastToInstance(instanceId, {
        type: 'stream',
        event: 'instance.member.joined',
        data: {
          instance_id: instanceId,
          player_id: playerId,
          player_name: playerName,
          joined_at: new Date().toISOString(),
        },
        timestamp: new Date().toISOString(),
      });

      // Get all members
      const members = this.instanceManager.getMembers(instanceId);
      return {
        subscriptionId: `instance_${instanceId}`,
        members: members.map((m) => ({
          playerId: m.userId,
          playerName: m.playerName,
          joined: m.joinedAt.toISOString(),
        })),
      };
    });

    this.rpcRouter.register('instance.leave', async (req) => {
      const params = req.message.params as any;
      const instanceId = params.instanceId;

      const removed = this.instanceManager.removeMember(instanceId, req.session.sessionId);
      if (removed) {
        req.session.unsubscribe(`instance_${instanceId}`);
        logInfo(`Player left instance: ${instanceId}`, {
          playerName: removed.playerName,
        });
      }

      return { left: !!removed };
    });

    this.rpcRouter.register('instance.alert', async (req) => {
      const params = req.message.params as any;
      const instanceId = params.instanceId;

      const members = this.instanceManager.getMembers(instanceId);
      logInfo(`Alert sent in instance ${instanceId}`, {
        alertType: params.alertType,
        recipients: members.length,
      });

      return {
        delivered: true,
        recipients: members.length,
      };
    });

    // ========== Subscription handlers ==========
    this.rpcRouter.register('subscribe', async (req) => {
      const params = req.message.params as any;
      const subscriptionId = uuidv4();

      req.session.subscribe(params.channel, subscriptionId);
      logDebug(`Subscription created: ${subscriptionId} for channel ${params.channel}`);

      return {
        subscriptionId,
      };
    });

    this.rpcRouter.register('unsubscribe', async (req) => {
      const params = req.message.params as any;
      req.session.unsubscribe(params.subscriptionId);
      logDebug(`Subscription removed: ${params.subscriptionId}`);

      return { unsubscribed: true };
    });

    // ========== Stats handlers ==========
    this.rpcRouter.register('stats.query', async (req) => {
      const params = req.message.params as any;
      
      // Use AnalyticsService for comprehensive statistics
      const timeRange = params.timeRange ? {
        start: new Date(params.timeRange.from),
        end: new Date(params.timeRange.to),
      } : undefined;
      
      const playerStats = await this.analyticsService.getPlayerStatistics(
        params.userId,
        timeRange
      );
      
      const terrorStats = await this.analyticsService.getTerrorStatistics(
        undefined, // all terrors
        timeRange
      );

      return {
        totalRounds: playerStats.total_rounds,
        completedRounds: playerStats.completed_rounds,
        survivalRate: playerStats.avg_survival_rate,
        averageDuration: playerStats.avg_duration_minutes,
        terrorDistribution: terrorStats.reduce((acc: any, terror: any) => {
          acc[terror.terror_name] = terror.appearance_count;
          return acc;
        }, {}),
        timeRange: params.timeRange,
      };
    });

    // ========== Auth handlers (additional) ==========
    this.rpcRouter.register('auth.login', async (req) => {
      const params = req.message.params as any;
      // Create session using player_id
      const session = await this.authService.createSession(
        params.player_id || params.playerId || params.email, 
        params.client_version || params.clientVersion || '1.0.0'
      );
      return {
        session_id: session.session_id,
        session_token: session.session_token,
        player_id: session.user_id,
        user_id: session.user_id,
      };
    });

    this.rpcRouter.register('auth.logout', async (req) => {
      await this.authService.logout(req.session.sessionId);
      return { success: true };
    });

    this.rpcRouter.register('auth.refresh', async (req) => {
      await this.authService.extendSession(req.session.sessionId);
      return { 
        success: true, 
        session_id: req.session.sessionId,
        session_token: req.session.sessionId, // In a real implementation, generate a new token
      };
    });

    // ========== Instance handlers (additional) ==========
    this.rpcRouter.register('instance.create', async (req) => {
      const params = req.message.params as any;
      // Generate a unique instance ID
      const instanceId = `inst_${Date.now()}_${Math.random().toString(36).substring(7)}`;
      const instance = await this.instanceService.createInstance(
        instanceId,
        req.session.userId,
        params.max_players || 10,
        params.settings || { auto_suicide_mode: 'Individual', voting_timeout: 30 }
      );
      return instance;
    });

    this.rpcRouter.register('instance.list', async (req) => {
      const params = req.message.params as any;
      const result = await this.instanceService.listInstances(
        params.filter || 'available',
        params.limit || 50,
        params.offset || 0
      );
      return result;
    });

    this.rpcRouter.register('instance.update', async (req) => {
      const params = req.message.params as any;
      await this.instanceService.updateInstance(params.instance_id, params.updates);
      return { success: true };
    });

    this.rpcRouter.register('instance.delete', async (req) => {
      const params = req.message.params as any;
      await this.instanceService.deleteInstance(params.instance_id);
      return { success: true };
    });

    this.rpcRouter.register('instance.get', async (req) => {
      const params = req.message.params as any;
      const instance = await this.instanceService.getInstance(params.instance_id);
      return instance;
    });

    // ========== Player state handlers ==========
    this.rpcRouter.register('player.state.update', async (req) => {
      const params = req.message.params as any;
      const instanceId = params.instance_id || req.session.sessionId;
      
      // Extract player_state from params (it may be nested)
      const playerState = params.player_state || params;
      
      const state = {
        player_id: playerState.player_id || params.player_id || req.session.userId,
        player_name: playerState.player_name || playerState.player_id || params.player_id || req.session.userId,
        velocity: Number(playerState.velocity) || 0,
        afk_duration: Math.floor(Number(playerState.afk_duration) || 0), // Convert to integer
        items: Array.isArray(playerState.items) ? playerState.items : [],
        damage: Math.floor(Number(playerState.damage) || 0), // Convert to integer
        is_alive: playerState.is_alive !== false,
      };
      
      const stateChanged = await this.playerStateService.updatePlayerState(instanceId, state);
      
      // Only broadcast if state actually changed
      if (stateChanged) {
        this.wsHandler.broadcastToInstance(instanceId, {
          type: 'stream',
          event: 'player.state.updated',
          data: {
            instance_id: instanceId,
            player_id: state.player_id,
            velocity: state.velocity,
            afk_duration: state.afk_duration,
            items: state.items,
            damage: state.damage,
            is_alive: state.is_alive,
          },
          timestamp: new Date().toISOString(),
        });
      }
      
      return { success: true, state_changed: stateChanged };
    });

    this.rpcRouter.register('player.state.get', async (req) => {
      const params = req.message.params as any;
      const state = await this.playerStateService.getPlayerState(
        params.instance_id,
        params.player_id || req.session.userId
      );
      return state;
    });

    this.rpcRouter.register('player.state.getAll', async (req) => {
      const params = req.message.params as any;
      const states = await this.playerStateService.getAllPlayerStates(params.instance_id);
      return { states };
    });

    // Get player states with member info for an instance
    this.rpcRouter.register('player.states.get', async (req) => {
      const params = req.message.params as any;
      const instanceId = params.instanceId;
      
      // Get player states
      const states = await this.playerStateService.getAllPlayerStates(instanceId);
      
      // Get instance members for player names using InstanceRepository
      const InstanceRepository = require('./repositories/InstanceRepository').default;
      const members = await InstanceRepository.getMembers(instanceId);
      
      // Merge state and member info
      const memberMap = new Map(members.map((m: any) => [m.player_id, m.player_name]));
      
      const playerStates = states.map(state => ({
        player_id: state.player_id,
        player_name: memberMap.get(state.player_id) || 'Unknown',
        damage: state.damage,
        current_item: state.items && state.items.length > 0 ? state.items[state.items.length - 1] : 'None',
        is_dead: !state.is_alive,
        velocity: state.velocity,
        afk_duration: state.afk_duration,
      }));
      
      return { player_states: playerStates };
    });

    // ========== Threat handlers ==========
    this.rpcRouter.register('threat.announce', async (req) => {
      const params = req.message.params as any;
      await this.threatService.announceThreat({
        terror_name: params.terror_name,
        round_key: params.round_key,
        instance_id: params.instance_id,
        desire_players: params.desire_players || [],
      });
      return { success: true };
    });

    this.rpcRouter.register('threat.response', async (req) => {
      const params = req.message.params as any;
      await this.threatService.recordThreatResponse(
        params.threat_id,
        params.player_id || req.session.userId,
        params.decision || params.response
      );
      return { success: true };
    });

    // ========== Voting handlers ==========
    this.rpcRouter.register('coordinated.voting.start', async (req) => {
      const params = req.message.params as any;
      const campaignId = uuidv4();
      const expiresAt = params.expires_at ? new Date(params.expires_at) : new Date(Date.now() + 60000);
      const result = await this.votingService.startVoting(
        campaignId,
        params.instance_id,
        params.terror_name,
        expiresAt
      );
      return result;
    });

    this.rpcRouter.register('coordinated.voting.vote', async (req) => {
      const params = req.message.params as any;
      await this.votingService.submitVote(
        params.campaign_id || params.voting_id,
        params.player_id || req.session.userId,
        params.decision || params.option || 'Proceed'
      );
      return { success: true };
    });

    this.rpcRouter.register('coordinated.voting.getCampaign', async (req) => {
      const params = req.message.params as any;
      const campaign = await this.votingService.getCampaign(params.campaign_id);
      return campaign;
    });

    this.rpcRouter.register('coordinated.voting.getVotes', async (req) => {
      const params = req.message.params as any;
      const votes = await this.votingService.getVotes(params.campaign_id);
      return { votes };
    });

    // ========== Wished terrors handlers ==========
    this.rpcRouter.register('wished.terrors.update', async (req) => {
      const params = req.message.params as any;
      await this.wishedTerrorService.updateWishedTerrors(
        params.player_id || req.session.userId,
        params.wished_terrors
      );
      return { success: true };
    });

    this.rpcRouter.register('wished.terrors.get', async (req) => {
      const params = req.message.params as any;
      const result = await this.wishedTerrorService.getWishedTerrors(params.player_id);
      return { wished_terrors: result };
    });

    this.rpcRouter.register('wished.terrors.findDesirePlayers', async (req) => {
      const params = req.message.params as any;
      const desirePlayers = await this.wishedTerrorService.findDesirePlayersForTerror(
        params.instance_id,
        params.terror_name,
        params.round_key || ''
      );
      return { desire_players: desirePlayers };
    });

    // ========== Profile handlers ==========
    this.rpcRouter.register('profile.get', async (req) => {
      const params = req.message.params as any;
      const profile = await this.profileService.getProfile(params.player_id);
      return profile;
    });

    this.rpcRouter.register('profile.update', async (req) => {
      const params = req.message.params as any;
      const profile = await this.profileService.updateProfile(params.player_id, {
        player_name: params.player_name,
        skill_level: params.skill_level,
        terror_stats: params.terror_stats,
        total_rounds: params.total_rounds,
        total_survived: params.total_survived,
      });
      return profile;
    });

    // ========== Settings handlers ==========
    this.rpcRouter.register('settings.get', async (req) => {
      const params = req.message.params as any;
      const settings = await this.settingsService.getSettings(params.user_id);
      return settings;
    });

    this.rpcRouter.register('settings.update', async (req) => {
      const params = req.message.params as any;
      const result = await this.settingsService.updateSettings(params.user_id, params.settings);
      return result;
    });

    this.rpcRouter.register('settings.sync', async (req) => {
      const params = req.message.params as any;
      // Get remote settings
      const remoteSettings = await this.settingsService.getSettings(params.user_id);
      const result = await this.settingsService.mergeSettings(
        params.user_id,
        params.local_settings,
        remoteSettings?.categories || {}
      );
      return { merged_settings: result, remote_version: remoteSettings?.version || 0 };
    });

    this.rpcRouter.register('settings.history', async (req) => {
      const params = req.message.params as any;
      const history = await this.settingsService.getSettingsHistory(
        params.user_id || req.session.userId,
        params.limit || 10
      );
      return { history };
    });

    // ========== Monitoring handlers ==========
    this.rpcRouter.register('monitoring.report', async (req) => {
      const params = req.message.params as any;
      const statusData = params.status_data || params;
      const result = await this.monitoringService.reportStatus(
        req.session.userId,
        params.instance_id,
        {
          application_status: statusData.application_status || 'RUNNING',
          application_version: statusData.application_version,
          uptime: statusData.uptime || 0,
          memory_usage: statusData.memory_usage || 0,
          cpu_usage: statusData.cpu_usage || 0,
          osc_status: statusData.osc_status,
          osc_latency: statusData.osc_latency,
          vrchat_status: statusData.vrchat_status,
          vrchat_world_id: statusData.vrchat_world_id,
          vrchat_instance_id: statusData.vrchat_instance_id,
        }
      );
      return result;
    });

    this.rpcRouter.register('monitoring.status', async (req) => {
      const params = req.message.params as any;
      const result = await this.monitoringService.getStatusHistory(params.user_id, params.limit || 10);
      return { statuses: result };
    });

    this.rpcRouter.register('monitoring.errors', async (req) => {
      const params = req.message.params as any;
      const result = await this.monitoringService.getErrors(
        params.user_id,
        params.severity,
        params.limit || 50
      );
      return { errors: result };
    });

    // ========== Remote control handlers - REMOVED FOR SECURITY ==========
    // Remote command execution poses a critical security risk
    // Attackers could execute arbitrary commands on client machines
    // All remote.command.* endpoints have been permanently disabled

    // ========== Analytics handlers ==========
    this.rpcRouter.register('analytics.player', async (req) => {
      const params = req.message.params as any;
      const result = await this.analyticsService.getPlayerStatistics(
        params.player_id,
        params.time_range
      );
      return result;
    });

    this.rpcRouter.register('analytics.terror', async (req) => {
      const params = req.message.params as any;
      const result = await this.analyticsService.getTerrorStatistics(
        params.terror_name,
        params.time_range
      );
      return result;
    });

    this.rpcRouter.register('analytics.trends', async (req) => {
      const params = req.message.params as any;
      const result = await this.analyticsService.getRoundTrends(
        params.group_by,
        params.limit
      );
      return result;
    });

    this.rpcRouter.register('analytics.instance', async (req) => {
      const params = req.message.params as any;
      const result = await this.analyticsService.getInstanceStatistics(params.instance_id);
      return result;
    });

    this.rpcRouter.register('analytics.voting', async (req) => {
      const params = req.message.params as any;
      const result = await this.analyticsService.getVotingStatistics(params.instance_id);
      return result;
    });

    this.rpcRouter.register('analytics.export', async (req) => {
      const params = req.message.params as any;
      const result = await this.analyticsService.exportData(
        params.format,
        params.data_type,
        params.filters
      );
      return result;
    });

    // ========== Backup handlers ==========
    this.rpcRouter.register('backup.create', async (req) => {
      const params = req.message.params as any;
      const result = await this.backupService.createBackup(
        req.session.userId,
        {
          type: params.type || 'FULL',
          compress: params.compress !== false,
          encrypt: params.encrypt === true,
          description: params.description,
        }
      );
      return result;
    });

    this.rpcRouter.register('backup.restore', async (req) => {
      const params = req.message.params as any;
      await this.backupService.restoreBackup(
        params.backup_id,
        { 
          validateBeforeRestore: params.validate_before_restore !== false,
          createBackupBeforeRestore: params.create_backup_before_restore !== false,
        }
      );
      return { success: true };
    });

    this.rpcRouter.register('backup.list', async (req) => {
      const params = req.message.params as any;
      const result = await this.backupService.listBackups(params.user_id);
      return { backups: result };
    });
  }

  /**
   * Handle incoming WebSocket message
   */
  private async handleMessage(ws: WebSocket, rawMessage: string, session: Session): Promise<void> {
    try {
      const message = JSON.parse(rawMessage) as RequestMessage;

      if (!isRequestMessage(message)) {
        logError('Invalid message format', { message });
        const msgId = (message as any)?.id || 'unknown';
        ws.send(
          JSON.stringify(
            createErrorResponse(msgId, ErrorCode.INVALID_PARAMS, 'Invalid message format')
          )
        );
        return;
      }

      logDebug(`RPC call: ${message.method}`, { id: message.id });
      session.updateActivity();

      const req: RPCRequest = {
        message,
        session,
      };

      if (!this.rpcRouter.has(message.method)) {
        ws.send(
          JSON.stringify(
            createErrorResponse(message.id, ErrorCode.NOT_FOUND, `Method not found: ${message.method}`)
          )
        );
        return;
      }

      try {
        const result = await this.rpcRouter.execute(message.method, req);
        const response = createResponse(message.id, result);
        ws.send(JSON.stringify(response));
      } catch (error) {
        logError(`RPC method error: ${message.method}`, error);
        const errorMessage = error instanceof Error ? error.message : 'Internal server error';
        const errorCode = errorMessage.includes('not found') || errorMessage.includes('Not found') 
          ? ErrorCode.NOT_FOUND 
          : errorMessage.includes('invalid') || errorMessage.includes('Invalid')
          ? ErrorCode.INVALID_PARAMS
          : ErrorCode.INTERNAL_ERROR;
        
        ws.send(
          JSON.stringify(
            createErrorResponse(message.id, errorCode, errorMessage)
          )
        );
      }
    } catch (error) {
      logError('Message parsing error', error);
      const msg = rawMessage ? JSON.parse(rawMessage) : {};
      ws.send(
        JSON.stringify(
          createErrorResponse(
            msg.id || 'unknown',
            ErrorCode.INTERNAL_ERROR,
            error instanceof Error ? error.message : 'Internal server error'
          )
        )
      );
    }
  }

  /**
   * Start the server
   */
  async start(): Promise<void> {
    return new Promise((resolve) => {
      const server = this.app.listen(this.port, () => {
        logInfo(`HTTP server listening on port ${this.port}`);
      });

      this.wss = new WebSocket.Server({ server });

      this.wss.on('connection', (ws: WebSocket) => {
        logInfo('WebSocket client connected');

        let session: Session | null = null;

        ws.on('message', async (rawMessage: string) => {
          if (!session) {
            // First message must be auth.connect
            const msg = JSON.parse(rawMessage) as RequestMessage;
            if (msg.method === 'auth.connect') {
              try {
                const result = await this.rpcRouter.execute('auth.connect', {
                  message: msg,
                  session: {} as Session, // Temporary session for auth
                } as any);

                const sessionId = result.sessionId;
                session = this.sessionManager.getSession(sessionId)!;

                const response = createResponse(msg.id, result);
                ws.send(JSON.stringify(response));
                logInfo(`Client authenticated: ${sessionId}`);
              } catch (err) {
                logError('Auth failed', err);
                ws.close(1008, 'Authentication failed');
              }
            } else {
              ws.close(1008, 'Must authenticate first');
            }
          } else {
            await this.handleMessage(ws, rawMessage, session);
          }
        });

        ws.on('close', () => {
          if (session) {
            this.sessionManager.removeSession(session.sessionId);
            logInfo(`Session closed: ${session.sessionId}`);
          }
        });

        ws.on('error', (error: any) => {
          logError('WebSocket error', error);
        });
      });

      resolve();
    });
  }

  /**
   * Stop the server
   */
  async stop(): Promise<void> {
    return new Promise((resolve) => {
      this.wss.close(() => {
        logInfo('WebSocket server closed');
        resolve();
      });
    });
  }
}

export default ToNRoundCounterCloudServer;

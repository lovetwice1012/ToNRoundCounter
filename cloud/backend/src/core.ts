import { v4 as uuidv4 } from 'uuid';
import { RequestMessage, ResponseMessage, StreamMessage, ErrorDetails } from '@tonroundcounter/types';

export class SessionManager {
  private sessions = new Map<string, Session>();

  createSession(clientId: string, clientVersion: string): Session {
    const sessionId = uuidv4();
    const session = new Session(sessionId, clientId, clientVersion);
    this.sessions.set(sessionId, session);
    return session;
  }

  getSession(sessionId: string): Session | undefined {
    return this.sessions.get(sessionId);
  }

  removeSession(sessionId: string): void {
    this.sessions.delete(sessionId);
  }

  getAllSessions(): Session[] {
    return Array.from(this.sessions.values());
  }

  getSessionCount(): number {
    return this.sessions.size;
  }
}

export class Session {
  readonly sessionId: string;
  readonly clientId: string;
  readonly clientVersion: string;
  private _userId: string;
  readonly createdAt: Date;
  lastActivity: Date;
  subscriptions = new Set<string>();
  data: Map<string, any> = new Map();

  constructor(sessionId: string, clientId: string, clientVersion: string) {
    this.sessionId = sessionId;
    this.clientId = clientId;
    this.clientVersion = clientVersion;
    this._userId = uuidv4();
    this.createdAt = new Date();
    this.lastActivity = new Date();
  }

  get userId(): string {
    return this._userId;
  }

  set userId(value: string) {
    this._userId = value;
  }

  updateActivity(): void {
    this.lastActivity = new Date();
  }

  subscribe(channel: string, subscriptionId: string): void {
    this.subscriptions.add(subscriptionId);
    this.data.set(`sub_${subscriptionId}`, { channel, subscribedAt: new Date() });
  }

  unsubscribe(subscriptionId: string): void {
    this.subscriptions.delete(subscriptionId);
    this.data.delete(`sub_${subscriptionId}`);
  }

  isSubscribedTo(channel: string): boolean {
    for (const subId of this.subscriptions) {
      const subData = this.data.get(`sub_${subId}`);
      if (subData && subData.channel === channel) {
        return true;
      }
    }
    return false;
  }
}

export interface RPCRequest {
  message: RequestMessage;
  session: Session;
}

export interface RPCResponse {
  message: ResponseMessage | StreamMessage;
  broadcastTo?: string[]; // session IDs to broadcast to
}

/**
 * RPC Handler registry and executor
 */
export class RPCRouter {
  private handlers = new Map<string, (req: RPCRequest) => Promise<any>>();

  register(method: string, handler: (req: RPCRequest) => Promise<any>): void {
    this.handlers.set(method, handler);
  }

  async execute(method: string, req: RPCRequest): Promise<any> {
    const handler = this.handlers.get(method);
    if (!handler) {
      throw new Error(`Method not found: ${method}`);
    }
    return handler(req);
  }

  has(method: string): boolean {
    return this.handlers.has(method);
  }
}

/**
 * Instance management for shared multiplayer
 */
export interface InstanceData {
  instanceId: string;
  name: string;
  members: Map<string, InstanceMember>;
  createdAt: Date;
  maxMembers?: number;
}

export interface InstanceMember {
  sessionId: string;
  userId: string;
  playerName: string;
  playerData: Map<string, any>;
  joinedAt: Date;
}

export class InstanceManager {
  private instances = new Map<string, InstanceData>();

  createInstance(name: string, maxMembers?: number): InstanceData {
    const instanceId = uuidv4();
    const instance: InstanceData = {
      instanceId,
      name,
      members: new Map(),
      createdAt: new Date(),
      maxMembers,
    };
    this.instances.set(instanceId, instance);
    return instance;
  }

  getInstance(instanceId: string): InstanceData | undefined {
    return this.instances.get(instanceId);
  }

  addMember(instanceId: string, member: InstanceMember): boolean {
    const instance = this.instances.get(instanceId);
    if (!instance) return false;

    if (instance.maxMembers && instance.members.size >= instance.maxMembers) {
      return false;
    }

    instance.members.set(member.sessionId, member);
    return true;
  }

  removeMember(instanceId: string, sessionId: string): InstanceMember | undefined {
    const instance = this.instances.get(instanceId);
    if (!instance) return undefined;

    const member = instance.members.get(sessionId);
    instance.members.delete(sessionId);

    if (instance.members.size === 0) {
      this.instances.delete(instanceId);
    }

    return member;
  }

  getMembers(instanceId: string): InstanceMember[] {
    const instance = this.instances.get(instanceId);
    if (!instance) return [];
    return Array.from(instance.members.values());
  }

  getMemberCount(instanceId: string): number {
    const instance = this.instances.get(instanceId);
    return instance ? instance.members.size : 0;
  }

  getAllInstances(): InstanceData[] {
    return Array.from(this.instances.values());
  }
}

/**
 * Database abstraction layer
 * Note: This class is deprecated. Use dedicated repositories instead:
 * - RoundRepository for round operations
 * - ProfileService for player stats
 * - AnalyticsService for aggregated statistics
 * 
 * This class is kept for backward compatibility only.
 */
export class DatabaseManager {
  private db: any;

  constructor() {
    // Lazy load database to avoid circular dependencies
    this.db = null;
  }

  private getDb() {
    if (!this.db) {
      const { getDatabase } = require('./database/connection');
      this.db = getDatabase();
    }
    return this.db;
  }

  async saveRound(
    userId: string,
    roundData: {
      roundId: string;
      roundType: string;
      mapName?: string;
      terrorType?: string;
      survived: boolean;
      duration: number;
      damageDealt?: number;
      itemsObtained?: string[];
    }
  ): Promise<string> {
    const db = this.getDb();
    
    // This is a legacy method - modern code should use RoundRepository
    // We'll store basic round information here
    const metadata = {
      roundType: roundData.roundType,
      mapName: roundData.mapName,
      terrorType: roundData.terrorType,
      survived: roundData.survived,
      duration: roundData.duration,
      damageDealt: roundData.damageDealt,
      itemsObtained: roundData.itemsObtained,
    };

    await db.run(
      `INSERT INTO rounds (round_id, instance_id, round_key, start_time, status, metadata)
       VALUES (?, ?, ?, NOW(), 'COMPLETED', ?)
       ON DUPLICATE KEY UPDATE metadata = VALUES(metadata)`,
      [roundData.roundId, userId, roundData.roundType, JSON.stringify(metadata)]
    );

    return roundData.roundId;
  }

  async getRoundStats(userId: string, timeRange?: { from: Date; to: Date }): Promise<any> {
    const db = this.getDb();
    
    let query = `
      SELECT 
        COUNT(*) as totalRounds,
        AVG(CASE WHEN JSON_EXTRACT(metadata, '$.survived') = true THEN 1.0 ELSE 0.0 END) as survivalRate,
        AVG(JSON_EXTRACT(metadata, '$.duration')) as averageDuration,
        JSON_EXTRACT(metadata, '$.terrorType') as terrorType,
        JSON_EXTRACT(metadata, '$.itemsObtained') as items
      FROM rounds
      WHERE instance_id = ?
    `;

    const params: any[] = [userId];

    if (timeRange) {
      query += ` AND created_at BETWEEN ? AND ?`;
      params.push(timeRange.from.toISOString(), timeRange.to.toISOString());
    }

    const result = await db.get(query, params);

    // Get terror distribution
    const terrorQuery = `
      SELECT 
        JSON_EXTRACT(metadata, '$.terrorType') as terror,
        COUNT(*) as count
      FROM rounds
      WHERE instance_id = ? ${timeRange ? 'AND created_at BETWEEN ? AND ?' : ''}
      GROUP BY terror
    `;
    
    const terrorRows = await db.all(terrorQuery, params);
    const terrorDistribution: Record<string, number> = {};
    terrorRows.forEach((row: any) => {
      if (row.terror) {
        terrorDistribution[row.terror] = row.count;
      }
    });

    return {
      totalRounds: result?.totalRounds || 0,
      survivalRate: result?.survivalRate || 0.0,
      averageDuration: result?.averageDuration || 0,
      terrorDistribution,
      itemDistribution: {}, // Would require more complex JSON parsing
      timeRange: timeRange ? {
        from: timeRange.from.toISOString(),
        to: timeRange.to.toISOString(),
      } : null,
    };
  }

  async getUserStats(userId: string): Promise<any> {
    const db = this.getDb();
    
    // Query player profile and statistics
    const profile = await db.get(
      `SELECT * FROM player_profiles WHERE player_id = ?`,
      [userId]
    );

    if (!profile) {
      // Return default stats for new users
      return {
        userId,
        totalRounds: 0,
        survivalRate: 0.0,
        averageDuration: 0,
        lastActive: new Date().toISOString(),
      };
    }

    // Get aggregated stats
    const stats = await db.get(
      `SELECT 
        COUNT(*) as totalRounds,
        AVG(CASE WHEN JSON_EXTRACT(metadata, '$.survived') = true THEN 1.0 ELSE 0.0 END) as survivalRate,
        AVG(JSON_EXTRACT(metadata, '$.duration')) as averageDuration
       FROM rounds
       WHERE instance_id = ?`,
      [userId]
    );

    return {
      userId,
      playerName: profile.player_name,
      skillLevel: profile.skill_level,
      totalRounds: stats?.totalRounds || profile.total_rounds,
      totalSurvived: profile.total_survived,
      survivalRate: stats?.survivalRate || (profile.total_rounds > 0 ? profile.total_survived / profile.total_rounds : 0),
      averageDuration: stats?.averageDuration || 0,
      lastActive: profile.last_active,
      terrorStats: JSON.parse(profile.terror_stats || '{}'),
    };
  }
}

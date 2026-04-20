/**
 * Global State Management using Zustand
 */

import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import { ToNRoundCloudClient } from '../lib/websocket-client';
import { RestApiClient } from '../lib/rest-client';

export interface AppState {
    // Connection
    client: ToNRoundCloudClient | null;
    restClient: RestApiClient | null;
    connected: boolean;
    connectionState: 'connected' | 'disconnected' | 'reconnecting' | 'auth-required';
    wsLatencyMs: number | null;
    apiLatencyMs: number | null;
    lastSyncAt: string | null;
    
    // Auth
    sessionToken: string | null;
    playerId: string | null;
    /** Internal DB user_id returned by auth — needed for instance membership self-check. */
    userId: string | null;
    
    // Instance
    currentInstance: any | null;
    instances: any[];
    instanceMembers: any[];
    
    // Player States
    playerStates: Map<string, any>;
    
    // Voting
    activeVoting: any | null;
    
    // Settings
    settings: any | null;
    
    // Monitoring
    statusHistory: any[];
    errors: any[];
    
    // Analytics
    playerStats: any | null;
    terrorStats: any[];
    roundTypeStats: any[];
    trends: any[];

    // UI
    toasts: Array<{ id: string; type: 'success' | 'error' | 'info'; message: string }>;
    
    // Actions
    setClient: (client: ToNRoundCloudClient) => void;
    setRestClient: (restClient: RestApiClient) => void;
    setConnected: (connected: boolean) => void;
    setConnectionState: (state: 'connected' | 'disconnected' | 'reconnecting' | 'auth-required') => void;
    setWsLatencyMs: (latency: number | null) => void;
    setApiLatencyMs: (latency: number | null) => void;
    touchSyncTime: () => void;
    setSession: (token: string | null, playerId: string | null, userId?: string | null) => void;
    setCurrentInstance: (instance: any | null) => void;
    setInstances: (instances: any[]) => void;
    setInstanceMembers: (members: any[]) => void;
    updatePlayerState: (playerId: string, state: any) => void;
    setActiveVoting: (voting: any | null) => void;
    setSettings: (settings: any) => void;
    setStatusHistory: (history: any[]) => void;
    setErrors: (errors: any[]) => void;
    setPlayerStats: (stats: any) => void;
    setTerrorStats: (stats: any[]) => void;
    setRoundTypeStats: (stats: any[]) => void;
    setTrends: (trends: any[]) => void;
    pushToast: (toast: { type: 'success' | 'error' | 'info'; message: string }) => void;
    removeToast: (id: string) => void;
    logout: () => void;
}

export const useAppStore = create<AppState>()(
    persist(
        (set) => ({
    // Initial State
    client: null,
    restClient: null,
    connected: false,
    connectionState: 'disconnected',
    wsLatencyMs: null,
    apiLatencyMs: null,
    lastSyncAt: null,
    sessionToken: null,
    playerId: null,
    userId: null,
    currentInstance: null,
    instances: [],
    instanceMembers: [],
    playerStates: new Map(),
    activeVoting: null,
    settings: null,
    statusHistory: [],
    errors: [],
    playerStats: null,
    terrorStats: [],
    roundTypeStats: [],
    trends: [],
    toasts: [],
    
    // Actions
    setClient: (client) => set({ client }),
    
    setRestClient: (restClient) => set({ restClient }),
    
    setConnected: (connected) => set({ connected }),
    
    setConnectionState: (connectionState) => set({ connectionState }),

    setWsLatencyMs: (wsLatencyMs) => set({ wsLatencyMs }),

    setApiLatencyMs: (apiLatencyMs) => set({ apiLatencyMs }),

    touchSyncTime: () => set({ lastSyncAt: new Date().toISOString() }),
    
    setSession: (sessionToken, playerId, userId) => set({ sessionToken, playerId, ...(userId !== undefined ? { userId } : {}) }),
    
    setCurrentInstance: (currentInstance) => set({ currentInstance }),
    
    setInstances: (instances) => set({ instances: Array.isArray(instances) ? instances : [] }),
    
    setInstanceMembers: (instanceMembers) => set({ instanceMembers: Array.isArray(instanceMembers) ? instanceMembers : [] }),
    
    updatePlayerState: (playerId, state) => set((prevState) => {
        const newStates = new Map(prevState.playerStates);
        const prev: any = newStates.get(playerId) ?? {};
        const incoming: any = state ?? {};
        // 部分更新でも既存値を残すために merge する。
        const merged: any = { ...prev, ...incoming };

        // items が空配列で来た場合、直前に保持していたアイテムが消えてしまう
        // (速度のみのスナップショット送信などで一時的に items=[] が届くケース)
        // ため、前回が非空なら維持する。
        if (
            Array.isArray(incoming.items) &&
            incoming.items.length === 0 &&
            Array.isArray(prev.items) &&
            prev.items.length > 0
        ) {
            merged.items = prev.items;
        }

        // timestamp が無い payload (WS broadcast) の場合は受信時刻を補う。
        // 補わないと「不明 ⇄ 時刻」を交互に表示してしまう。
        if (incoming.timestamp === undefined || incoming.timestamp === null) {
            merged.timestamp = prev.timestamp ?? new Date().toISOString();
        }

        newStates.set(playerId, merged);
        return { playerStates: newStates };
    }),
    
    setActiveVoting: (activeVoting) => set({ activeVoting }),
    
    setSettings: (settings) => set({ settings }),
    
    setStatusHistory: (statusHistory) => set({ statusHistory: Array.isArray(statusHistory) ? statusHistory : [] }),
    
    setErrors: (errors) => set({ errors: Array.isArray(errors) ? errors : [] }),
    
    setPlayerStats: (playerStats) => set({ playerStats }),
    
    setTerrorStats: (terrorStats) => set({ terrorStats: Array.isArray(terrorStats) ? terrorStats : [] }),
    
    setRoundTypeStats: (roundTypeStats) => set({ roundTypeStats: Array.isArray(roundTypeStats) ? roundTypeStats : [] }),
    
    setTrends: (trends) => set({ trends: Array.isArray(trends) ? trends : [] }),

    pushToast: (toast) => set((state) => {
        const id = `${Date.now()}_${Math.random().toString(36).slice(2, 10)}`;
        return {
            toasts: [...state.toasts, { id, ...toast }].slice(-6),
        };
    }),

    removeToast: (id) => set((state) => ({
        toasts: state.toasts.filter((toast) => toast.id !== id),
    })),
    
    logout: () => set({
        client: null,
        restClient: null,
        connected: false,
        connectionState: 'disconnected',
        wsLatencyMs: null,
        apiLatencyMs: null,
        lastSyncAt: null,
        sessionToken: null,
        playerId: null,
        userId: null,
        currentInstance: null,
        instances: [],
        instanceMembers: [],
        playerStates: new Map(),
        activeVoting: null,
        settings: null,
        toasts: [],
    }),
}),
        {
            name: 'tonround-cloud-storage',
            partialize: (state) => ({
                sessionToken: state.sessionToken,
                playerId: state.playerId,
                userId: state.userId,
                playerStats: state.playerStats,
                terrorStats: state.terrorStats,
                roundTypeStats: state.roundTypeStats,
                trends: state.trends,
            }),
        }
    )
);

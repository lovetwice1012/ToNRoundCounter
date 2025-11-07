/**
 * Global State Management using Zustand
 */

import { create } from 'zustand';
import { ToNRoundCloudClient } from '../lib/websocket-client';
import { RestApiClient } from '../lib/rest-client';

export interface AppState {
    // Connection
    client: ToNRoundCloudClient | null;
    restClient: RestApiClient | null;
    connected: boolean;
    connectionState: 'connected' | 'disconnected' | 'reconnecting';
    
    // Auth
    sessionToken: string | null;
    playerId: string | null;
    
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
    trends: any[];
    
    // Actions
    setClient: (client: ToNRoundCloudClient) => void;
    setRestClient: (restClient: RestApiClient) => void;
    setConnected: (connected: boolean) => void;
    setConnectionState: (state: 'connected' | 'disconnected' | 'reconnecting') => void;
    setSession: (token: string | null, playerId: string | null) => void;
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
    setTrends: (trends: any[]) => void;
}

export const useAppStore = create<AppState>((set) => ({
    // Initial State
    client: null,
    restClient: null,
    connected: false,
    connectionState: 'disconnected',
    sessionToken: null,
    playerId: null,
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
    trends: [],
    
    // Actions
    setClient: (client) => set({ client }),
    
    setRestClient: (restClient) => set({ restClient }),
    
    setConnected: (connected) => set({ connected }),
    
    setConnectionState: (connectionState) => set({ connectionState }),
    
    setSession: (sessionToken, playerId) => set({ sessionToken, playerId }),
    
    setCurrentInstance: (currentInstance) => set({ currentInstance }),
    
    setInstances: (instances) => set({ instances }),
    
    setInstanceMembers: (instanceMembers) => set({ instanceMembers }),
    
    updatePlayerState: (playerId, state) => set((prevState) => {
        const newStates = new Map(prevState.playerStates);
        newStates.set(playerId, state);
        return { playerStates: newStates };
    }),
    
    setActiveVoting: (activeVoting) => set({ activeVoting }),
    
    setSettings: (settings) => set({ settings }),
    
    setStatusHistory: (statusHistory) => set({ statusHistory }),
    
    setErrors: (errors) => set({ errors }),
    
    setPlayerStats: (playerStats) => set({ playerStats }),
    
    setTerrorStats: (terrorStats) => set({ terrorStats }),
    
    setTrends: (trends) => set({ trends }),
}));

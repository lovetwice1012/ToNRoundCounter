/**
 * Settings Service
 * Handles settings synchronization and management
 */

import SettingsRepository from '../repositories/SettingsRepository';
import { Settings, SettingsCategories } from '../models/types';
import { logger } from '../logger';

export class SettingsService {
    private wsHandler: any;

    constructor(wsHandler: any) {
        this.wsHandler = wsHandler;
    }

    async getSettings(userId: string): Promise<Settings | null> {
        const settings = await SettingsRepository.getSettings(userId);

        if (!settings) {
            // Create default settings
            return await this.createDefaultSettings(userId);
        }

        return settings;
    }

    async updateSettings(userId: string, categories: SettingsCategories): Promise<Settings> {
        const settings = await SettingsRepository.updateSettings(userId, categories);

        logger.info({ userId, version: settings.version }, 'Settings updated');

        // Broadcast settings change to all user's connected clients
        this.broadcastSettingsChange(userId, settings);

        return settings;
    }

    async mergeSettings(
        userId: string,
        localSettings: SettingsCategories,
        remoteSettings: SettingsCategories
    ): Promise<SettingsCategories> {
        // Simple merge strategy: remote wins for conflicts
        const merged = { ...localSettings };

        for (const category in remoteSettings) {
            if (typeof remoteSettings[category] === 'object' && !Array.isArray(remoteSettings[category])) {
                merged[category] = {
                    ...(merged[category] || {}),
                    ...remoteSettings[category],
                };
            } else {
                merged[category] = remoteSettings[category];
            }
        }

        return merged;
    }

    async syncSettings(
        userId: string,
        localSettings: SettingsCategories,
        localVersion: number
    ): Promise<{ settings: Settings; action: 'updated' | 'conflict_resolved' | 'up_to_date' }> {
        const remoteSettings = await this.getSettings(userId);

        if (!remoteSettings) {
            // No remote settings, upload local
            const settings = await this.updateSettings(userId, localSettings);
            return { settings, action: 'updated' };
        }

        if (remoteSettings.version === localVersion) {
            // Already up to date
            return { settings: remoteSettings, action: 'up_to_date' };
        }

        if (remoteSettings.version > localVersion) {
            // Remote is newer, return remote
            return { settings: remoteSettings, action: 'updated' };
        }

        // Local is newer or conflict, merge and update
        const merged = await this.mergeSettings(userId, localSettings, remoteSettings.categories);
        const settings = await this.updateSettings(userId, merged);

        return { settings, action: 'conflict_resolved' };
    }

    async getSettingsHistory(userId: string, limit: number = 10): Promise<Settings[]> {
        return await SettingsRepository.getSettingsHistory(userId, limit);
    }

    async createDefaultSettings(userId: string): Promise<Settings> {
        const defaultCategories: SettingsCategories = {
            general: {
                language: 'ja',
                theme: 'dark',
                notifications: true,
            },
            autoSuicide: {
                enabled: false,
                rules: [],
            },
            recording: {
                autoRecord: false,
                format: 'mp4',
                quality: 720,
            },
        };

        return await SettingsRepository.createSettings(userId, defaultCategories);
    }

    private broadcastSettingsChange(userId: string, settings: Settings): void {
        logger.debug({ userId, version: settings.version }, 'Broadcasting settings change');
        this.wsHandler.broadcastToUser(userId, {
            stream: 'settings.updated',
            data: settings,
            timestamp: new Date().toISOString(),
        });
    }
}

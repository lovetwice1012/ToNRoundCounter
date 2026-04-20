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
        baseSettings: SettingsCategories,
        overrideSettings: SettingsCategories
    ): Promise<SettingsCategories> {
        // Override values win for keys present in both. Caller decides which
        // side is the authoritative override (e.g. local-wins or remote-wins).
        const merged: SettingsCategories = { ...baseSettings };

        if (!overrideSettings || typeof overrideSettings !== 'object') {
            return merged;
        }

        for (const category in overrideSettings) {
            const overrideValue = (overrideSettings as any)[category];
            const baseValue = (merged as any)[category];
            if (
                overrideValue !== null &&
                typeof overrideValue === 'object' &&
                !Array.isArray(overrideValue) &&
                baseValue !== null &&
                typeof baseValue === 'object' &&
                !Array.isArray(baseValue)
            ) {
                (merged as any)[category] = {
                    ...baseValue,
                    ...overrideValue,
                };
            } else {
                (merged as any)[category] = overrideValue;
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

        // Local is newer than remote. Merge with local taking priority so the
        // user does not lose their newer changes to a stale remote copy.
        const merged = await this.mergeSettings(userId, remoteSettings.categories, localSettings);
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
        try {
            // Notify all websocket clients connected for this user so the UI
            // can refresh its synced state without polling.
            if (this.wsHandler && typeof this.wsHandler.broadcastToUser === 'function') {
                this.wsHandler.broadcastToUser(userId, {
                    stream: 'settings.updated',
                    data: {
                        user_id: userId,
                        version: settings.version,
                        categories: settings.categories,
                    },
                    timestamp: new Date().toISOString(),
                });
            }
        } catch (err) {
            logger.warn({ err, userId }, 'Failed to broadcast settings change');
        }
        logger.debug({ userId, version: settings.version }, 'Broadcasting settings change');
    }
}

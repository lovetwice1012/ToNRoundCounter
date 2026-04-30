/**
 * Coordinated Auto-Suicide Service
 * Stores instance-scoped shared skip targets and presets in instance.settings.
 */

import { v4 as uuidv4 } from 'uuid';
import InstanceRepository from '../repositories/InstanceRepository';
import {
    CoordinatedAutoSuicideEntry,
    CoordinatedAutoSuicidePreset,
    CoordinatedAutoSuicidePresetEntry,
    CoordinatedAutoSuicideState,
    InstanceSettings,
} from '../models/types';

const SETTINGS_KEY = 'coordinated_auto_suicide';

function normalizeText(value: unknown): string {
    return typeof value === 'string' ? value.trim() : '';
}

function normalizeKey(value: string): string {
    return value.trim().toLocaleLowerCase();
}

function isWildcardValue(value: string): boolean {
    const normalized = normalizeKey(value);
    return !normalized
        || normalized === '*'
        || normalized === 'all'
        || normalized === 'any'
        || normalized === 'all terrors'
        || normalized === 'all rounds'
        || normalized === '全'
        || normalized === '全部'
        || normalized === '全て'
        || normalized === 'すべて'
        || normalized === '全テラー'
        || normalized === '全ラウンド';
}

function normalizeTarget(value: string): string {
    return isWildcardValue(value) ? '' : value;
}

function hasScopedTarget(terrorName: string, roundKey: string): boolean {
    return !isWildcardValue(terrorName) || !isWildcardValue(roundKey);
}

function entryDuplicateKey(entry: { terror_name: string; round_key: string }): string {
    return `${normalizeKey(entry.terror_name)}::${normalizeKey(entry.round_key)}`;
}

function normalizePresetEntry(raw: any): CoordinatedAutoSuicidePresetEntry | null {
    const terrorName = normalizeText(raw?.terror_name);
    const roundKey = normalizeText(raw?.round_key);
    if (!hasScopedTarget(terrorName, roundKey)) {
        return null;
    }

    return {
        terror_name: normalizeTarget(terrorName),
        round_key: normalizeTarget(roundKey),
    };
}

function normalizeEntry(raw: any): CoordinatedAutoSuicideEntry | null {
    const terrorName = normalizeText(raw?.terror_name);
    const roundKey = normalizeText(raw?.round_key);
    if (!hasScopedTarget(terrorName, roundKey)) {
        return null;
    }

    return {
        id: normalizeText(raw?.id) || `coord_skip_${uuidv4()}`,
        terror_name: normalizeTarget(terrorName),
        round_key: normalizeTarget(roundKey),
        created_at: normalizeText(raw?.created_at) || new Date().toISOString(),
        created_by: normalizeText(raw?.created_by) || undefined,
        source: raw?.source === 'vote' ? 'vote' : 'manual',
    };
}

function normalizePreset(raw: any): CoordinatedAutoSuicidePreset | null {
    const name = normalizeText(raw?.name);
    if (!name) {
        return null;
    }

    const entries = Array.isArray(raw?.entries)
        ? raw.entries
            .map((entry: any) => normalizePresetEntry(entry))
            .filter((entry: CoordinatedAutoSuicidePresetEntry | null): entry is CoordinatedAutoSuicidePresetEntry => entry !== null)
        : [];

    return {
        id: normalizeText(raw?.id) || `coord_preset_${uuidv4()}`,
        name,
        entries,
        created_at: normalizeText(raw?.created_at) || new Date().toISOString(),
        created_by: normalizeText(raw?.created_by) || undefined,
    };
}

function normalizeState(raw: any): CoordinatedAutoSuicideState {
    const entries = Array.isArray(raw?.entries)
        ? raw.entries
            .map((entry: any) => normalizeEntry(entry))
            .filter((entry: CoordinatedAutoSuicideEntry | null): entry is CoordinatedAutoSuicideEntry => entry !== null)
        : [];

    const presets = Array.isArray(raw?.presets)
        ? raw.presets
            .map((preset: any) => normalizePreset(preset))
            .filter((preset: CoordinatedAutoSuicidePreset | null): preset is CoordinatedAutoSuicidePreset => preset !== null)
        : [];

    return {
        entries,
        presets,
        skip_all_without_survival_wish: Boolean(raw?.skip_all_without_survival_wish),
        updated_at: normalizeText(raw?.updated_at) || undefined,
        updated_by: normalizeText(raw?.updated_by) || undefined,
    };
}

export class CoordinatedAutoSuicideService {
    async getState(instanceId: string): Promise<CoordinatedAutoSuicideState> {
        const instance = await InstanceRepository.getInstance(instanceId);
        if (!instance) {
            throw new Error('Instance not found');
        }

        return normalizeState(instance.settings?.[SETTINGS_KEY]);
    }

    async replaceState(
        instanceId: string,
        state: Partial<CoordinatedAutoSuicideState> | undefined,
        updatedBy?: string,
    ): Promise<CoordinatedAutoSuicideState> {
        const instance = await InstanceRepository.getInstance(instanceId);
        if (!instance) {
            throw new Error('Instance not found');
        }

        const current = normalizeState(instance.settings?.[SETTINGS_KEY]);
        const next = normalizeState({
            ...current,
            ...state,
            updated_at: new Date().toISOString(),
            updated_by: normalizeText(updatedBy) || current.updated_by,
        });

        const nextSettings: InstanceSettings = {
            ...(instance.settings || {}),
            [SETTINGS_KEY]: next,
        };

        await InstanceRepository.updateSettings(instanceId, nextSettings);
        return next;
    }

    async addVoteSkipEntry(
        instanceId: string,
        terrorName: string,
        roundKey: string,
        createdBy?: string,
    ): Promise<CoordinatedAutoSuicideState> {
        const current = await this.getState(instanceId);
        const normalizedEntry = normalizeEntry({
            terror_name: terrorName,
            round_key: roundKey,
            created_by: createdBy,
            created_at: new Date().toISOString(),
            source: 'vote',
        });

        if (!normalizedEntry) {
            return current;
        }

        const duplicateKey = entryDuplicateKey(normalizedEntry);
        const hasDuplicate = current.entries.some((entry) => entryDuplicateKey(entry) === duplicateKey);
        if (hasDuplicate) {
            return current;
        }

        return await this.replaceState(instanceId, {
            ...current,
            entries: [...current.entries, normalizedEntry],
        }, createdBy);
    }
}

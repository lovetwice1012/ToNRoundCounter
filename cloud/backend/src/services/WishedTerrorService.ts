/**
 * Wished Terror Service
 * Handles wished terror management
 */

import { v4 as uuidv4 } from 'uuid';
import { getDatabase } from '../database/connection';
import { WishedTerror } from '../models/types';
import { logger } from '../logger';

export class WishedTerrorService {
    private db = getDatabase();

    private readonly terrorAliases = new Map<string, string>([
        ['allaroundhelpers', 'allaroundhelper'],
        ['mirosbirds', 'mirosbird'],
        ['booboobabies', 'booboobaby'],
    ]);

    private normalizeValue(value: string | null | undefined): string {
        return (value || '').trim().toLocaleLowerCase();
    }

    private canonicalizeTerrorName(value: string | null | undefined): string {
        const normalized = this.normalizeValue(value).replace(/[^a-z0-9]/g, '');
        if (!normalized) {
            return '';
        }

        if (this.terrorAliases.has(normalized)) {
            return this.terrorAliases.get(normalized)!;
        }

        if (normalized.endsWith('s')) {
            const singular = normalized.slice(0, -1);
            if (this.terrorAliases.has(singular)) {
                return this.terrorAliases.get(singular)!;
            }

            return singular;
        }

        return normalized;
    }

    private splitTerrorNames(terrorName: string): string[] {
        const candidates = new Set<string>();
        const fullName = this.canonicalizeTerrorName(terrorName);
        if (fullName) {
            candidates.add(fullName);
        }

        for (const part of terrorName.split(/\s+[&＆,，\/]\s+/)) {
            const candidate = this.canonicalizeTerrorName(part);
            if (candidate) {
                candidates.add(candidate);
            }
        }

        return Array.from(candidates);
    }
    private wsHandler: any;

    constructor(wsHandler: any) {
        this.wsHandler = wsHandler;
    }

    async updateWishedTerrors(playerId: string, wishedTerrors: any[]): Promise<void> {
        if (!Array.isArray(wishedTerrors)) {
            throw new Error('wished_terrors must be an array');
        }

        // Normalize each entry: clients send either bare strings ("Terror Name")
        // or objects ({ terror_name, round_key, id }). Reject malformed entries
        // up front instead of letting NOT NULL terror_name violations corrupt the
        // half-applied DELETE+INSERT below.
        const normalized: Array<{ id: string; terror_name: string; round_key: string }> = [];
        const seen = new Set<string>();
        for (const raw of wishedTerrors) {
            let terrorName: string | undefined;
            let roundKey = '';
            let id: string | undefined;

            if (typeof raw === 'string') {
                terrorName = raw;
            } else if (raw && typeof raw === 'object') {
                terrorName = typeof raw.terror_name === 'string' ? raw.terror_name : undefined;
                roundKey = typeof raw.round_key === 'string' ? raw.round_key : '';
                id = typeof raw.id === 'string' && raw.id.length > 0 ? raw.id : undefined;
            }

            terrorName = typeof terrorName === 'string' ? terrorName.trim() : terrorName;
            roundKey = typeof roundKey === 'string' ? roundKey.trim() : '';

            if (!terrorName || terrorName.length === 0) {
                logger.warn({ raw }, 'Skipping malformed wished terror entry');
                continue;
            }

            const dedupeKey = `${terrorName.toLocaleLowerCase()}::${roundKey.toLocaleLowerCase()}`;
            if (seen.has(dedupeKey)) {
                logger.debug({ playerId, terrorName, roundKey }, 'Skipping duplicate wished terror entry');
                continue;
            }
            seen.add(dedupeKey);

            normalized.push({
                id: id ?? uuidv4(),
                terror_name: terrorName,
                round_key: roundKey,
            });
        }

        // Atomic replace: do the whole rewrite in a single transaction so that a
        // mid-loop failure can't leave the player with an empty / partial wish list.
        await this.db.run('START TRANSACTION');
        try {
            await this.db.run(
                `DELETE FROM wished_terrors WHERE player_id = ?`,
                [playerId]
            );

            for (const entry of normalized) {
                await this.db.run(
                    `INSERT INTO wished_terrors (id, player_id, terror_name, round_key)
                     VALUES (?, ?, ?, ?)`,
                    [entry.id, playerId, entry.terror_name, entry.round_key]
                );
            }

            await this.db.run('COMMIT');
        } catch (err) {
            try {
                await this.db.run('ROLLBACK');
            } catch (rollbackErr) {
                logger.warn({ rollbackErr }, 'Failed to ROLLBACK wished_terrors update');
            }
            throw err;
        }

        logger.info({ playerId, count: normalized.length }, 'Wished terrors updated');
    }

    async getWishedTerrors(playerId: string): Promise<WishedTerror[]> {
        const rows = await this.db.all<any>(
            `SELECT * FROM wished_terrors WHERE player_id = ? ORDER BY created_at DESC`,
            [playerId]
        );

        return rows.map(row => ({
            id: row.id,
            player_id: row.player_id,
            terror_name: row.terror_name,
            round_key: row.round_key,
            created_at: new Date(row.created_at),
        }));
    }

    async findDesirePlayersForTerror(
        instanceId: string,
        terrorName: string,
        roundKey: string
    ): Promise<any[]> {
        logger.info({ instanceId, terrorName, roundKey }, 'Finding desire players for terror');
        const normalizedRoundKey = this.normalizeValue(roundKey);
        const normalizedTerrorNames = new Set(this.splitTerrorNames(terrorName));
        
        // Get all members of the instance
        const members = await this.db.all<any>(
            `SELECT player_id, player_name FROM instance_members
             WHERE instance_id = ? AND status = 'ACTIVE'`,
            [instanceId]
        );

        logger.info({ instanceId, memberCount: members.length, memberIds: members.map(m => m.player_id) }, 'Found instance members');

        const desirePlayers: any[] = [];

        for (const member of members) {
            // Check if player has wished for this terror
            const wishedTerrors = await this.getWishedTerrors(member.player_id);

            logger.debug({ 
                playerId: member.player_id, 
                wishedCount: wishedTerrors.length,
                wishedTerrors: wishedTerrors.map(w => ({ terror: w.terror_name, round: w.round_key }))
            }, 'Player wished terrors');

            const matched = wishedTerrors.some(wished => {
                const wishedTerrorName = this.canonicalizeTerrorName(wished.terror_name);
                const wishedRoundKey = this.normalizeValue(wished.round_key);

                // Terror name must match. Current terrorName may contain multiple
                // names joined for display (e.g. "A & B"), so match any token.
                if (!normalizedTerrorNames.has(wishedTerrorName)) {
                    return false;
                }

                // If round_key is empty, it matches all rounds
                if (!wishedRoundKey) {
                    return true;
                }

                // Otherwise, round_key must match exactly
                return wishedRoundKey === normalizedRoundKey;
            });

            if (matched) {
                logger.info({ playerId: member.player_id, terrorName, roundKey }, 'Player matched as desire player');
                desirePlayers.push({
                    player_id: member.player_id,
                    player_name: member.player_name,
                });
            }
        }

        logger.info({ instanceId, terrorName, roundKey, desireCount: desirePlayers.length, desireIds: desirePlayers.map(p => p.player_id) }, 'Desire players result');
        
        return desirePlayers;
    }
}

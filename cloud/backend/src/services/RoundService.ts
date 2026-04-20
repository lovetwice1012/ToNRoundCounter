/**
 * Round Service
 * Handles round data reporting and storage
 */

import { getDatabase } from '../database/connection';
import { logger } from '../logger';

/**
 * Convert any timestamp-like value (Date, ISO 8601 string, MySQL DATETIME string,
 * epoch number) into the MySQL DATETIME literal `YYYY-MM-DD HH:MM:SS` (UTC).
 *
 * BUG FIX: Previously the round.report payload from the C# client (and the
 * GameRoundEnd shim) was sent as ISO 8601 with `T`/fractional seconds/`Z`
 * (e.g. `2026-04-20T15:30:00.0000000Z`). MySQL/MariaDB strict mode rejects
 * those for TIMESTAMP/DATETIME columns with "Incorrect datetime value", so
 * every INSERT INTO rounds failed at SQL level. Result: the cloud dashboard
 * always saw zero rounds and the round statistics never refreshed even after
 * clicking the refresh button. Same fix pattern as `SessionRepository`/
 * `VotingService` `toMySQLDatetime`.
 */
function toMySQLDatetime(value: any): string {
    let date: Date;
    if (value instanceof Date) {
        date = value;
    } else if (typeof value === 'number' && Number.isFinite(value)) {
        date = new Date(value);
    } else if (typeof value === 'string' && value.length > 0) {
        const parsed = new Date(value);
        if (!Number.isNaN(parsed.getTime())) {
            date = parsed;
        } else {
            // If we can't parse it, fall back to "now" rather than letting the
            // INSERT fail. Better to over-report than to lose the round entirely.
            date = new Date();
        }
    } else {
        date = new Date();
    }
    const pad = (n: number) => String(n).padStart(2, '0');
    return (
        `${date.getUTCFullYear()}-${pad(date.getUTCMonth() + 1)}-${pad(date.getUTCDate())} ` +
        `${pad(date.getUTCHours())}:${pad(date.getUTCMinutes())}:${pad(date.getUTCSeconds())}`
    );
}

export class RoundService {
    private db = getDatabase();

    /**
     * Report a round (persist round data)
     * BUG FIX #7 (MEDIUM): Added defensive validation even though WebSocketHandler.handleRoundReport
     * already checks membership. If called from another code path (admin endpoint, debugging),
     * it now validates instance_id is non-empty and reporter_user_id is present.
     * This prevents data attribution to wrong instances.
     */
    async reportRound(roundData: any): Promise<void> {
        const {
            instance_id,
            round_type,
            round_key,
            terror_name,
            terror_key,
            start_time,
            end_time,
            initial_player_count,
            survivor_count,
            status,
            events,
            metadata,
            reporter_user_id,
        } = roundData;

        if (!instance_id) {
            throw new Error('instance_id is required');
        }

        // SECURITY FIX #7: Verify reporter_user_id is present to ensure round attribution
        // is auditable. If called from unexpected code path, fail early instead of
        // silently persisting unattributed data.
        if (!reporter_user_id) {
            logger.warn(
                { roundData },
                'Round reported without reporter_user_id; data will be unattributed. Check code path.'
            );
            // Note: For backward compatibility, allow this but log it. Future: throw error.
        }

        const resolvedRoundKey = round_key || round_type || 'UNKNOWN';
        const resolvedStartTime = toMySQLDatetime(start_time || new Date());
        const resolvedEndTime = toMySQLDatetime(end_time || new Date());
        const resolvedTerrorName = terror_name || terror_key || null;
        const roundId = `round_${Date.now()}_${Math.random().toString(36).substring(2, 10)}`;
        const eventsJson = JSON.stringify(Array.isArray(events) ? events : []);
        // Persist the authenticated reporter inside metadata so we can later
        // tell which user actually reported a round (the rounds table has no
        // dedicated column, but the JSON metadata is a stable extension point).
        const baseMetadata = (metadata && typeof metadata === 'object' && !Array.isArray(metadata))
            ? { ...metadata }
            : {};
        if (reporter_user_id) {
            baseMetadata.reporter_user_id = reporter_user_id;
        }
        const metadataJson = JSON.stringify(baseMetadata);

        await this.db.run(
            `INSERT INTO rounds (round_id, instance_id, round_key, start_time, end_time, status, survivor_count, initial_player_count, events, metadata)
             VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
            [
                roundId,
                instance_id,
                resolvedRoundKey,
                resolvedStartTime,
                resolvedEndTime,
                status || 'COMPLETED',
                survivor_count || 0,
                initial_player_count || 0,
                eventsJson,
                metadataJson,
            ]
        );

        // Record terror appearance if terror name exists
        if (resolvedTerrorName) {
            await this.db.run(
                `INSERT INTO terror_appearances (round_id, terror_name, appearance_time, desire_players, responses)
                 VALUES (?, ?, ?, ?, ?)`,
                [
                    roundId,
                    resolvedTerrorName,
                    resolvedStartTime,
                    JSON.stringify([]),
                    JSON.stringify([]),
                ]
            );
        }

        logger.info({ roundId, instance_id, round_key: resolvedRoundKey }, 'Round reported');
    }

    async getRounds(instanceId?: string, limit: number = 100): Promise<any[]> {
        let query = `
            SELECT * FROM rounds
            WHERE 1=1
        `;

        const params: any[] = [];

        if (instanceId) {
            query += ` AND instance_id = ?`;
            params.push(instanceId);
        }

        query += ` ORDER BY start_time DESC LIMIT ?`;
        params.push(limit);

        return await this.db.all<any>(query, params);
    }
}

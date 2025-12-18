/**
 * Wished Terror Panel Component
 * Manages player's wished terrors (survival preferences)
 */

import React, { useEffect, useState } from 'react';
import { useAppStore } from '../store/appStore';

interface WishedTerror {
    id: string;
    terror_name: string;
    round_key: string;
}

const TERROR_NAMES = [
    'Aku Ball',
    'Akumii-kari',
    'All-Around-Helpers',
    'An Arbiter',
    'Ao Oni',
    'Apocrean Harvester',
    'Arkus',
    'Arrival',
    'Astrum Aureus',
    'Bacteria',
    'Bad Batter',
    'Bed Mecha',
    'Beyond',
    'BFF',
    'Big Bird',
    'Bigger Boot',
    'Black Sun',
    'Bravera',
    'Cartoon Cat',
    '[CENSORED]',
    'Charlotte',
    'Christian Brutal Sniper',
    'Clockey',
    'Comedy',
    'Corrupted Toys',
    'Cubor\'s Revenge',
    'Deleted',
    'Demented Spongebob',
    'Dev Bytes',
    'Dog Mimic',
    'Don\'t Touch Me',
    'DoomBox',
    'Dr. Tox',
    'Express Train To Hell',
    'FOX Squad',
    'Garten Goers',
    'Ghost Girl',
    'Haket',
    'Harvest',
    'Hell Bell',
    'HER',
    'Herobrine',
    'HoovyDundy',
    'Horseless Headless Horsemann',
    'Huggy',
    'Hush',
    'Immortal Snail',
    'Imposter',
    'Ink Demon',
    'Judgement Bird',
    'Karol_Corpse',
    'Killer Fish',
    'Killer Rabbit',
    'Legs',
    'Living Shadow',
    'Luigi & Luigi Dolls',
    'Lunatic Cultist',
    'Malicious Twins',
    'Manti',
    'Maul-A-Child',
    'Miros Birds',
    'Mirror',
    'MissingNo',
    'Mona & The Mountain',
    'Mope Mope',
    'MX',
    'Nextbots',
    'Nosk',
    'Pale Association',
    'Parhelion\'s Victims',
    'Peepy',
    'Poly',
    'Prisoner',
    'Punishing Bird',
    'Purple Guy',
    'Red Bus',
    'Red Fanatic',
    'Retep',
    'Rush',
    'S.O.S',
    'Sakuya Izayoi',
    'Sawrunner',
    'Scavenger',
    'Security',
    'Seek',
    'Shinto',
    'Shiteyanyo',
    'Signus',
    'Slender',
    'Smileghost',
    'Snarbolax',
    'Something',
    'Something Wicked',
    'Sonic',
    'Spamton',
    'Specimen 2',
    'Specimen 5',
    'Specimen 8',
    'Specimen 10',
    'Spongefly Swarm',
    'Starved',
    'Sturm',
    'Tails Doll',
    'TBH',
    'Terror of Nowhere',
    'The Boys',
    'The Guidance',
    'The Jester',
    'The Lifebringer',
    'The Origin',
    'The Painter',
    'The Plague Doctor',
    'The Pursuer',
    'The Rat',
    'The Swarm',
    'Those Olden Days',
    'Tiffany',
    'Time Ripper',
    'Tinky Winky',
    'Toren\'s Shadow',
    'Toy Enforcer',
    'Tricky',
    'V2',
    'Waldo',
    'Warden',
    'Wario Apparition',
    'Waterwraith',
    'Wild Yet Curious Creature',
    'With Many Voices',
    'Withered Bonnie',
    'WhiteNight',
    'Yolm',
    'lain',
    'This Killer Does Not Exist',
    ' ',
    ',D@;Q7Y',
    'Ambush',
    'Angry Munci',
    'Apathy',
    'Army In Black',
    'Lone Agent',
    'Chomper',
    'Convict Squad',
    'Decayed Sponge',
    'Dev Maulers',
    'Eggman\'s Announcement',
    'Feddys',
    'Fusion Pilot',
    'Glaggle Gang',
    'Judas',
    'Joy',
    'Lord\'s Signal',
    'MR.MEGA',
    'Paradise Bird',
    'Parhelion',
    'Restless Creator',
    'Roblander',
    'Sakuya The Ripper',
    'Sanic',
    'sm64.z64',
    'S.T.G.M',
    'TBH SPY',
    'Teuthida',
    'The Knight Of Toren',
    'The Observation',
    'The Red Mist',
    'Tragedy',
    'Try Not To Touch Me',
    'Walpurgisnacht',
    'WHITEFACE',
    'Psychosis',
    'Virus',
    'APOCALYPSE BIRD',
    'Pandora',
    'Atrached',
    'Hungry Home Invader',
    'Wild Yet Bloodthirsty Creature',
    'The MeatBallMan',
    'Rift Monsters',
    'Neo Pilot',
    'GIGABYTE',
    'Evil Purple Foxy',
    'Transportation Trio ＆ The Drifter',
    'Alternates',
    'Baldi',
    'Red Mist Apparition',
    'Searchlights',
    'Shadow Freddy',
    'Specimen 9',
    'Interloper',
    'Beyond Plush',
    'OH NO',
    'Epic Bonnie',
    'TBH SANS',
    'Blue Haket',
    'Eyes',
    'Inverted Roblander',
    'Kimera',
    'Monarch',
    'Nameless',
    'Rabid Snarbolax',
    'Rewrite',
    'Ruinborn Afton',
    'Scrapyard Machine',
    'Search and Destroy',
    'Slendy',
    'The Batter',
];

export const WishedTerrorPanel: React.FC = () => {
    const { client, playerId } = useAppStore();
    const [wishedTerrors, setWishedTerrors] = useState<WishedTerror[]>([]);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [selectedTerrors, setSelectedTerrors] = useState<string[]>([]);
    const [newRoundKey, setNewRoundKey] = useState('');
    const [searchQuery, setSearchQuery] = useState('');

    useEffect(() => {
        if (client && playerId) {
            loadWishedTerrors();
        }
    }, [client, playerId]);

    const loadWishedTerrors = async () => {
        if (!client || !playerId) return;

        setLoading(true);
        setError(null);
        try {
            const result = await client.getWishedTerrors(playerId);
            setWishedTerrors(result as any);
        } catch (error) {
            console.error('Failed to load wished terrors:', error);
            setError('ほしいテラーの読み込みに失敗しました');
        } finally {
            setLoading(false);
        }
    };

    const handleAdd = async () => {
        if (!client || !playerId) return;
        if (selectedTerrors.length === 0) {
            setError('テラーを1つ以上選択してください');
            return;
        }

        setError(null);
        try {
            const newItems = selectedTerrors.map(terrorName => ({
                id: crypto.randomUUID(),
                terror_name: terrorName,
                round_key: newRoundKey,
            }));

            const newList = [...wishedTerrors, ...newItems];

            await client.updateWishedTerrors(playerId, newList as any);
            setWishedTerrors(newList);
            setSelectedTerrors([]);
            setNewRoundKey('');
        } catch (error) {
            console.error('Failed to add wished terror:', error);
            setError('追加に失敗しました');
        }
    };

    const handleToggleTerror = (terrorName: string) => {
        setSelectedTerrors(prev => {
            if (prev.includes(terrorName)) {
                return prev.filter(t => t !== terrorName);
            } else {
                return [...prev, terrorName];
            }
        });
    };

    const handleSelectAll = () => {
        const filteredTerrors = getFilteredTerrors();
        if (selectedTerrors.length === filteredTerrors.length) {
            setSelectedTerrors([]);
        } else {
            setSelectedTerrors(filteredTerrors);
        }
    };

    const getFilteredTerrors = () => {
        if (!searchQuery) return TERROR_NAMES;
        return TERROR_NAMES.filter(name => 
            name.toLowerCase().includes(searchQuery.toLowerCase())
        );
    };

    const handleRemove = async (id: string) => {
        if (!client || !playerId) return;

        setError(null);
        try {
            const newList = wishedTerrors.filter(t => t.id !== id);
            await client.updateWishedTerrors(playerId, newList as any);
            setWishedTerrors(newList);
        } catch (error) {
            console.error('Failed to remove wished terror:', error);
            setError('削除に失敗しました');
        }
    };

    if (loading && wishedTerrors.length === 0) {
        return <div className="loading">ほしいテラーを読み込んでいます...</div>;
    }

    return (
        <div className="wished-terror-panel">
            <h2>ほしいテラー設定</h2>
            <p className="description">
                生存を希望するテラーを登録しておくと、そのテラーが出現したときに他のプレイヤーに通知されます。
            </p>

            {error && (
                <div className="error-message">
                    {error}
                    <button onClick={() => setError(null)} className="btn-dismiss">×</button>
                </div>
            )}

            <div className="add-section">
                <h3>新規追加</h3>
                <div className="add-form">
                    <input
                        type="text"
                        value={searchQuery}
                        onChange={(e) => setSearchQuery(e.target.value)}
                        placeholder="テラー名で検索..."
                        className="input-search"
                    />

                    <input
                        type="text"
                        value={newRoundKey}
                        onChange={(e) => setNewRoundKey(e.target.value)}
                        placeholder="ラウンド名 (空白=全ラウンド)"
                        className="input-round-key"
                    />
                </div>

                <div className="terror-selection">
                    <div className="selection-header">
                        <button 
                            onClick={handleSelectAll} 
                            className="btn-select-all"
                            type="button"
                        >
                            {selectedTerrors.length === getFilteredTerrors().length && getFilteredTerrors().length > 0
                                ? 'すべて解除'
                                : 'すべて選択'}
                        </button>
                        <span className="selection-count">
                            {selectedTerrors.length}件選択中
                        </span>
                    </div>

                    <div className="terror-checkbox-list">
                        {getFilteredTerrors().map(name => (
                            <label key={name} className="terror-checkbox-item">
                                <input
                                    type="checkbox"
                                    checked={selectedTerrors.includes(name)}
                                    onChange={() => handleToggleTerror(name)}
                                />
                                <span className="terror-name">{name}</span>
                            </label>
                        ))}
                    </div>

                    <button 
                        onClick={handleAdd} 
                        className="btn-add-selected" 
                        disabled={loading || selectedTerrors.length === 0}
                    >
                        選択した{selectedTerrors.length}件を追加
                    </button>
                </div>
            </div>

            <div className="list-section">
                <h3>登録済み ({wishedTerrors.length}件)</h3>
                {wishedTerrors.length === 0 ? (
                    <p className="empty-message">まだ登録されていません</p>
                ) : (
                    <table className="wished-terror-table">
                        <thead>
                            <tr>
                                <th>テラー名</th>
                                <th>ラウンド</th>
                                <th>操作</th>
                            </tr>
                        </thead>
                        <tbody>
                            {wishedTerrors.map(terror => (
                                <tr key={terror.id}>
                                    <td>{terror.terror_name}</td>
                                    <td>{terror.round_key || '全ラウンド'}</td>
                                    <td>
                                        <button
                                            onClick={() => handleRemove(terror.id)}
                                            className="btn-remove"
                                            disabled={loading}
                                        >
                                            削除
                                        </button>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                )}
            </div>
        </div>
    );
};

using System;
using System.Collections.Generic;
using System.Linq;

namespace ToNRoundCounter.Domain
{
    /// <summary>
    /// Lookup table for Unbound round names and their terror compositions.
    /// </summary>
    public static class UnboundRoundDefinitions
    {
        private static readonly Dictionary<string, (string name, int count)[]> _map =
            new Dictionary<string, (string, int)[]>(StringComparer.OrdinalIgnoreCase);

        static UnboundRoundDefinitions()
        {
            Add("Guidance &\nThe Booboo's", new[] { ("The Guidance", 2), ("BooBooBabies", 3) });
            Add("Red vs Blue", new[] { ("Haket", 1), ("Blue Haket", 1) });
            Add("Third Trumpet", new[] { ("CENSORED", 1), ("All-around-Helper", 2), ("Mountain of Smiling Bodies", 1), ("Army in Black", 1), ("Big Bird", 1), ("Express Train To Hell", 1) });
            Add("Forest Gurdians", new[] { ("Big Bird", 1), ("Judgement Bird", 1), ("Punishing Bird", 1) });
            Add("Higher Beings", new[] { ("Security", 1), ("The Swarm", 1), ("Prisoner", 1) });
            Add("Quadruple Sponge", new[] { ("Demented Spongebob", 1), ("Spongefly Swarm", 1), ("Decayed Sponge", 1), ("S.T.G.M", 1) });
            Add("Your Best Friends", new[] { ("BFF", 2), ("Nameless", 1) });
            Add("Hotel Monsters", new[] { ("Seek", 1), ("Rush", 1), ("Eyes", 1) });
            Add("Squibb Squad", new[] { ("Squibbs (Dev Bytes)", 3), ("Convict(D)", 1) });
            Add("Garden Rejects", new[] { ("Convict Squad", 1), ("Kimera", 1), ("Search and Destroy", 1) });
            Add("Judgement Day", new[] { ("WhiteNight", 1), ("Paradise Bird", 1) });
            Add("Me and My Shadow", new[] { ("Roblander", 1), ("Inverted Roblander", 1) });
            Add("Meltdown", new[] { ("An Arbiter", 1), ("The Red Mist", 1) });
            Add("Faceless Mafia", new[] { ("Slender", 1), ("Slendy", 1), ("Hungry Home Invader", 1) });
            Add("Mansion Monsters", new[] { ("Specimen 2", 1), ("Specimen 5", 1), ("Specimen 8", 1), ("Specimen 10", 1) });
            Add("Copyright infringement", new[] { ("MX", 1), ("Luigi", 1), ("Wario Apparition", 1) });
            Add("Purple Bros", new[] { ("Purple Guy", 2) });
            Add("Scavengers", new[] { ("Scavenger", 3) });
            Add("Life & Death", new[] { ("The LifeBringer", 1), ("Scrapyard Machine", 1) });
            Add("Labyrinth", new[] { ("Unknown Witch", 7) });
            Add("Spiteful Shadows", new[] { ("Umbra", 2), ("Spiteful Eye", 2) });
            Add("Triple Munci", new[] { ("Angry Munci", 3) });
            Add("Daycare", new[] { ("Karol_Corpse", 3) });
            Add("Huggy Horde", new[] { ("Huggy", 3) });
            Add("Infection", new[] { ("Arrival", 3) });
            Add("Triple Hush", new[] { ("Hush", 3) });
            Add("[CENSORED]", new[] { ("[CENSORED]", 3) });
            Add("Byte Horde", new[] { ("Dev Bytes", 1), ("Vana", 1), ("Duke", 1) });
            Add("SawMarathon", new[] { ("Sawrunner", 3) });
            Add("TAKE THE NAMI CHALLENGE", new[] { ("Little Witch", 2), ("War", 1), ("Convict(N)", 1) });
            Add("Thunderstorm", new[] { ("Lightning", 7) });
            Add("END OF THE WORLD", new[] { ("Joy", 3) });
            Add("Fragmented Memories", new[] { ("TOREN_SHADOW", 1), ("WITH_MANY_VOICES", 1), ("ETRIGAN", 1), ("PRINCESS", 1), ("SMILEY", 1), ("IMPOSTER", 1) });
            Add("Mona & Mona &\nMona & Mona", new[] { ("Mona", 4) });
            Add("Seekers", new[] { ("Legs", 3) });
            Add("Nugget Squad", new[] { ("Convict(N)", 4) });
            Add("Saul's goodman", new[] { ("Saul", 5) });
            Add("Something Old,\nSomething New", new[] { ("Security", 1), ("Ancient Security", 1) });
            Add("POV:Bug", new[] { ("Bigger Boot", 3) });
            Add("Punishing Birdemic", new[] { ("Punishing Bird", 5) });
            Add("Double Ao Oni", new[] { ("Ao Oni", 2) });
            Add("Too Many Voices", new[] { ("With Many Voices", 6) });
            Add("Memory Crypts", new[] { ("Miros Bird", 5) });
            Add("Zumbo Sauce", new[] { ("Jumbo Josh", 3) });
            Add("Freaks", new[] { ("Christian Brutal Sniper", 1), ("HoovyDundy", 1), ("Horseless Headless Horsemann", 1) });
            Add("Lunatic Cult", new[] { ("Lunatic Cultist", 3) });
            Add("Transportation Trio\n& The Drifter", new[] { ("Red Bus", 1), ("Blue Bus", 1), ("Green Bus", 1), ("Yellow Bus", 1) });
            Add("Father Son Bonding", new[] { ("Purple Guy", 1), ("Voidwalker", 1) });
            Add("What is My NAME", new[] { ("HER", 1), ("WHITEFACE", 1) });
            Add("Glaggleland Cremators", new[] { ("Dapper Enphoso", 3), ("Pyromatic Enphoso", 3) });
            Add("Triple Signus", new[] { ("Signus", 3) });
            Add("Triple Akumii-Kari", new[] { ("Akumii-Kari", 3) });
            Add("Black & White", new[] { ("Big Bird", 1), ("Paradise Bird", 1) });
            Add("[LESSER CENSORED]", new[] { ("LESSER CENSORED", 9) });
            Add("Blue Monsters", new[] { ("Blue gaper", 5), ("Blue Flies", 3) });
            Add("Drones", new[] { ("Drone", 5) });
            Add("Scrapyard Takers", new[] { ("Scrapyard Taker", 4) });
            Add("Luigi Dolls", new[] { ("Luigi Doll", 5) });
            Add("Meteor Shower", new[] { ("Meteor", 1) });
            Add("Triple TBH", new[] { ("TBH", 3) });
            Add("Lost Souls", new[] { ("Lost Soul", 5) });
            Add("Ballin", new[] { ("Aku Ball", 3) });
            Add("Reunion", new[] { ("Toren's Shadow", 1), ("Mona", 1), ("Karol_Corpse", 1), ("Haket", 1) });
            Add("Angels", new[] { ("Roblander", 1), ("Bliss", 1), ("Ancient Monarch", 1) });
            Add("Ordinary Apocalypse Bird", new[] { ("Apocalypse Bird", 1), ("Immortal Snail", 1) });
            Add("Pack of\nWild Yet Curious Creatures", new[] { ("Wild Yet Curious Creature", 3) });
            Add("ToN X SlashCo Collab", new[] { ("Beyond", 1), ("Manti", 1) });
            Add("Pack of Yolm", new[] { ("Yolm", 3) });
            Add("Threepy", new[] { ("Peepy", 3) });
            Add("???", new[] { ("Ghost Girl", 3) });
            Add("Delete Me", new[] { ("Deleted", 1), ("Akumii-Kari", 1) });
            Add("Spamton Spam", new[] { ("Spamton", 3) });
            Add("Death from above", new[] { ("Solstice Eye", 1), ("Bigger Boot", 1), ("AXE", 1), ("Meteor", 1), ("Nabnab", 1), ("Giant Laser", 1) });
            Add("It Came From\nBus To Nowhere", new[] { ("Mirror", 1), ("Red Bus", 1), ("Terror of Nowhere", 1) });
            Add("Zombie Apocalypse", new[] { ("SCP-049-B", 6) });
            Add("Eating Contest", new[] { ("Mountain of Smiling Bodies", 2) });
            Add("Triple Clockey", new[] { ("Clockey", 3) });
            Add("Triple KillerFish", new[] { ("Killer Fish", 3) });
            Add("Lethal League", new[] { ("MR.MEGA", 1), ("DoomBox", 1) });
            Add("Trollge", new[] { ("Comedy", 1), ("Tragedy", 1) });
            Add("Mopemopemopemopemopemope", new[] { ("MopeMope", 3) });
            Add("Triple Trouble", new[] { ("Sonic", 1), ("Rewrite", 1), ("Atrached", 1) });
            Add("Triple Living Shadow", new[] { ("Living Shadow", 3) });
            Add("Beyond's Masks", new[] { ("Beyond", 1), ("Convict[E]", 1), ("Byte[E]", 1), ("Wild Yet Curious Creature", 1), ("Toren's Shadow", 1), ("Paint(Beyond)", 1) });
        }

        private static void Add(string name, (string, int)[] terrors)
        {
            _map[Normalize(name)] = terrors;
        }

        private static string Normalize(string name)
            => name.Replace("\r", "").Replace("\n", " ").Trim();

        public static IReadOnlyList<(string name, int count)>? GetTerrors(string name)
        {
            if (_map.TryGetValue(Normalize(name), out var terrors))
                return terrors;
            return null;
        }

        public static string? GetTerrorDisplay(string name)
        {
            var terrors = GetTerrors(name);
            if (terrors == null) return null;
            return string.Join(", ", terrors.Select(t => $"{t.name} x{t.count}"));
        }

        public static List<string>? GetTerrorNames(string name)
        {
            var terrors = GetTerrors(name);
            if (terrors == null) return null;
            var list = new List<string>();
            foreach (var t in terrors)
            {
                for (int i = 0; i < t.count; i++)
                    list.Add(t.name);
            }
            return list;
        }
    }
}

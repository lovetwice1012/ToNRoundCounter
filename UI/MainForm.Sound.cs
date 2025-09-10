using System;
using System.Windows.Media;

namespace ToNRoundCounter.UI
{
    public partial class MainForm
    {
        private static MediaPlayer CreatePlayer(string path)
        {
            var player = new MediaPlayer();
            player.Open(new Uri(path, UriKind.Relative));
            return player;
        }

        private static void PlayFromStart(MediaPlayer player)
        {
            player.Position = TimeSpan.Zero;
            player.Play();
        }

        private readonly MediaPlayer notifyPlayer = CreatePlayer("./audio/notify.mp3");
        private readonly MediaPlayer afkPlayer = CreatePlayer("./audio/afk70.mp3");
        private readonly MediaPlayer punishPlayer = CreatePlayer("./audio/punish_8page.mp3");
        private readonly MediaPlayer tester_roundStartAlternatePlayer = CreatePlayer("./audio/testerOnly/RoundStart/alternate.mp3");
        private readonly MediaPlayer tester_IDICIDEDKILLALLPlayer = CreatePlayer("./audio/testerOnly/RoundStart/IDICIDEDKILLALL.mp3");
        private readonly MediaPlayer tester_BATOU_01Player = CreatePlayer("./audio/testerOnly/Batou/Batou-01.mp3");
        private readonly MediaPlayer tester_BATOU_02Player = CreatePlayer("./audio/testerOnly/Batou/Batou-02.mp3");
        private readonly MediaPlayer tester_BATOU_03Player = CreatePlayer("./audio/testerOnly/Batou/Batou-03.mp3");

        private void InitializeSoundPlayers()
        {
            notifyPlayer.Stop();
            afkPlayer.Stop();
            punishPlayer.Stop();
            tester_roundStartAlternatePlayer.Stop();
            tester_IDICIDEDKILLALLPlayer.Stop();
            tester_BATOU_01Player.Stop();
            tester_BATOU_02Player.Stop();
            tester_BATOU_03Player.Stop();
        }
    }
}

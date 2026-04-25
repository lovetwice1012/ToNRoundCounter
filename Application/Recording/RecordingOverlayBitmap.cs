#nullable enable

using System.Drawing;

namespace ToNRoundCounter.Application.Recording
{
    internal readonly struct RecordingOverlayBitmap
    {
        public RecordingOverlayBitmap(Bitmap bitmap, Point screenLocation)
        {
            Bitmap = bitmap;
            ScreenLocation = screenLocation;
        }

        public Bitmap Bitmap { get; }
        public Point ScreenLocation { get; }
    }
}

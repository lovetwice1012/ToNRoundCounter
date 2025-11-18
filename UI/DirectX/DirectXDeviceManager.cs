using System;
using Vortice.Direct2D1;
using Vortice.DirectWrite;

namespace ToNRoundCounter.UI.DirectX
{
    internal sealed class DirectXDeviceManager : IDisposable
    {
        private static readonly Lazy<DirectXDeviceManager> InstanceFactory = new(() => new DirectXDeviceManager());

        public static DirectXDeviceManager Instance => InstanceFactory.Value;

        private bool disposed;

        private DirectXDeviceManager()
        {
            Direct2DFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(FactoryType.SingleThreaded);
            DirectWriteFactory = DWrite.DWriteCreateFactory<IDWriteFactory>();
        }

        public ID2D1Factory1 Direct2DFactory { get; }

        public IDWriteFactory DirectWriteFactory { get; }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            DirectWriteFactory?.Dispose();
            Direct2DFactory?.Dispose();
        }
    }
}

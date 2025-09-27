using System;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;

namespace ToNRoundCounter.UI.DirectX
{
    internal sealed class DirectXDeviceManager : IDisposable
    {
        private static readonly Lazy<DirectXDeviceManager> InstanceFactory = new(() => new DirectXDeviceManager());

        public static DirectXDeviceManager Instance => InstanceFactory.Value;

        private bool disposed;

        private DirectXDeviceManager()
        {
            Direct2DFactory = new Factory1(FactoryType.SingleThreaded);
            DirectWriteFactory = new Factory();
        }

        public Factory1 Direct2DFactory { get; }

        public Factory DirectWriteFactory { get; }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            DirectWriteFactory.Dispose();
            Direct2DFactory.Dispose();
        }
    }
}

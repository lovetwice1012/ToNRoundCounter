using System;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
using D2DFactory1 = SharpDX.Direct2D1.Factory1;
using DWFactory = SharpDX.DirectWrite.Factory;

namespace ToNRoundCounter.UI.DirectX
{
    internal sealed class DirectXDeviceManager : IDisposable
    {
        private static readonly Lazy<DirectXDeviceManager> InstanceFactory = new(() => new DirectXDeviceManager());

        public static DirectXDeviceManager Instance => InstanceFactory.Value;

        private bool disposed;

        private DirectXDeviceManager()
        {
            Direct2DFactory = new D2DFactory1(FactoryType.SingleThreaded);
            DirectWriteFactory = new DWFactory();
        }

        public D2DFactory1 Direct2DFactory { get; }

        public DWFactory DirectWriteFactory { get; }

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

using System.Runtime.InteropServices;
using ToNRoundCounter.Application;

namespace ToNRoundCounter.Infrastructure
{
    public class NativeInputSender : IInputSender
    {
        [DllImport("ton-self-kill", EntryPoint = "press_keys", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern void press_keys();

        public void PressKeys()
        {
            press_keys();
        }
    }
}

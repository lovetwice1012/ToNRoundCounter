using System;
using System.Linq;
using System.Windows.Forms;
using ToNRoundCounter.Application;
using WinFormsApp = System.Windows.Forms.Application;

namespace ToNRoundCounter.Infrastructure
{
    /// <summary>
    /// WinForms implementation of <see cref="IUiDispatcher"/> that marshals calls onto the UI thread.
    /// </summary>
    public class WinFormsDispatcher : IUiDispatcher
    {
        public void Invoke(Action action)
        {
            if (WinFormsApp.OpenForms.Count > 0)
            {
                var form = WinFormsApp.OpenForms.Cast<Form>().First();
                if (form.InvokeRequired)
                {
                    form.BeginInvoke(action);
                }
                else
                {
                    action();
                }
            }
            else
            {
                action();
            }
        }
    }
}

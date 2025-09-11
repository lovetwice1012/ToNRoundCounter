using System;
using System.Windows.Forms;
using ToNRoundCounter.Application;

namespace ToNRoundCounter.Infrastructure
{
    /// <summary>
    /// WinForms implementation of <see cref="IUiDispatcher"/> that marshals calls onto the UI thread.
    /// </summary>
    public class WinFormsDispatcher : IUiDispatcher
    {
        private Form? _mainForm;

        public void SetMainForm(Form form)
        {
            _mainForm = form;
        }

        public void Invoke(Action action)
        {
            var form = _mainForm;
            if (form == null || form.IsDisposed)
            {
                return;
            }

            if (!form.IsHandleCreated)
            {
                return;
            }

            if (form.InvokeRequired)
            {
                form.BeginInvoke(action);
            }
            else
            {
                action();
            }
        }
    }
}

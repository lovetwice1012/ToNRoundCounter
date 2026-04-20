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
            
            // No main form has been registered yet - we are still wiring things up on the UI thread
            if (form == null)
            {
                action();
                return;
            }

            // Main form has been disposed - just run inline
            try
            {
                if (form.IsDisposed)
                {
                    action();
                    return;
                }
            }
            catch
            {
                // If IsDisposed property throws, form is in an invalid state - run inline
                action();
                return;
            }

            // Once the handle exists we can safely check InvokeRequired and marshal if needed
            if (form.IsHandleCreated)
            {
                try
                {
                    if (form.InvokeRequired)
                    {
                        form.BeginInvoke(action);
                    }
                    else
                    {
                        action();
                    }
                }
                catch
                {
                    // If form is in invalid state, run inline
                    action();
                }
                return;
            }

            // Handle has not been created yet (we are still inside the form's constructor or
            // before Show()/Load fires). InvokeRequired is meaningless without a handle, but the
            // caller is necessarily executing on the same thread that will own the UI loop, so
            // running the action inline is safe and required - otherwise initialization work
            // (e.g. OverlayManager.Initialize) would be silently dropped.
            action();
        }
    }
}

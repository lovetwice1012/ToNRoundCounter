using System.Windows.Forms;

namespace ToNRoundCounter.UI
{
    /// <summary>
    /// Helper methods for displaying common dialogs and messages
    /// </summary>
    public static class DialogHelper
    {
        /// <summary>
        /// Shows an error dialog with standard formatting
        /// </summary>
        /// <param name="message">Error message to display</param>
        /// <param name="title">Dialog title (defaults to "エラー")</param>
        public static void ShowError(string message, string? title = null)
        {
            MessageBox.Show(
                message,
                title ?? "エラー",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }

        /// <summary>
        /// Shows a warning dialog with standard formatting
        /// </summary>
        /// <param name="message">Warning message to display</param>
        /// <param name="title">Dialog title (defaults to "警告")</param>
        public static void ShowWarning(string message, string? title = null)
        {
            MessageBox.Show(
                message,
                title ?? "警告",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
        }

        /// <summary>
        /// Shows an information dialog with standard formatting
        /// </summary>
        /// <param name="message">Information message to display</param>
        /// <param name="title">Dialog title (defaults to "情報")</param>
        public static void ShowInfo(string message, string? title = null)
        {
            MessageBox.Show(
                message,
                title ?? "情報",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

        /// <summary>
        /// Shows a success dialog with standard formatting
        /// </summary>
        /// <param name="message">Success message to display</param>
        /// <param name="title">Dialog title (defaults to "成功")</param>
        public static void ShowSuccess(string message, string? title = null)
        {
            MessageBox.Show(
                message,
                title ?? "成功",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

        /// <summary>
        /// Shows a confirmation dialog and returns true if user confirms
        /// </summary>
        /// <param name="message">Confirmation message to display</param>
        /// <param name="title">Dialog title (defaults to "確認")</param>
        /// <returns>True if user clicked Yes, false otherwise</returns>
        public static bool ShowConfirmation(string message, string? title = null)
        {
            var result = MessageBox.Show(
                message,
                title ?? "確認",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );
            return result == DialogResult.Yes;
        }

        /// <summary>
        /// Shows a validation error for input fields
        /// </summary>
        /// <param name="message">Validation error message</param>
        public static void ShowInputError(string message)
        {
            MessageBox.Show(
                message,
                "入力エラー",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
        }

        /// <summary>
        /// Shows an exception error dialog with formatted message
        /// </summary>
        /// <param name="operation">The operation that failed (e.g., "バックアップの作成")</param>
        /// <param name="ex">The exception that occurred</param>
        public static void ShowException(string operation, System.Exception ex)
        {
            MessageBox.Show(
                $"{operation}に失敗しました: {ex.Message}",
                "エラー",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
    }
}

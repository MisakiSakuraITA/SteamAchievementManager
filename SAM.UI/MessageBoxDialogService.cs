/* Copyright (c) 2024 Rick (rick 'at' gibbed 'dot' us)
 *
 * This software is provided 'as-is', without any express or implied
 * warranty. In no event will the authors be held liable for any damages
 * arising from the use of this software.
 *
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter it and redistribute it
 * freely, subject to the following restrictions:
 *
 * 1. The origin of this software must not be misrepresented; you must not
 *    claim that you wrote the original software. If you use this software
 *    in a product, an acknowledgment in the product documentation would
 *    be appreciated but is not required.
 *
 * 2. Altered source versions must be plainly marked as such, and must not
 *    be misrepresented as being the original software.
 *
 * 3. This notice may not be removed or altered from any source
 *    distribution.
 */

using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using SAM.Core.ViewModels;

namespace SAM.UI
{
    /// <summary>
    /// The shell's <see cref="IDialogService"/>: shows a Win32 message box or file dialog and
    /// reports back what the user chose.
    /// </summary>
    public sealed class MessageBoxDialogService : IDialogService
    {
        private const string _SnapshotFilter =
            "Snapshot files (*.json;*.csv)|*.json;*.csv|JSON snapshot (*.json)|*.json|CSV snapshot (*.csv)|*.csv|All files (*.*)|*.*";

        public Task<bool> ShowConfirmationAsync(string title, string message, DialogSeverity severity)
        {
            var icon = severity switch
            {
                DialogSeverity.Question => MessageBoxImage.Question,
                DialogSeverity.Error => MessageBoxImage.Error,
                _ => MessageBoxImage.Warning,
            };

            var result = MessageBox.Show(message, title, MessageBoxButton.YesNo, icon);
            return Task.FromResult(result == MessageBoxResult.Yes);
        }

        public Task<string> ShowSaveFileAsync(string suggestedFileName)
        {
            var dialog = new SaveFileDialog
            {
                FileName = suggestedFileName,
                Filter = _SnapshotFilter,
                DefaultExt = ".json",
                AddExtension = true,
            };

            return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
        }

        public Task<string> ShowOpenFileAsync()
        {
            var dialog = new OpenFileDialog
            {
                Filter = _SnapshotFilter,
                CheckFileExists = true,
            };

            return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
        }
    }
}

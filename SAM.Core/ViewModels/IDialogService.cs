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

namespace SAM.Core.ViewModels
{
    /// <summary>How a confirmation should present itself to the user.</summary>
    public enum DialogSeverity
    {
        Warning,
        Question,
        Error,
    }

    /// <summary>
    /// Everything a view model needs to ask the user a yes/no question, without depending on a
    /// presentation framework to do it.
    /// </summary>
    /// <remarks>
    /// A view model that shows a message box directly cannot be exercised without a display; a
    /// view model that raises a synchronous event and waits for the shell to answer it can, but
    /// the answer has to be wired up by hand for every test. A fake <see cref="IDialogService"/>
    /// makes a confirmation flow -- including a multi-step one, such as a reset -- just another
    /// sequence of calls a test can script and assert against.
    /// </remarks>
    public interface IDialogService
    {
        /// <summary>Shows a yes/no question and returns whether the user chose yes.</summary>
        Task<bool> ShowConfirmationAsync(string title, string message, DialogSeverity severity);

        /// <summary>
        /// Asks where to save a new file. Returns the chosen path, or null if the user
        /// cancelled.
        /// </summary>
        Task<string> ShowSaveFileAsync(string suggestedFileName);

        /// <summary>Asks for an existing file to open. Returns null if the user cancelled.</summary>
        Task<string> ShowOpenFileAsync();
    }
}

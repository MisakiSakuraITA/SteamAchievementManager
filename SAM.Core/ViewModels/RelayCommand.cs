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

using System;
using System.Threading.Tasks;
using System.Windows.Input;
using SAM.Core.Threading;

namespace SAM.Core.ViewModels
{
    /// <summary>
    /// A command backed by a delegate.
    /// </summary>
    public sealed class RelayCommand : ICommand
    {
        private readonly Action<object> _Execute;
        private readonly Func<object, bool> _CanExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
            : this(
                _ => execute(),
                canExecute == null ? null : new Func<object, bool>(_ => canExecute()))
        {
        }

        public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
        {
            this._Execute = execute ?? throw new ArgumentNullException(nameof(execute));
            this._CanExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter) => this._CanExecute == null || this._CanExecute(parameter);

        public void Execute(object parameter) => this._Execute(parameter);

        public void RaiseCanExecuteChanged() => this.CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// A command whose handler is asynchronous. It reports itself as unavailable while it is
    /// running, so a double click cannot start a second copy of the same work.
    /// </summary>
    public sealed class AsyncRelayCommand : ICommand
    {
        private readonly Func<object, Task> _Execute;
        private readonly Func<object, bool> _CanExecute;

        private bool _IsRunning;

        public AsyncRelayCommand(Func<Task> execute, Func<bool> canExecute = null)
            : this(
                _ => execute(),
                canExecute == null ? null : new Func<object, bool>(_ => canExecute()))
        {
        }

        public AsyncRelayCommand(Func<object, Task> execute, Func<object, bool> canExecute = null)
        {
            this._Execute = execute ?? throw new ArgumentNullException(nameof(execute));
            this._CanExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged;

        public bool IsRunning => this._IsRunning;

        public bool CanExecute(object parameter)
        {
            return this._IsRunning == false && (this._CanExecute == null || this._CanExecute(parameter));
        }

        public void Execute(object parameter)
        {
            this.ExecuteAsync(parameter).Forget();
        }

        public async Task ExecuteAsync(object parameter)
        {
            if (this.CanExecute(parameter) == false)
            {
                return;
            }

            this._IsRunning = true;
            this.RaiseCanExecuteChanged();
            try
            {
                await this._Execute(parameter).ConfigureAwait(true);
            }
            finally
            {
                this._IsRunning = false;
                this.RaiseCanExecuteChanged();
            }
        }

        public void RaiseCanExecuteChanged() => this.CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}

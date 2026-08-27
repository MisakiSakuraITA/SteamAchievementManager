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
using System.Threading;
using System.Threading.Tasks;

namespace SAM.Core.Threading
{
    public static class TaskExtensions
    {
        /// <summary>
        /// Deliberately abandons a task that reports its own failures, marking the intent at
        /// the call site and observing any exception so it cannot escape later as an
        /// unobserved fault.
        /// </summary>
        public static void Forget(this Task task)
        {
            if (task == null)
            {
                throw new ArgumentNullException(nameof(task));
            }

            if (task.IsCompleted == true)
            {
                Observe(task);
                return;
            }

            task.ContinueWith(
                static completed => Observe(completed),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private static void Observe(Task task)
        {
            AggregateException ignored = task.Exception;
            GC.KeepAlive(ignored);
        }
    }
}

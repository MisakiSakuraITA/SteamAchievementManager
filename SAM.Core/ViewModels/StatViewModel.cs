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
using System.Globalization;
using SAM.Core.Steam.Schema;

namespace SAM.Core.ViewModels
{
    /// <summary>
    /// One editable game statistic. The value is edited as text so a half-typed entry does
    /// not have to be a valid number, with validation reported alongside it.
    /// </summary>
    public abstract class StatViewModel : ObservableObject
    {
        private string _ValueText;
        private string _ValidationError;
        private bool _IsModified;

        protected StatViewModel(StatDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            this.Id = definition.Id;
            this.DisplayName = string.IsNullOrEmpty(definition.DisplayName) == true
                ? definition.Id
                : definition.DisplayName;
            this.Permission = definition.Permission;
        }

        public string Id { get; }

        public string DisplayName { get; }

        public int Permission { get; }

        /// <summary>Steam refuses writes to these; the editor shows them read-only.</summary>
        public bool IsProtected => (this.Permission & 2) != 0;

        public abstract bool IsIncrementOnly { get; }

        public abstract string TypeName { get; }

        /// <summary>A short note on why a stat cannot be edited freely, or an empty string.</summary>
        public string Extra
        {
            get
            {
                if (this.IsProtected == true)
                {
                    return "protected";
                }
                return this.IsIncrementOnly == true ? "increment only" : "";
            }
        }

        public string ValueText
        {
            get => this._ValueText;
            set
            {
                if (this.Set(ref this._ValueText, value) == false)
                {
                    return;
                }

                this.Validate();
            }
        }

        public string ValidationError
        {
            get => this._ValidationError;
            private set
            {
                if (this.Set(ref this._ValidationError, value) == true)
                {
                    this.Raise(nameof(this.HasError));
                }
            }
        }

        public bool HasError => string.IsNullOrEmpty(this._ValidationError) == false;

        public bool IsModified
        {
            get => this._IsModified;
            protected set => this.Set(ref this._IsModified, value);
        }

        internal event Action<StatViewModel> Changed;

        public bool Matches(string search)
        {
            if (string.IsNullOrEmpty(search) == true)
            {
                return true;
            }

            return (this.DisplayName != null && this.DisplayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (this.Id != null && this.Id.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>Writes the pending value through a service. Returns false on refusal.</summary>
        internal abstract bool Store(Steam.ISteamStatsService steam);

        /// <summary>Accepts the pending value as the stored truth after a successful store.</summary>
        internal abstract void AcceptPending();

        protected void SetInitialText(string text)
        {
            this._ValueText = text;
            this._ValidationError = null;
            this._IsModified = false;
            this.Raise(nameof(this.ValueText), nameof(this.ValidationError), nameof(this.HasError), nameof(this.IsModified));
        }

        protected abstract void Validate();

        protected void ReportValidation(string error, bool isModified)
        {
            this.ValidationError = error;
            this.IsModified = isModified;
            this.Changed?.Invoke(this);
        }

        public override string ToString() => $"{this.DisplayName} = {this._ValueText}";
    }

    public sealed class IntegerStatViewModel : StatViewModel
    {
        private readonly IntegerStatDefinition _Definition;

        private int _OriginalValue;
        private int _PendingValue;

        public IntegerStatViewModel(IntegerStatDefinition definition, int value)
            : base(definition)
        {
            this._Definition = definition;
            this._OriginalValue = value;
            this._PendingValue = value;
            this.SetInitialText(value.ToString(CultureInfo.CurrentCulture));
        }

        public override bool IsIncrementOnly => this._Definition.IncrementOnly;

        public override string TypeName => "Integer";

        public int Value => this._PendingValue;

        protected override void Validate()
        {
            if (this.IsProtected == true)
            {
                this.ReportValidation("This stat is protected and cannot be modified.", false);
                return;
            }

            if (int.TryParse(this.ValueText, NumberStyles.Integer, CultureInfo.CurrentCulture, out var parsed) == false)
            {
                this.ReportValidation("Not a whole number.", this.IsModified);
                return;
            }

            if (parsed < this._Definition.MinValue || parsed > this._Definition.MaxValue)
            {
                this.ReportValidation(
                    $"Must be between {this._Definition.MinValue} and {this._Definition.MaxValue}.",
                    this.IsModified);
                return;
            }

            if (this._Definition.IncrementOnly == true && parsed < this._OriginalValue)
            {
                this.ReportValidation("This stat can only increase.", this.IsModified);
                return;
            }

            this._PendingValue = parsed;
            this.ReportValidation(null, parsed != this._OriginalValue);
        }

        internal override bool Store(Steam.ISteamStatsService steam)
        {
            return steam.SetIntegerStat(this.Id, this._PendingValue);
        }

        internal override void AcceptPending()
        {
            this._OriginalValue = this._PendingValue;
            this.IsModified = false;
        }
    }

    public sealed class FloatStatViewModel : StatViewModel
    {
        private readonly FloatStatDefinition _Definition;

        private float _OriginalValue;
        private float _PendingValue;

        public FloatStatViewModel(FloatStatDefinition definition, float value)
            : base(definition)
        {
            this._Definition = definition;
            this._OriginalValue = value;
            this._PendingValue = value;
            this.SetInitialText(value.ToString(CultureInfo.CurrentCulture));
        }

        public override bool IsIncrementOnly => this._Definition.IncrementOnly;

        public override string TypeName => "Float";

        public float Value => this._PendingValue;

        protected override void Validate()
        {
            if (this.IsProtected == true)
            {
                this.ReportValidation("This stat is protected and cannot be modified.", false);
                return;
            }

            if (float.TryParse(this.ValueText, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed) == false)
            {
                this.ReportValidation("Not a number.", this.IsModified);
                return;
            }

            if (parsed < this._Definition.MinValue || parsed > this._Definition.MaxValue)
            {
                this.ReportValidation(
                    $"Must be between {this._Definition.MinValue} and {this._Definition.MaxValue}.",
                    this.IsModified);
                return;
            }

            if (this._Definition.IncrementOnly == true && parsed < this._OriginalValue)
            {
                this.ReportValidation("This stat can only increase.", this.IsModified);
                return;
            }

            this._PendingValue = parsed;

            // Comparing floats exactly is right here: anything the user typed that round-trips
            // to the same value is not a modification.
            this.ReportValidation(null, parsed.Equals(this._OriginalValue) == false);
        }

        internal override bool Store(Steam.ISteamStatsService steam)
        {
            return steam.SetFloatStat(this.Id, this._PendingValue);
        }

        internal override void AcceptPending()
        {
            this._OriginalValue = this._PendingValue;
            this.IsModified = false;
        }
    }
}

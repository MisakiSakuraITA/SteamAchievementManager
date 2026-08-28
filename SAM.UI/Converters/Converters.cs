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
using System.Windows;
using System.Windows.Data;

namespace SAM.UI.Converters
{
    /// <summary>Visible when true; collapsed otherwise.</summary>
    public sealed class BooleanToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var flag = value is bool b && b;
            if (this.Invert == true)
            {
                flag = !flag;
            }
            return flag ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var visible = value is Visibility v && v == Visibility.Visible;
            return this.Invert == true ? !visible : visible;
        }
    }

    /// <summary>Visible when the value is neither null nor an empty string.</summary>
    public sealed class HasValueToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var has = value != null && (value is string s == false || string.IsNullOrEmpty(s) == false);
            if (this.Invert == true)
            {
                has = !has;
            }
            return has ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>Inverts a boolean, for binding an "is enabled" to an "is busy".</summary>
    public sealed class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b == false || !b;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b == false || !b;
    }

    /// <summary>
    /// True when the bound enum equals the value named in the converter parameter. Two-way, so
    /// a set of toggle buttons can drive a single enum property.
    /// </summary>
    public sealed class EnumToBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter is string name == false)
            {
                return false;
            }

            return string.Equals(value.ToString(), name, StringComparison.Ordinal);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Only a checked button selects a value; unchecking is the other button's job.
            if (value is bool b == false || b == false || parameter is string name == false)
            {
                return Binding.DoNothing;
            }

            var enumType = Nullable.GetUnderlyingType(targetType) ?? targetType;
            return enumType.IsEnum == true ? Enum.Parse(enumType, name) : Binding.DoNothing;
        }
    }
}

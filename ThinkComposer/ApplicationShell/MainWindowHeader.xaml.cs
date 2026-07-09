// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Copyright (C) 2011-2015 Néstor Marcel Sánchez Ahumada.
// https://github.com/nmarcel/ThinkComposer
//
// This file is part of ThinkComposer, which is free software licensed under the GNU General Public License.
// It is provided without any warranty. You should find a copy of the license in the root directory of this software product.
// -------------------------------------------------------------------------------------------
//
// Project: Instrumind ThinkComposer v1.0
// File   : MainWindowHeader.cs
// Object : Instrumind.ThinkComposer.ApplicationShell.MainWindowHeader (Interface)
//
// Date       Author             Changes
// ---------- ------------------ -------------------------------------------------------------
// 2009.06.20 Néstor Sánchez A.  Creation
//
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using Instrumind.Common;
using Instrumind.Common.Visualization;

using Instrumind.ThinkComposer.ApplicationProduct;

/// Provides the user-interface top level frame of the application.
namespace Instrumind.ThinkComposer.ApplicationShell
{
    /// <summary>
    /// The header section of the main window of the application.
    /// Shows the working title, subtitle and image, plus controls for the window handling.
    /// </summary>
    public partial class MainWindowHeader : UserControl
    {
        public event Action<MouseButtonEventArgs> Dragging;
        public event Action Minimizing;
        public event Action RestoringOrMaximizing;
        public event Action Closing;
        public event Action<bool> ThemeToggled;

        private bool IsUpdatingThemeToggle = false;

        static MainWindowHeader()
        {
        }

        public MainWindowHeader()
        {
            InitializeComponent();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            // Make the Menu Toolbar start and remain open.
            this.PostCall(
                winhdr =>
                {
                    this.PaletteSupraContainer.Show();
                    this.PaletteSupraContainer.CanCollapse = false;
                });
        }

        private void MainWindowHeader_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (IsInsideInteractiveControl(e.OriginalSource as DependencyObject))
                return;

            var Handler = Dragging;

            if (Handler != null)
                Handler(e);
        }

        public void SetThemeToggleState(bool IsDarkTheme)
        {
            this.IsUpdatingThemeToggle = true;
            this.ThemeToggleButton.IsChecked = IsDarkTheme;
            this.ThemeToggleButton.ToolTip = ApplicationThemeManager.GetToggleToolTip();
            this.IsUpdatingThemeToggle = false;
        }

        private void ThemeToggleButton_Checked(object sender, RoutedEventArgs e)
        {
            RaiseThemeToggled(true);
        }

        private void ThemeToggleButton_Unchecked(object sender, RoutedEventArgs e)
        {
            RaiseThemeToggled(false);
        }

        private void RaiseThemeToggled(bool UseDarkTheme)
        {
            if (this.IsUpdatingThemeToggle)
                return;

            var Handler = ThemeToggled;
            if (Handler != null)
                Handler(UseDarkTheme);
        }

        private bool IsInsideInteractiveControl(DependencyObject Source)
        {
            while (Source != null && Source != this)
            {
                if (Source is ButtonBase || Source is TextBox || Source is Selector)
                    return true;

                Source = GetObjectParent(Source);
            }

            return false;
        }

        private DependencyObject GetObjectParent(DependencyObject Source)
        {
            try
            {
                return VisualTreeHelper.GetParent(Source);
            }
            catch
            {
                return LogicalTreeHelper.GetParent(Source);
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            if (e != null)
                e.Handled = true;

            var Handler = Minimizing;

            if (Handler != null)
                Handler();
        }

        private void BtnRestoreOrMaximize_Click(object sender, RoutedEventArgs e)
        {
            if (e != null)
                e.Handled = true;

            RestoringOrMaximizing();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            if (e != null)
                e.Handled = true;

            var Handler = Closing;

            if (Handler != null)
                Handler();
        }

        private void CompanyLogo_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ProductDirector.ShowAbout();
        }
    }
}

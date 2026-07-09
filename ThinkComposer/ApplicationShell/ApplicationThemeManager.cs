// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Copyright (C) 2011-2015 Nestor Marcel Sanchez Ahumada.
// https://github.com/nmarcel/ThinkComposer
//
// This file is part of ThinkComposer, which is free software licensed under the GNU General Public License.
// It is provided without any warranty. You should find a copy of the license in the root directory of this software product.
// -------------------------------------------------------------------------------------------

using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

using Instrumind.Common;
using Instrumind.Common.Visualization.Widgets;

namespace Instrumind.ThinkComposer.ApplicationShell
{
    public enum ApplicationThemePreference
    {
        System,
        Light,
        Dark
    }

    /// <summary>
    /// Applies application chrome theme colors without changing diagram visual elements.
    /// </summary>
    public static class ApplicationThemeManager
    {
        private const string CONFIG_SCOPE = "UserInterface";
        private const string CONFIG_CODE_THEME_PREFERENCE = "ThemePreference";
        private const string WINDOWS_PERSONALIZE_KEY = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string WINDOWS_APPS_USE_LIGHT_THEME_VALUE = "AppsUseLightTheme";

        private static bool Initialized = false;

        public static event EventHandler ThemeChanged;

        public static ApplicationThemePreference Preference { get; private set; }

        public static bool IsDarkThemeApplied { get; private set; }

        public static void Initialize()
        {
            if (Initialized)
                return;

            Initialized = true;
            LoadPreferenceFromConfiguration();
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        }

        public static void Shutdown()
        {
            if (!Initialized)
                return;

            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            Initialized = false;
        }

        public static void LoadPreferenceFromConfiguration()
        {
            var PreferenceText = AppExec.GetConfiguration<string>(CONFIG_SCOPE, CONFIG_CODE_THEME_PREFERENCE,
                                                                  ApplicationThemePreference.System.ToString());
            ApplicationThemePreference DetectedPreference;
            if (!Enum.TryParse<ApplicationThemePreference>(PreferenceText, true, out DetectedPreference))
                DetectedPreference = ApplicationThemePreference.System;

            Preference = DetectedPreference;
            ApplyCurrentPreference();
        }

        public static void SetDarkTheme(bool UseDarkTheme, bool SavePreference)
        {
            SetPreference(UseDarkTheme ? ApplicationThemePreference.Dark : ApplicationThemePreference.Light, SavePreference);
        }

        public static void SetPreference(ApplicationThemePreference NewPreference, bool SavePreference)
        {
            Preference = NewPreference;

            if (SavePreference)
                AppExec.SetConfiguration<string>(CONFIG_SCOPE, CONFIG_CODE_THEME_PREFERENCE, Preference.ToString(), true);

            ApplyCurrentPreference();
        }

        public static string GetToggleToolTip()
        {
            return IsDarkThemeApplied ? "Switch to light theme" : "Switch to dark theme";
        }

        private static void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (Preference != ApplicationThemePreference.System)
                return;

            if (e.Category != UserPreferenceCategory.Color
                && e.Category != UserPreferenceCategory.General
                && e.Category != UserPreferenceCategory.VisualStyle)
                return;

            var App = Application.Current;
            if (App == null || App.Dispatcher == null || App.Dispatcher.HasShutdownStarted || App.Dispatcher.HasShutdownFinished)
                return;

            App.Dispatcher.BeginInvoke(new Action(ApplyCurrentPreference));
        }

        private static void ApplyCurrentPreference()
        {
            var UseDarkTheme = (Preference == ApplicationThemePreference.Dark)
                               || (Preference == ApplicationThemePreference.System && IsWindowsDarkThemePreferred());

            try
            {
                if (UseDarkTheme)
                    ApplyDarkTheme();
                else
                    ApplyLightTheme();

                IsDarkThemeApplied = UseDarkTheme;
            }
            catch (Exception Problem)
            {
                Console.WriteLine("Cannot apply application theme: " + Problem.Message);
                try
                {
                    AppExec.LogException(Problem);
                }
                catch
                {
                    // Keep theme failures from blocking application startup.
                }
            }

            RaiseThemeChanged();
        }

        private static bool IsWindowsDarkThemePreferred()
        {
            try
            {
                using (var Key = Registry.CurrentUser.OpenSubKey(WINDOWS_PERSONALIZE_KEY))
                {
                    if (Key == null)
                        return false;

                    var AppsUseLightTheme = Key.GetValue(WINDOWS_APPS_USE_LIGHT_THEME_VALUE);
                    if (AppsUseLightTheme == null)
                        return false;

                    return Convert.ToInt32(AppsUseLightTheme) == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void ApplyLightTheme()
        {
            SetSystemBrushes(Rgb(255, 255, 255), Rgb(0, 0, 0), Rgb(240, 247, 250), Rgb(225, 235, 239), Rgb(30, 144, 255));

            SetSolid(BasicWindowKey("GeneralTopBrush"), Colors.Azure);
            SetSolid(BasicWindowKey("GeneralBodyBrush"), Colors.Azure);
            SetSolid(BasicWindowKey("GeneralBottomBrush"), Colors.Azure);
            SetGradient(BasicWindowKey("WindowFrameBrush"), Rgb(101, 124, 149), Colors.LightSteelBlue);
            SetSolid(BasicWindowKey("GeneralMarkedTextColor"), Colors.White);
            SetSolid(BasicWindowKey("GeneralNormalTextColor"), Colors.WhiteSmoke);

            SetGradient(DialogOptionsWindowKey("DialogFrameBrush"), Colors.LightBlue, Colors.LightCyan);
            SetGradient(DialogOptionsWindowKey("DialogTitleBrush"), Colors.SlateGray, Colors.SteelBlue);
            SetSolid(DialogOptionsWindowKey("DialogBorderBrush"), Colors.SteelBlue);

            SetGradient(EntitledPanelKey("HeaderBrush"), Rgb(191, 210, 219), Rgb(174, 200, 216));
            SetSolid(EntitledPanelKey("HeaderBottomBrush"), Rgb(120, 179, 203));
            SetSolid(EntitledPanelKey("HeaderTextBrush"), Colors.White);
            SetGradient(EntitledPanelKey("PanelBrush"), Rgb(240, 247, 250), Rgb(225, 235, 239));
            SetSolid(EntitledPanelKey("PanelTextBrush"), Rgb(93, 150, 173));
            SetSolid(EntitledPanelKey("FormBodyBrush"), Colors.Transparent);
            SetSolid(EntitledPanelKey("ExpositorBrush"), Rgb(223, 232, 235));
            SetSolid(EntitledPanelKey("ExpositorTextBrush"), Colors.Black);
            SetGradient(EntitledPanelKey("ItemBrush"), Rgb(217, 226, 229), Rgb(233, 240, 242));
            SetSolid(EntitledPanelKey("ItemTextBrush"), Colors.Black);
            SetSolid(EntitledPanelKey("ItemBorderBrush"), Colors.LightBlue);
            SetSolid(EntitledPanelKey("ItemSelectionBrush"), Colors.DodgerBlue);

            SetScrollBrushes(false);
            SetToggleBrushes(Rgb(244, 249, 251), Rgb(190, 207, 218), Rgb(68, 98, 112), Rgb(255, 255, 255));
        }

        private static void ApplyDarkTheme()
        {
            SetSystemBrushes(Rgb(30, 34, 39), Rgb(232, 238, 242), Rgb(41, 47, 53), Rgb(50, 58, 65), Rgb(45, 152, 170));

            SetSolid(BasicWindowKey("GeneralTopBrush"), Rgb(30, 34, 39));
            SetSolid(BasicWindowKey("GeneralBodyBrush"), Rgb(37, 42, 48));
            SetSolid(BasicWindowKey("GeneralBottomBrush"), Rgb(32, 36, 42));
            SetGradient(BasicWindowKey("WindowFrameBrush"), Rgb(38, 52, 57), Rgb(67, 89, 98));
            SetSolid(BasicWindowKey("GeneralMarkedTextColor"), Colors.White);
            SetSolid(BasicWindowKey("GeneralNormalTextColor"), Rgb(226, 235, 240));

            SetGradient(DialogOptionsWindowKey("DialogFrameBrush"), Rgb(42, 48, 55), Rgb(30, 35, 41));
            SetGradient(DialogOptionsWindowKey("DialogTitleBrush"), Rgb(59, 74, 84), Rgb(23, 107, 117));
            SetSolid(DialogOptionsWindowKey("DialogBorderBrush"), Rgb(94, 125, 132));

            SetGradient(EntitledPanelKey("HeaderBrush"), Rgb(53, 66, 74), Rgb(43, 54, 61));
            SetSolid(EntitledPanelKey("HeaderBottomBrush"), Rgb(79, 122, 132));
            SetSolid(EntitledPanelKey("HeaderTextBrush"), Rgb(245, 249, 250));
            SetGradient(EntitledPanelKey("PanelBrush"), Rgb(43, 48, 54), Rgb(35, 40, 46));
            SetSolid(EntitledPanelKey("PanelTextBrush"), Rgb(185, 211, 219));
            SetSolid(EntitledPanelKey("FormBodyBrush"), Colors.Transparent);
            SetSolid(EntitledPanelKey("ExpositorBrush"), Rgb(31, 36, 41));
            SetSolid(EntitledPanelKey("ExpositorTextBrush"), Rgb(232, 238, 242));
            SetGradient(EntitledPanelKey("ItemBrush"), Rgb(51, 58, 65), Rgb(41, 47, 53));
            SetSolid(EntitledPanelKey("ItemTextBrush"), Rgb(232, 238, 242));
            SetSolid(EntitledPanelKey("ItemBorderBrush"), Rgb(81, 108, 118));
            SetSolid(EntitledPanelKey("ItemSelectionBrush"), Rgb(45, 152, 170));

            SetScrollBrushes(true);
            SetToggleBrushes(Rgb(35, 40, 46), Rgb(83, 103, 111), Rgb(228, 238, 242), Rgb(47, 55, 62));
        }

        private static void SetSystemBrushes(Color WindowColor, Color TextColor, Color ControlColor, Color ControlLightColor, Color HighlightColor)
        {
            SetApplicationBrush(SystemColors.WindowBrushKey, WindowColor);
            SetApplicationBrush(SystemColors.WindowTextBrushKey, TextColor);
            SetApplicationBrush(SystemColors.ControlBrushKey, ControlColor);
            SetApplicationBrush(SystemColors.ControlTextBrushKey, TextColor);
            SetApplicationBrush(SystemColors.ControlLightBrushKey, ControlLightColor);
            SetApplicationBrush(SystemColors.ControlDarkBrushKey, ControlLightColor);
            SetApplicationBrush(SystemColors.HighlightBrushKey, HighlightColor);
            SetApplicationBrush(SystemColors.HighlightTextBrushKey, Colors.White);
            SetApplicationBrush(SystemColors.GrayTextBrushKey, Rgb(150, 160, 166));
            SetApplicationBrush("ThinkComposer.WindowTextBrush", TextColor);
        }

        private static void SetScrollBrushes(bool IsDark)
        {
            if (IsDark)
            {
                SetSolid("StandardBorderBrush", Rgb(80, 92, 100));
                SetSolid("StandardBackgroundBrush", Rgb(35, 40, 46));
                SetSolid("HoverBorderBrush", Rgb(100, 120, 128));
                SetSolid("SelectedBackgroundBrush", Rgb(45, 152, 170));
                SetSolid("SelectedForegroundBrush", Colors.White);
                SetSolid("DisabledForegroundBrush", Rgb(112, 122, 128));
                SetSolid("NormalBrush", Rgb(82, 94, 102));
                SetSolid("NormalBorderBrush", Rgb(90, 104, 112));
                SetSolid("HorizontalNormalBrush", Rgb(82, 94, 102));
                SetSolid("HorizontalNormalBorderBrush", Rgb(90, 104, 112));
                SetGradient("ListBoxBackgroundBrush", Rgb(35, 40, 46), Rgb(35, 40, 46), Rgb(30, 35, 41));
                SetGradient("StandardBrush", Rgb(53, 61, 68), Rgb(38, 44, 50));
                SetSolid("GlyphBrush", Rgb(228, 238, 242));
                SetGradient("PressedBrush", Rgb(58, 68, 76), Rgb(45, 152, 170), Rgb(39, 78, 86), Rgb(34, 40, 46));
            }
            else
            {
                SetSolid("StandardBorderBrush", Rgb(136, 136, 136));
                SetSolid("StandardBackgroundBrush", Colors.White);
                SetSolid("HoverBorderBrush", Rgb(221, 221, 221));
                SetSolid("SelectedBackgroundBrush", Colors.Gray);
                SetSolid("SelectedForegroundBrush", Colors.White);
                SetSolid("DisabledForegroundBrush", Rgb(136, 136, 136));
                SetSolid("NormalBrush", Rgb(136, 136, 136));
                SetSolid("NormalBorderBrush", Rgb(136, 136, 136));
                SetSolid("HorizontalNormalBrush", Rgb(136, 136, 136));
                SetSolid("HorizontalNormalBorderBrush", Rgb(136, 136, 136));
                SetGradient("ListBoxBackgroundBrush", Colors.White, Colors.White, Rgb(221, 221, 221));
                SetGradient("StandardBrush", Colors.White, Rgb(204, 204, 204));
                SetSolid("GlyphBrush", Rgb(68, 68, 68));
                SetGradient("PressedBrush", Rgb(187, 187, 187), Rgb(238, 238, 238), Rgb(238, 238, 238), Colors.White);
            }
        }

        private static void SetToggleBrushes(Color Background, Color Border, Color Glyph, Color Hover)
        {
            SetApplicationBrush("ThinkComposer.ThemeToggleBackgroundBrush", Background);
            SetApplicationBrush("ThinkComposer.ThemeToggleBorderBrush", Border);
            SetApplicationBrush("ThinkComposer.ThemeToggleGlyphBrush", Glyph);
            SetApplicationBrush("ThinkComposer.ThemeToggleHoverBrush", Hover);
        }

        private static ComponentResourceKey BasicWindowKey(string ResourceId)
        {
            return new ComponentResourceKey(typeof(BasicWindow), ResourceId);
        }

        private static ComponentResourceKey DialogOptionsWindowKey(string ResourceId)
        {
            return new ComponentResourceKey(typeof(DialogOptionsWindow), ResourceId);
        }

        private static ComponentResourceKey EntitledPanelKey(string ResourceId)
        {
            return new ComponentResourceKey(typeof(EntitledPanel), ResourceId);
        }

        private static void SetSolid(object Key, Color Color)
        {
            var App = Application.Current;
            if (App == null)
                return;

            var Brush = App.TryFindResource(Key) as SolidColorBrush;
            if (Brush == null || Brush.IsFrozen)
            {
                SetApplicationBrush(Key, Color);
                return;
            }

            Brush.Color = Color;
        }

        private static void SetGradient(object Key, params Color[] Colors)
        {
            var App = Application.Current;
            if (App == null)
                return;

            var PreviousBrush = App.TryFindResource(Key) as LinearGradientBrush;
            var Brush = new LinearGradientBrush();

            for (int Index = 0; Index < Colors.Length; Index++)
                Brush.GradientStops.Add(new GradientStop(Colors[Index], Colors.Length == 1 ? 0.0 : (double)Index / (double)(Colors.Length - 1)));

            if (PreviousBrush != null)
            {
                Brush.StartPoint = PreviousBrush.StartPoint;
                Brush.EndPoint = PreviousBrush.EndPoint;
                Brush.MappingMode = PreviousBrush.MappingMode;
                Brush.SpreadMethod = PreviousBrush.SpreadMethod;
                Brush.ColorInterpolationMode = PreviousBrush.ColorInterpolationMode;
            }

            App.Resources[Key] = Brush;
        }

        private static void SetApplicationBrush(object Key, Color Color)
        {
            var App = Application.Current;
            if (App == null)
                return;

            App.Resources[Key] = new SolidColorBrush(Color);
        }

        private static Color Rgb(byte Red, byte Green, byte Blue)
        {
            return Color.FromRgb(Red, Green, Blue);
        }

        private static void RaiseThemeChanged()
        {
            var Handler = ThemeChanged;
            if (Handler != null)
                Handler(null, EventArgs.Empty);
        }
    }
}

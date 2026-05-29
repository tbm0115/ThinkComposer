using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Media;

namespace Instrumind.Common.Visualization.Widgets
{
    /// <summary>
    /// Grayable image.
    /// </summary>
    public class ImprovedImage : Image
    {
        private bool IsUpdatingImageSource = false;

        static ImprovedImage()
        {
            IsEnabledProperty.OverrideMetadata(typeof(ImprovedImage),
                                               new FrameworkPropertyMetadata(true, new PropertyChangedCallback(OnAutoGrayScaleImageIsEnabledPropertyChanged)));
        }

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);

            if (e.Property == SourceProperty && !this.IsUpdatingImageSource && !this.IsEnabled)
                ApplyGrayScale(this);
        }

        private static void OnAutoGrayScaleImageIsEnabledPropertyChanged(DependencyObject source, DependencyPropertyChangedEventArgs args)
        {
            var autoGrayScaleImg = source as ImprovedImage;
            var isEnable = Convert.ToBoolean(args.NewValue);

            if (autoGrayScaleImg == null)
                return;

            if (!isEnable)
                ApplyGrayScale(autoGrayScaleImg);
            else
                RestoreColor(autoGrayScaleImg);
        }

        private static void ApplyGrayScale(ImprovedImage image)
        {
            if (image.Source == null)
                return;

            var converted = image.Source as FormatConvertedBitmap;
            if (converted != null)
                return;

            var bitmapSource = image.Source as BitmapSource;
            if (bitmapSource == null)
                return;

            image.IsUpdatingImageSource = true;
            try
            {
                image.Source = new FormatConvertedBitmap(bitmapSource, PixelFormats.Gray32Float, null, 0);
                image.OpacityMask = new ImageBrush(bitmapSource);
            }
            finally
            {
                image.IsUpdatingImageSource = false;
            }
        }

        private static void RestoreColor(ImprovedImage image)
        {
            var converted = image.Source as FormatConvertedBitmap;

            image.IsUpdatingImageSource = true;
            try
            {
                if (converted != null && converted.Source != null)
                    image.Source = converted.Source;

                image.OpacityMask = null;
            }
            finally
            {
                image.IsUpdatingImageSource = false;
            }
        }
    }
}

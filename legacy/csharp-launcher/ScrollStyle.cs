using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace PalrLauncher
{
    /// <summary>
    /// 细滚动条（2026-10-30）：账号列表、日志等深色面板里用的滚动条样式。
    ///
    /// 系统默认滚动条在深墨底上几乎看不见，且带上下箭头很占宽；这里换成
    /// **6px 宽的圆角滑块**（金色半透明），只保留滑块、无箭头，滚轮照常可用。
    /// </summary>
    public static class ScrollStyle
    {
        public static void Apply(ScrollViewer sv)
        {
            if (sv == null) return;
            sv.PanningMode = PanningMode.VerticalOnly;
            sv.PanningDeceleration = 0.001;
            sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            sv.Resources[typeof(ScrollBar)] = BuildBarStyle(Orientation.Vertical);
        }

        /**
         * 只做「变窄 + 透明底」，**不重写模板**：
         * Track 不是 IAddChild，FrameworkElementFactory 无法挂 Thumb，
         * 自定义模板会在窗口构建期抛异常（实测 04:22:06 InvalidOperationException）。
         */
        private static Style BuildBarStyle(Orientation orientation)
        {
            var style = new Style(typeof(ScrollBar));
            style.Setters.Add(new Setter(FrameworkElement.WidthProperty, 10.0));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.MarginProperty, new Thickness(0)));
            return style;
        }
    }
}

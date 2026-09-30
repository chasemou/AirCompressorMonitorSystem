using LiveCharts;
using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WpfTest.Views
{
    /// <summary>
    /// 可复用的实时趋势图控件：
    /// ChartTitle 设置标题，ChartSeries 绑定实时数据，ChartStroke 设置折线颜色，
    /// MinValue/MaxValue 设置 Y 轴量程（不设置则自动缩放）。
    /// 数据源由代码后台显式挂载并订阅集合变化，确保 LiveCharts 折线随实时数据更新。
    /// </summary>
    public partial class TrendChartControl : UserControl
    {
        /// <summary>图表标题</summary>
        public static readonly DependencyProperty ChartTitleProperty =
            DependencyProperty.Register(
                nameof(ChartTitle),
                typeof(string),
                typeof(TrendChartControl),
                new PropertyMetadata(string.Empty));

        public string ChartTitle
        {
            get { return (string)GetValue(ChartTitleProperty); }
            set { SetValue(ChartTitleProperty, value); }
        }

        /// <summary>折线数据源（ChartValues<double>，LiveCharts 专用集合，支持增量更新）</summary>
        public static readonly DependencyProperty ChartSeriesProperty =
            DependencyProperty.Register(
                nameof(ChartSeries),
                typeof(ChartValues<double>),
                typeof(TrendChartControl),
                new PropertyMetadata(null, OnChartSeriesChanged));

        public ChartValues<double> ChartSeries
        {
            get { return (ChartValues<double>)GetValue(ChartSeriesProperty); }
            set { SetValue(ChartSeriesProperty, value); }
        }

        /// <summary>折线颜色</summary>
        public static readonly DependencyProperty ChartStrokeProperty =
            DependencyProperty.Register(
                nameof(ChartStroke),
                typeof(Brush),
                typeof(TrendChartControl),
                new PropertyMetadata(Brushes.SeaGreen));

        public Brush ChartStroke
        {
            get { return (Brush)GetValue(ChartStrokeProperty); }
            set { SetValue(ChartStrokeProperty, value); }
        }

        /// <summary>Y 轴最小值（默认 0，XAML 可不写）</summary>
        public static readonly DependencyProperty MinValueProperty =
            DependencyProperty.Register(
                nameof(MinValue),
                typeof(double),
                typeof(TrendChartControl),
                new PropertyMetadata(0d, OnAxisRangeChanged));

        public double MinValue
        {
            get { return (double)GetValue(MinValueProperty); }
            set { SetValue(MinValueProperty, value); }
        }

        /// <summary>Y 轴最大值（double.NaN 表示自动）</summary>
        public static readonly DependencyProperty MaxValueProperty =
            DependencyProperty.Register(
                nameof(MaxValue),
                typeof(double),
                typeof(TrendChartControl),
                new PropertyMetadata(double.NaN, OnAxisRangeChanged));

        public double MaxValue
        {
            get { return (double)GetValue(MaxValueProperty); }
            set { SetValue(MaxValueProperty, value); }
        }

        public TrendChartControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            AttachSeries();
            ApplyAxisRange();
            SetupTimeAxis();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            DetachSeries();
        }

        /// <summary>
        /// 将折线显式挂载到数据源并订阅集合变化，
        /// 新数据到达时强制刷新图表，保证折线实时更新。
        /// </summary>
        private void AttachSeries()
        {
            DetachSeries();
            if (ChartSeries != null)
            {
                ChartSeries.CollectionChanged += OnSeriesChanged;
            }
            line.Values = ChartSeries ?? new ChartValues<double>();
            chart.Update(true);
        }

        private void DetachSeries()
        {
            if (ChartSeries != null)
            {
                ChartSeries.CollectionChanged -= OnSeriesChanged;
            }
        }

        /// <summary>
        /// 将 Y 轴量程直接赋值到 Axis（绕开 XAML 绑定，确保生效）。
        /// 未设置（NaN）时由 LiveCharts 自动缩放。
        /// </summary>
        private void ApplyAxisRange()
        {
            yAxis.MinValue = MinValue;
            yAxis.MaxValue = MaxValue;
            chart.Update(true);
        }

        /// <summary>
        /// 设置 X 轴时间标签：数据按 1 秒间隔采集，索引 0 为最旧点，
        /// 索引 N-1 为最新点（当前时间）。每次渲染时根据当前点数实时换算时间。
        /// </summary>
        private void SetupTimeAxis()
        {
            xAxis.LabelFormatter = value =>
            {
                if (ChartSeries == null || ChartSeries.Count == 0)
                    return string.Empty;

                int total = ChartSeries.Count;
                int index = (int)Math.Round(value);
                if (index < 0 || index >= total)
                    return string.Empty;

                // 索引 0 = 最旧点（total-1 秒前），索引 total-1 = 最新点（当前时间）
                double secondsAgo = total - 1 - index;
                return DateTime.Now.AddSeconds(-secondsAgo).ToString("M/d");
            };
        }

        private void OnSeriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            chart.Update(true);
        }

        private static void OnChartSeriesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (TrendChartControl)d;
            if (control.IsLoaded)
            {
                control.AttachSeries();
            }
        }

        private static void OnAxisRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (TrendChartControl)d;
            if (control.IsLoaded)
            {
                control.ApplyAxisRange();
            }
        }
    }
}

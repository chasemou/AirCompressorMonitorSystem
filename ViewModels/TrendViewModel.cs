using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveCharts;
using LiveCharts.Wpf;
using WpfTest.Services;

namespace WpfTest.ViewModels
{
    /// <summary>
    /// 趋势页 ViewModel：历史趋势查询 + 实时/历史切换
    /// 实时趋势数据源（PressureValue 等）保留在 MainViewModel 中（监控页和趋势页共用）
    /// </summary>
    public partial class TrendViewModel : ObservableObject
    {
        private readonly MainViewModel _main;
        private readonly DatabaseService _db;

        public TrendViewModel(MainViewModel main, DatabaseService db)
        {
            _main = main;
            _db = db;

            //默认查询最近一天
            StartDate = DateTime.Today.AddDays(-1);
            EndDate = DateTime.Today;
        }

        #region 历史趋势数据
        public ChartValues<double> HistoricalPressureValue { get; } = new ChartValues<double>();
        public ChartValues<double> HistoricalTemperatureValue { get; } = new ChartValues<double>();
        public ChartValues<double> HistoricalHumidityValue { get; } = new ChartValues<double>();
        public ChartValues<double> HistoricalFlowValue { get; } = new ChartValues<double>();
        #endregion

        #region 实时/历史切换
        [ObservableProperty]
        private bool isHistoricalMode;

        //Display*Value：根据模式切换实时或历史序列（供 TrendView 绑定）
        public IChartValues DisplayPressureValue => IsHistoricalMode ? HistoricalPressureValue : _main.PressureValue;
        public IChartValues DisplayTemperatureValue => IsHistoricalMode ? HistoricalTemperatureValue : _main.TemperatureValue;
        public IChartValues DisplayHumidityValue => IsHistoricalMode ? HistoricalHumidityValue : _main.HumidityValue;
        public IChartValues DisplayFlowValue => IsHistoricalMode ? HistoricalFlowValue : _main.FlowValue;

        partial void OnIsHistoricalModeChanged(bool value)
        {
            //切换模式时通知所有 Display 属性刷新
            OnPropertyChanged(nameof(DisplayPressureValue));
            OnPropertyChanged(nameof(DisplayTemperatureValue));
            OnPropertyChanged(nameof(DisplayHumidityValue));
            OnPropertyChanged(nameof(DisplayFlowValue));
        }
        #endregion

        #region 历史查询参数
        [ObservableProperty]
        private DateTime startDate;

        [ObservableProperty]
        private DateTime endDate;

        [ObservableProperty]
        private string historicalStatus = "实时数据";
        #endregion

        #region 报警声音开关
        [ObservableProperty]
        private bool alarmSoundEnabled = true;
        #endregion

        #region 命令
        [RelayCommand]
        private async Task QueryHistoricalTrendAsync()
        {
            try
            {
                HistoricalStatus = "查询中...";
                var records = await _db.LoadTrendDataAsync(StartDate, EndDate);

                HistoricalPressureValue.Clear();
                HistoricalTemperatureValue.Clear();
                HistoricalHumidityValue.Clear();
                HistoricalFlowValue.Clear();

                foreach (var r in records)
                {
                    HistoricalPressureValue.Add(r.Pressure);
                    HistoricalTemperatureValue.Add(r.Temperature);
                    HistoricalHumidityValue.Add(r.Humidity);
                    HistoricalFlowValue.Add(r.Flow);
                }

                IsHistoricalMode = true;
                HistoricalStatus = $"历史数据（{records.Count} 条）";
            }
            catch (Exception ex)
            {
                HistoricalStatus = $"查询失败：{ex.Message}";
            }
        }

        [RelayCommand]
        private void BackToRealtime()
        {
            IsHistoricalMode = false;
            HistoricalStatus = "实时数据";
        }
        #endregion
    }
}

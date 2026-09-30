using System;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WpfTest.ViewModels
{
    /// <summary>
    /// 监控页 ViewModel：实时数据卡片 + 设备统计
    /// 由 MainViewModel 轮询时更新其属性
    /// </summary>
    public partial class MonitorViewModel : ObservableObject
    {
        private readonly MainViewModel _main;

        public MonitorViewModel(MainViewModel main)
        {
            _main = main;
        }

        #region 实时数值卡片（由 MainViewModel.PollLoop 更新）
        [ObservableProperty]
        private double currentPressure;

        [ObservableProperty]
        private double currentTemperature;

        [ObservableProperty]
        private double currentHumidity;

        [ObservableProperty]
        private double currentFlow;
        #endregion

        #region 设备统计
        [ObservableProperty]
        private int onlineCount;

        [ObservableProperty]
        private int stoppedCount;

        [ObservableProperty]
        private int operatingRate;

        [ObservableProperty]
        private string runningDuration = "00:00:00";

        private readonly DateTime _startTime = DateTime.Now;
        #endregion

        /// <summary>
        /// 更新设备统计（由 MainViewModel 的 UI 定时器每秒调用）
        /// </summary>
        public void UpdateDeviceStats()
        {
            //运行时长
            var span = DateTime.Now - _startTime;
            RunningDuration = span.ToString(@"hh\:mm\:ss");

            //在线/停机：以 Modbus 连接状态模拟单设备在线
            OnlineCount = _main.IsConnected ? 1 : 0;
            StoppedCount = Math.Max(0, _main.SettingVM.TotalDeviceCount - OnlineCount);

            //运转率 = 在线 / 总设备 * 100
            OperatingRate = _main.SettingVM.TotalDeviceCount > 0
                ? (int)Math.Round((double)OnlineCount / _main.SettingVM.TotalDeviceCount * 100)
                : 0;

            Debug.WriteLine($"统计更新：在线={OnlineCount} 停机={StoppedCount} 运转率={OperatingRate}%");
        }
    }
}

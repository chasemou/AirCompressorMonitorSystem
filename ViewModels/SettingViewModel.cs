using System;
using System.Collections.Generic;
using System.IO.Ports;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WpfTest.ViewModels
{
    /// <summary>
    /// 设置页 ViewModel：设备设置 / 通信设置 / 报警阈值 / 数据采集
    /// MainViewModel 轮询时从这里读取配置参数
    /// </summary>
    public partial class SettingViewModel : ObservableObject
    {
        private readonly MainViewModel _main;

        public SettingViewModel(MainViewModel main)
        {
            _main = main;
            RefreshPorts();
        }

        #region 设备设置
        [ObservableProperty]
        private int totalDeviceCount = 6;

        [ObservableProperty]
        private int deviceId = 1;
        #endregion

        #region 通信设置
        [ObservableProperty]
        private string portName = "COM3";

        [ObservableProperty]
        private int baudRate = 9600;

        [ObservableProperty]
        private byte slaveAddress = 1;

        public List<string> AvailablePorts { get; private set; } = new();
        public List<int> BaudRates { get; } = new() { 9600, 19200, 38400, 57600, 115200 };
        #endregion

        #region 报警阈值
        [ObservableProperty]
        private double pressureHighLimit = 1.6;

        [ObservableProperty]
        private double temperatureHighLimit = 120;

        [ObservableProperty]
        private double humidityHighLimit = 90;

        [ObservableProperty]
        private double flowHighLimit = 20;
        #endregion

        #region 数据采集设置
        [ObservableProperty]
        private int pollIntervalSeconds = 1;

        [ObservableProperty]
        private int maxTrendPoints = 300;

        [ObservableProperty]
        private double pressureScale = 0.01;

        [ObservableProperty]
        private double temperatureScale = 1.0;

        [ObservableProperty]
        private double humidityScale = 0.1;

        [ObservableProperty]
        private double flowScale = 0.1;
        #endregion

        #region 命令
        /// <summary>
        /// 刷新可用串口列表
        /// </summary>
        [RelayCommand]
        private void RefreshPorts()
        {
            AvailablePorts = new List<string>(SerialPort.GetPortNames());
            OnPropertyChanged(nameof(AvailablePorts));
        }

        /// <summary>
        /// 应用通信设置并重新连接
        /// </summary>
        [RelayCommand]
        private void ApplyCommSettings()
        {
            _main.ApplyCommSettings(PortName, BaudRate, SlaveAddress);
        }
        #endregion
    }
}

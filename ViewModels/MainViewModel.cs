using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveCharts;
using LiveCharts.Wpf;
using WpfTest.Models;
using WpfTest.Services;

namespace WpfTest.ViewModels
{
    /// <summary>
    /// 主视图模型（协调者）：
    /// - 持有 ModbusService / DatabaseService
    /// - 后台线程轮询 Modbus，Dispatcher.InvokeAsync 回 UI 线程更新
    /// - 持有各子页面 ViewModel，轮询数据推送到子 VM
    /// - 保留跨页面共享数据：NowTime / IsConnected / AlarmList / AlarmCount / 实时趋势序列
    /// </summary>
    public partial class MainViewModel : ObservableObject
    {
        #region 子页面 ViewModel
        public MonitorViewModel MonitorVM { get; }
        public TrendViewModel TrendVM { get; }
        public AlarmViewModel AlarmVM { get; }
        public SettingViewModel SettingVM { get; }
        #endregion

        #region 常量（Modbus 寄存器映射）
        private const int RegPressure = 0;
        private const int RegTemperature = 1;
        private const int RegHumidity = 2;
        private const int RegFlow = 3;
        private const int StartAddress = 0;
        private const int RegisterCount = 4;
        private const int TrendSaveInterval = 30;  //每 30 次采集存一次趋势数据
        private const int MaxAlarmRecords = 500;
        private const int MaxConsecutiveFailures = 3;  //连续读取失败次数阈值，超过判定离线
        #endregion

        #region 字段
        private ModbusService modbusService;
        private readonly DatabaseService dbService;
        private CancellationTokenSource _cts;
        private Task _pollTask;
        private readonly DispatcherTimer _uiTimer;   //UI 线程定时器：每秒更新时间和设备统计
        private bool _isReading;
        private bool _connectFailedLogged;
        private bool _dbReady;
        private int _alarmIndex;
        private int _pollCounter;
        private int _consecutiveReadFailures;   //连续读取失败计数器（心跳检测用）
        private DateTime _lastPollTime = DateTime.MinValue;
        private readonly Dictionary<string, AlarmModels> _activeAlarms = new();
        #endregion

        #region 全局共享状态
        [ObservableProperty]
        private DateTime nowTime = DateTime.Now;

        [ObservableProperty]
        private bool isConnected;

        [ObservableProperty]
        private object pageContent;
        #endregion

        #region 报警（监控页 + 报警页共用）
        public ObservableCollection<AlarmModels> AlarmList { get; } = new();

        [ObservableProperty]
        private int alarmCount;
        #endregion

        #region 实时趋势序列（监控页 + 趋势页共用）
        public ChartValues<double> PressureValue { get; } = new ChartValues<double>();
        public ChartValues<double> TemperatureValue { get; } = new ChartValues<double>();
        public ChartValues<double> HumidityValue { get; } = new ChartValues<double>();
        public ChartValues<double> FlowValue { get; } = new ChartValues<double>();
        #endregion

        #region 日志服务
        public LogService Logger { get; } = new LogService();
        #endregion

        #region 构造函数
        public MainViewModel()
        {
            //数据库服务
            dbService = new DatabaseService("monitor.db");

            //先创建子 VM（它们持有 this 引用）
            SettingVM = new SettingViewModel(this);
            MonitorVM = new MonitorViewModel(this);
            TrendVM = new TrendViewModel(this, dbService);
            AlarmVM = new AlarmViewModel(this);

            //Modbus 服务
            modbusService = new ModbusService(SettingVM.PortName, SettingVM.BaudRate);

            //UI 线程定时器：每秒更新时间 + 设备统计
            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _uiTimer.Tick += (s, e) =>
            {
                NowTime = DateTime.Now;
                MonitorVM.UpdateDeviceStats();
            };
            _uiTimer.Start();

            //后台轮询线程
            _cts = new CancellationTokenSource();
            _pollTask = Task.Run(() => PollLoop(_cts.Token));

            //初始化数据库（fire-and-forget）
            _ = InitializeDatabaseAsync();

            Logger.Info("系统启动");
            TryConnect();
        }
        #endregion

        #region 导航命令
        [RelayCommand]
        private void Nav(object parameter)
        {
            PageContent = parameter;
        }

        [RelayCommand]
        private void Close(object parameter)
        {
            if (parameter is Window window)
                window.Close();
        }
        #endregion

        #region Modbus 连接
        private bool TryConnect()
        {
            try
            {
                //1. 打开串口 + 创建主站对象
                modbusService.Connect();

                //2. 真正验证从站是否在线：发一个读取请求，看从站有没有响应
                bool online = modbusService.TestConnection(SettingVM.SlaveAddress);

                IsConnected = online;
                if (online)
                {
                    _connectFailedLogged = false;
                    _consecutiveReadFailures = 0;   //重连成功，清零心跳计数器
                    Logger.Info($"Modbus 连接成功（{SettingVM.PortName} @ {SettingVM.BaudRate}，从站 {SettingVM.SlaveAddress}）");
                }
                else
                {
                    if (!_connectFailedLogged)
                    {
                        Logger.Warn("串口已打开但从站无响应，请检查 Modbus Slave 是否启动");
                        _connectFailedLogged = true;
                    }
                }
                return online;
            }
            catch (Exception ex)
            {
                IsConnected = false;
                if (!_connectFailedLogged)
                {
                    Logger.Warn($"Modbus 连接失败：{ex.Message}");
                    _connectFailedLogged = true;
                }
                return false;
            }
        }

        /// <summary>
        /// 由 SettingViewModel 调用：应用新通信参数并重新连接
        /// </summary>
        public void ApplyCommSettings(string portName, int baudRate, byte slaveAddress)
        {
            try
            {
                modbusService.Dispose();
            }
            catch { }

            modbusService = new ModbusService(portName, baudRate);
            Logger.Info($"应用通信设置：{portName} @ {baudRate}，从站 {slaveAddress}");
            TryConnect();
        }
        #endregion

        #region 后台轮询
        /// <summary>
        /// 后台轮询循环（Task.Run 线程）：
        /// 只负责 Modbus 采集、报警判定、写库。
        /// 时间更新和设备统计由 UI 线程 _uiTimer 负责。
        /// </summary>
        private async Task PollLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if ((DateTime.Now - _lastPollTime).TotalSeconds >= Math.Max(1, SettingVM.PollIntervalSeconds)
                        && !_isReading)
                    {
                        _lastPollTime = DateTime.Now;
                        _isReading = true;
                        try
                        {
                            //未连接时尝试重连（IsConnected 反映从站是否真正在线）
                            if (!IsConnected)
                            {
                                bool ok = TryConnect();
                                if (!ok) continue;
                            }

                            //读取保持寄存器
                            var data = await modbusService.ReadHoldingRegistersAsync(SettingVM.SlaveAddress, StartAddress, RegisterCount);
                            if (data == null || data.Length < RegisterCount)
                            {
                                //读取失败：连续计数，达到阈值判定离线（心跳机制）
                                _consecutiveReadFailures++;
                                if (_consecutiveReadFailures >= MaxConsecutiveFailures && IsConnected)
                                {
                                    IsConnected = false;
                                    Logger.Warn($"心跳检测：连续 {_consecutiveReadFailures} 次读取失败，判定从站离线");
                                }
                                continue;
                            }

                            //读取成功：清零失败计数
                            _consecutiveReadFailures = 0;

                            //数据转换（缩放系数从 SettingVM 读取）
                            double pressure = data[RegPressure] * SettingVM.PressureScale;
                            double temperature = data[RegTemperature] * SettingVM.TemperatureScale;
                            double humidity = data[RegHumidity] * SettingVM.HumidityScale;
                            double flow = data[RegFlow] * SettingVM.FlowScale;

                            //更新趋势图 + 实时卡片（UI 线程）
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                AddPoint(PressureValue, pressure);
                                AddPoint(TemperatureValue, temperature);
                                AddPoint(HumidityValue, humidity);
                                AddPoint(FlowValue, flow);
                                MonitorVM.CurrentPressure = pressure;
                                MonitorVM.CurrentTemperature = temperature;
                                MonitorVM.CurrentHumidity = humidity;
                                MonitorVM.CurrentFlow = flow;
                            });

                            //报警判定（阈值从 SettingVM 读取）
                            await CheckAlarm("压力", pressure, SettingVM.PressureHighLimit, "MPa", "紧急");
                            await CheckAlarm("温度", temperature, SettingVM.TemperatureHighLimit, "℃", "紧急");
                            await CheckAlarm("湿度", humidity, SettingVM.HumidityHighLimit, "%", "一般");
                            await CheckAlarm("流量", flow, SettingVM.FlowHighLimit, "m³/min", "一般");

                            //定时存库
                            _pollCounter++;
                            if (_pollCounter >= TrendSaveInterval)
                            {
                                _pollCounter = 0;
                                if (_dbReady)
                                {
                                    await dbService.InsertTrendDataAsync(DateTime.Now, pressure, temperature, humidity, flow);
                                    await SaveAllSettingsAsync();
                                }
                            }

                            Debug.WriteLine($"压力:{pressure:F2}MPa 温度:{temperature:F1}℃ 湿度:{humidity:F1}% 流量:{flow:F1}m³/min");
                        }
                        finally
                        {
                            _isReading = false;
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Logger.Error($"轮询异常：{ex.Message}");
                }

                try { await Task.Delay(200, ct); }
                catch (OperationCanceledException) { }
            }
        }

        /// <summary>
        /// 向趋势序列添加数据点并裁剪
        /// </summary>
        private void AddPoint(ChartValues<double> series, double value)
        {
            series.Add(value);
            if (series.Count > SettingVM.MaxTrendPoints)
                series.RemoveAt(0);
        }
        #endregion

        #region 报警判定
        /// <summary>
        /// 超限触发新报警，恢复正常时标记修复时间并从活动字典移除
        /// </summary>
        private async Task CheckAlarm(string itemName, double value, double highLimit, string unit, string level)
        {
            string key = itemName;
            bool isAlarm = value > highLimit;

            if (isAlarm && !_activeAlarms.ContainsKey(key))
            {
                //触发新报警
                var alarm = new AlarmModels
                {
                    Index = ++_alarmIndex,
                    DeviceId = SettingVM.DeviceId.ToString(),
                    ItemName = itemName,
                    Message = $"{itemName}值高：{value:F2}{unit}（阈值 {highLimit}{unit}）",
                    MesType = level,
                    MsgTime = DateTime.Now,
                    RemoveTime = null
                };

                _activeAlarms[key] = alarm;

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    AlarmList.Insert(0, alarm);
                    AlarmCount++;
                    //裁剪报警列表
                    while (AlarmList.Count > MaxAlarmRecords)
                        AlarmList.RemoveAt(AlarmList.Count - 1);
                });

                //报警声音
                if (TrendVM.AlarmSoundEnabled)
                {
                    try { System.Media.SystemSounds.Exclamation.Play(); } catch { }
                }

                Logger.Warn($"报警触发：{alarm.Message}");

                //写库
                if (_dbReady)
                {
                    int id = await dbService.InsertAlarmAsync(alarm);
                    alarm.DbId = id;
                }
            }
            else if (!isAlarm && _activeAlarms.TryGetValue(key, out var existing))
            {
                //恢复正常
                existing.RemoveTime = DateTime.Now;
                _activeAlarms.Remove(key);
                Logger.Info($"{itemName}恢复正常");

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    AlarmCount = Math.Max(0, AlarmCount - 1);
                });

                //更新数据库修复时间
                if (_dbReady && existing.DbId.HasValue)
                {
                    await dbService.UpdateAlarmRemoveTimeAsync(existing.DbId.Value, existing.RemoveTime.Value);
                }
            }
        }

        /// <summary>
        /// 由 AlarmViewModel 调用：清空已修复的报警记录
        /// </summary>
        public async Task ClearResolvedAlarmsAsync()
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                for (int i = AlarmList.Count - 1; i >= 0; i--)
                {
                    if (AlarmList[i].RemoveTime.HasValue)
                        AlarmList.RemoveAt(i);
                }
            });

            if (_dbReady)
                await dbService.DeleteResolvedAlarmsAsync();
        }
        #endregion

        #region 数据库
        private async Task InitializeDatabaseAsync()
        {
            try
            {
                await dbService.InitializeAsync();
                _dbReady = true;
                await ApplySettings();
                Logger.Info("数据库初始化完成");
            }
            catch (Exception ex)
            {
                Logger.Error($"数据库初始化失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 从数据库读取设置并应用到 SettingVM
        /// </summary>
        private async Task ApplySettings()
        {
            var settings = await dbService.LoadAllSettingsAsync();
            if (settings == null) return;

            if (settings.ContainsKey("TotalDeviceCount"))
                SettingVM.TotalDeviceCount = int.Parse(settings["TotalDeviceCount"]);
            if (settings.ContainsKey("DeviceId"))
                SettingVM.DeviceId = int.Parse(settings["DeviceId"]);
            if (settings.ContainsKey("PortName"))
                SettingVM.PortName = settings["PortName"];
            if (settings.ContainsKey("BaudRate"))
                SettingVM.BaudRate = int.Parse(settings["BaudRate"]);
            if (settings.ContainsKey("SlaveAddress"))
                SettingVM.SlaveAddress = byte.Parse(settings["SlaveAddress"]);
            if (settings.ContainsKey("PressureHighLimit"))
                SettingVM.PressureHighLimit = double.Parse(settings["PressureHighLimit"]);
            if (settings.ContainsKey("TemperatureHighLimit"))
                SettingVM.TemperatureHighLimit = double.Parse(settings["TemperatureHighLimit"]);
            if (settings.ContainsKey("HumidityHighLimit"))
                SettingVM.HumidityHighLimit = double.Parse(settings["HumidityHighLimit"]);
            if (settings.ContainsKey("FlowHighLimit"))
                SettingVM.FlowHighLimit = double.Parse(settings["FlowHighLimit"]);
            if (settings.ContainsKey("PollIntervalSeconds"))
                SettingVM.PollIntervalSeconds = int.Parse(settings["PollIntervalSeconds"]);
            if (settings.ContainsKey("MaxTrendPoints"))
                SettingVM.MaxTrendPoints = int.Parse(settings["MaxTrendPoints"]);
            if (settings.ContainsKey("PressureScale"))
                SettingVM.PressureScale = double.Parse(settings["PressureScale"]);
            if (settings.ContainsKey("TemperatureScale"))
                SettingVM.TemperatureScale = double.Parse(settings["TemperatureScale"]);
            if (settings.ContainsKey("HumidityScale"))
                SettingVM.HumidityScale = double.Parse(settings["HumidityScale"]);
            if (settings.ContainsKey("FlowScale"))
                SettingVM.FlowScale = double.Parse(settings["FlowScale"]);
            if (settings.ContainsKey("AlarmSoundEnabled"))
                TrendVM.AlarmSoundEnabled = bool.Parse(settings["AlarmSoundEnabled"]);
        }

        /// <summary>
        /// 将 SettingVM 和 TrendVM 的设置保存到数据库
        /// </summary>
        private async Task SaveAllSettingsAsync()
        {
            await dbService.SaveSettingAsync("TotalDeviceCount", SettingVM.TotalDeviceCount.ToString());
            await dbService.SaveSettingAsync("DeviceId", SettingVM.DeviceId.ToString());
            await dbService.SaveSettingAsync("PortName", SettingVM.PortName);
            await dbService.SaveSettingAsync("BaudRate", SettingVM.BaudRate.ToString());
            await dbService.SaveSettingAsync("SlaveAddress", SettingVM.SlaveAddress.ToString());
            await dbService.SaveSettingAsync("PressureHighLimit", SettingVM.PressureHighLimit.ToString());
            await dbService.SaveSettingAsync("TemperatureHighLimit", SettingVM.TemperatureHighLimit.ToString());
            await dbService.SaveSettingAsync("HumidityHighLimit", SettingVM.HumidityHighLimit.ToString());
            await dbService.SaveSettingAsync("FlowHighLimit", SettingVM.FlowHighLimit.ToString());
            await dbService.SaveSettingAsync("PollIntervalSeconds", SettingVM.PollIntervalSeconds.ToString());
            await dbService.SaveSettingAsync("MaxTrendPoints", SettingVM.MaxTrendPoints.ToString());
            await dbService.SaveSettingAsync("PressureScale", SettingVM.PressureScale.ToString());
            await dbService.SaveSettingAsync("TemperatureScale", SettingVM.TemperatureScale.ToString());
            await dbService.SaveSettingAsync("HumidityScale", SettingVM.HumidityScale.ToString());
            await dbService.SaveSettingAsync("FlowScale", SettingVM.FlowScale.ToString());
            await dbService.SaveSettingAsync("AlarmSoundEnabled", TrendVM.AlarmSoundEnabled.ToString());
        }
        #endregion

        #region 清理
        public void Cleanup()
        {
            _uiTimer.Stop();

            _cts.Cancel();
            try { _pollTask.Wait(TimeSpan.FromSeconds(5)); } catch { }
            _cts.Dispose();

            //退出前保存设置
            try { SaveAllSettingsAsync().Wait(TimeSpan.FromSeconds(3)); } catch { }

            try { modbusService.Dispose(); } catch { }
            try { dbService.Dispose(); } catch { }
            Logger.Info("系统退出");
        }
        #endregion
    }
}

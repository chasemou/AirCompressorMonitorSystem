# 空压机监控系统

基于 C# WPF + Modbus RTU 的空压机上位机监控系统。

## 技术栈

- **UI 框架**：WPF .NET 8
- **MVVM 工具**：CommunityToolkit.Mvvm（源生成器）
- **通信协议**：Modbus RTU（NModbus4 + System.IO.Ports）
- **图表控件**：LiveCharts.Wpf
- **数据存储**：SQLite（Microsoft.Data.Sqlite）
- **多线程**：Task.Run + Dispatcher.InvokeAsync

## 功能模块

| 模块 | 说明 |
|---|---|
| 监控界面 | 实时显示压力/温度/湿度/流量四项数据、设备统计、实时趋势图、报警列表、系统日志 |
| 趋势界面 | 实时趋势与历史趋势双模式，支持按日期范围查询历史采样数据 |
| 报警界面 | 超限自动报警、状态翻转触发、报警级别颜色区分、报警计数、修复时间记录 |
| 设置界面 | 设备参数、通信参数、报警阈值、数据采集参数配置，实时生效并持久化 |

## 架构设计

- **MVVM 分层**：MainViewModel 作为协调者，持有 Monitor/Trend/Alarm/Setting 四个子 ViewModel
- **多线程采集**：Task.Run 后台线程周期采集 Modbus 数据，Dispatcher.InvokeAsync 封送 UI 更新
- **心跳重连**：连接时 TestConnection 验证从站在线，运行中连续 3 次读取失败判定离线并自动重连
- **报警机制**：Dictionary 跟踪活动报警，状态翻转触发，避免持续超限重复报警
- **线程安全**：SemaphoreSlim 异步锁保证 SQLite 读写串行化，ObservableCollection 修改全部封送 UI 线程
- **数据持久化**：报警记录、趋势采样、系统设置三张表，异步 CRUD
- **日志系统**：三级日志（INFO/WARN/ERROR），内存环形队列 + 按天文件持久化

## 数据映射

| 寄存器 | 数据项 | 缩放系数 | 报警阈值 | 趋势量程 |
|---|---|---|---|---|
| 0 | 压力 (MPa) | 0.01 | 1.6 | 0~1.6 |
| 1 | 温度 (℃) | 1.0 | 120 | 0~150 |
| 2 | 湿度 (%) | 0.1 | 90 | 0~100 |
| 3 | 流量 (m³/min) | 0.1 | 20 | 0~20 |

## 运行环境

- .NET 8 运行时
- Windows 操作系统
- 串口设备（或 Modbus Slave 模拟器）
